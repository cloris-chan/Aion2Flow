using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime;

public static class SceneSinkFactory
{
    public static Func<IRuntimeObservationSink> CreateForLive(SceneLiveReadModel scene) =>
        scene.CreateSink;

    public static ReplaySinkHolder CreateForReplay()
    {
        var scene = new SceneLiveReadModel(
            DateTimeOffset.UnixEpoch,
            ReplaySinkTimeProvider.Instance);
        return new ReplaySinkHolder(scene.CreateSink(), scene);
    }

    private sealed class ReplaySinkTimeProvider : TimeProvider
    {
        public static ReplaySinkTimeProvider Instance { get; } = new();

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}

public sealed class SceneLiveReadModel : ILiveSceneCollectionPolicy
{
    private const int LiveJournalInitialCapacity = 4_096;
    private const int LiveCombatEventInitialCapacity = 4_096;
    private const int LiveCombatantInitialCapacity = 128;
    private const int LivePairInitialCapacity = 512;
    private readonly Lock _gate = new();
    private readonly Queue<SceneArchivePayload> _pendingArchives = [];
    private readonly Queue<SceneArchivePayload?> _pendingMapTransitions = [];
    private readonly HashSet<int> _seededBossSceneRuntimeStates = [];
    private readonly MapRuntimeObservationContext _mapRuntime = new();
    private readonly TimeProvider _timeProvider;
    private readonly ICombatOccurrenceObserver? _combatOccurrenceObserver;
    private readonly IAuraLifecycleObserver? _auraLifecycleObserver;
    private SceneReadModelOwner _owner;
    private long _nextFlushId;
    private SceneKind _kind;
    private CombatantStatisticsScope _combatantStatisticsScope = CombatantStatisticsScope.All;
    private BossSceneState _bossState;
    private bool _hydrateBossSceneRuntimeStateOnCombat;
    private long _frozenEndObservationOrdinalExclusive = -1;
    private SceneArchivePayload? _frozenArchive;
    private LiveScenePlaybackState? _playbackState;
    private int _playbackSourceCount;

    public Guid SessionId { get; private set; }
    public DateTimeOffset SessionStarted { get; private set; }
    public ObservedEventJournal Journal { get; }
    public SceneRuntimeClock Clock { get; }
    public RuntimeMetadataRegistry MetadataRegistry => Volatile.Read(ref _owner).MetadataRegistry;
    public SceneReadModelOwner Owner => Volatile.Read(ref _owner);
    public SceneKind Kind
    {
        get
        {
            lock (_gate)
                return _kind;
        }
    }
    public BossSceneState BossState
    {
        get
        {
            lock (_gate)
                return _bossState;
        }
    }
    public CombatantStatisticsScope CombatantStatisticsScope
    {
        get
        {
            lock (_gate)
                return _combatantStatisticsScope;
        }
    }
    public bool HasPendingProjectionChanges
    {
        get
        {
            lock (_gate)
                return _pendingArchives.Count > 0 ||
                       _pendingMapTransitions.Count > 0 ||
                       _owner.HasPendingProjectionChanges;
        }
    }

    public void SetCombatantStatisticsScope(CombatantStatisticsScope scope)
    {
        lock (_gate)
        {
            if (_combatantStatisticsScope == scope)
                return;

            _combatantStatisticsScope = scope;
            _owner.SetCombatantStatisticsScope(scope);
        }
    }

    public SceneLiveReadModel() : this(DateTimeOffset.Now)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted) : this(sessionStarted, TimeProvider.System)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted, TimeProvider timeProvider)
        : this(sessionStarted, timeProvider, null, null)
    {
    }

    public SceneLiveReadModel(
        DateTimeOffset sessionStarted,
        TimeProvider timeProvider,
        ICombatOccurrenceObserver? combatOccurrenceObserver,
        IAuraLifecycleObserver? auraLifecycleObserver)
    {
        _timeProvider = timeProvider;
        _combatOccurrenceObserver = combatOccurrenceObserver;
        _auraLifecycleObserver = auraLifecycleObserver;
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock = new SceneRuntimeClock(sessionStarted.ToUnixTimeMilliseconds());
        Journal = new ObservedEventJournal(LiveJournalInitialCapacity);
        _owner = CreateOwner(SessionId, sessionStarted, Journal.FirstObservationOrdinal);
        _kind = SceneKind.Standard;
        _bossState = BossSceneState.Waiting;
    }

    public void Reset()
    {
        lock (_gate)
        {
            ResetCore();
        }
    }

    public void Reset(Action reset)
    {
        lock (_gate)
        {
            reset();
            ResetCore();
        }
    }

    public void Reset(Func<DateTimeOffset> resolveSessionStarted)
    {
        lock (_gate)
        {
            ResetCore(resolveSessionStarted());
        }
    }

    public SceneArchivePayload ArchiveAndReset(Func<DateTimeOffset> resolveSessionStarted)
    {
        ArgumentNullException.ThrowIfNull(resolveSessionStarted);

        lock (_gate)
        {
            var archive = CreateArchivePayloadCore();
            ResetCore(
                resolveSessionStarted(),
                _kind,
                Clock.NextObservationOrdinal,
                archive);
            return archive;
        }
    }

    public long NextFlushId() => Interlocked.Increment(ref _nextFlushId);

    public IRuntimeObservationSink Synchronize(IRuntimeObservationSink sink) => new SynchronizedRuntimeObservationSink(sink, _gate);

    internal IRuntimeObservationSink CreateSink()
    {
        var journaling = new JournalingRuntimeObservationSink(
            Journal,
            Clock,
            () => SessionId,
            NextFlushId,
            this,
            _mapRuntime);
        return Synchronize(journaling);
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            var frame = Owner.CreateFrame(detailCombatantId, forceDetailRefresh);
            RefreshBossStateCore();
            return frame;
        }
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId, ICombatDetailEventWriter detailWriter, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            var frame = Owner.CreateFrame(detailCombatantId, detailWriter, forceDetailRefresh);
            RefreshBossStateCore();
            return frame;
        }
    }

    public SceneArchivePayload? ChangeKind(SceneKind kind, DateTimeOffset sessionStarted, bool archiveCurrent)
    {
        lock (_gate)
        {
            SceneArchivePayload? archive = archiveCurrent ? CreateArchivePayloadCore() : null;
            if (archive is { } payload)
                FinalizePlaybackStateCore(payload);
            ResetCore(sessionStarted, kind);
            return archive;
        }
    }

    public SceneArchivePayload? ChangeKind(SceneKind kind, Func<DateTimeOffset> resolveSessionStarted, bool archiveCurrent)
    {
        lock (_gate)
        {
            SceneArchivePayload? archive = archiveCurrent ? CreateArchivePayloadCore() : null;
            if (archive is { } payload)
                FinalizePlaybackStateCore(payload);
            ResetCore(resolveSessionStarted(), kind);
            return archive;
        }
    }

    public bool TryDequeuePendingArchive(out SceneArchivePayload payload)
    {
        lock (_gate)
        {
            if (_pendingArchives.Count == 0)
            {
                payload = default!;
                return false;
            }

            payload = _pendingArchives.Dequeue();
            return true;
        }
    }

    public SceneArchivePayload CreateArchivePayload()
    {
        lock (_gate)
            return CreateArchivePayloadCore();
    }

    public bool TryDequeueMapTransition(out SceneArchivePayload? payload)
    {
        lock (_gate)
        {
            if (_pendingMapTransitions.Count == 0)
            {
                payload = null;
                return false;
            }

            payload = _pendingMapTransitions.Dequeue();
            return true;
        }
    }

    public LiveScenePlaybackSource CreatePlaybackSource()
    {
        lock (_gate)
            return CreatePlaybackSourceCore();
    }

    public LiveScenePlaybackSource CreatePlaybackSource(out RuntimeMetadataRegistry metadataRegistry)
    {
        lock (_gate)
        {
            metadataRegistry = _owner.MetadataRegistry;
            return CreatePlaybackSourceCore();
        }
    }

    internal SceneCombatSnapshot CreatePlaybackSnapshot(LiveScenePlaybackState state)
    {
        lock (_gate)
        {
            return ReferenceEquals(_playbackState, state) && state.EncounterId == SessionId
                ? Owner.CreateSnapshot()
                : state.GetFrozenSnapshot();
        }
    }

    internal void ReleasePlaybackSource(LiveScenePlaybackState state)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_playbackState, state))
                return;

            _playbackSourceCount--;
            if (_playbackSourceCount > 0)
                return;

            state.StopGrowing(Journal.NextObservationOrdinal);
            _playbackState = null;
            _playbackSourceCount = 0;
        }
    }

    private void ResetCore() => ResetCore(DateTimeOffset.Now);

    public void Reset(DateTimeOffset sessionStarted)
    {
        lock (_gate)
        {
            ResetCore(sessionStarted);
        }
    }

    private void ResetCore(DateTimeOffset sessionStarted)
        => ResetCore(sessionStarted, _kind, Clock.NextObservationOrdinal);

    private void ResetCore(DateTimeOffset sessionStarted, SceneKind kind)
        => ResetCore(sessionStarted, kind, Clock.NextObservationOrdinal);

    private void ResetCore(
        DateTimeOffset sessionStarted,
        SceneKind kind,
        long startOrdinal,
        SceneArchivePayload? playbackPayload = null)
    {
        var shouldHydrateBossSceneRuntimeState =
            _kind == SceneKind.Standard &&
            kind == SceneKind.Standard;

        FinalizePlaybackStateCore(playbackPayload);
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock.Reset(sessionStarted);
        _kind = kind;
        _bossState = BossSceneState.Waiting;
        _hydrateBossSceneRuntimeStateOnCombat = shouldHydrateBossSceneRuntimeState;
        _frozenEndObservationOrdinalExclusive = -1;
        _frozenArchive = null;
        _seededBossSceneRuntimeStates.Clear();
        Owner.ResetCombat(
            SessionId,
            startOrdinal,
            sessionStarted,
            kind,
            trackBossFocus: kind == SceneKind.Standard);
    }

    void ILiveSceneCollectionPolicy.StartMapContext(in PacketObservationSource packet, uint mapId)
    {
        if (!_mapRuntime.HasMapScope && !_owner.HasCombatData)
        {
            _pendingMapTransitions.Enqueue(null);
            return;
        }

        var boundaryOrdinal = Clock.NextObservationOrdinal;
        var archive = _owner.CreateMapBoundaryArchive(boundaryOrdinal);
        var retainedArchive = archive.Snapshot.Combatants.Count > 0
            ? archive
            : null;

        FinalizePlaybackStateCore(archive);
        SessionId = Guid.NewGuid();
        SessionStarted = packet.CaptureTimestampMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(packet.CaptureTimestampMilliseconds)
            : _timeProvider.GetLocalNow();
        Clock.Reset(SessionStarted);
        _bossState = BossSceneState.Waiting;
        _hydrateBossSceneRuntimeStateOnCombat = false;
        _frozenEndObservationOrdinalExclusive = -1;
        _frozenArchive = null;
        _seededBossSceneRuntimeStates.Clear();
        Volatile.Write(ref _owner, CreateOwner(SessionId, SessionStarted, boundaryOrdinal));
        _pendingMapTransitions.Enqueue(retainedArchive);
    }

    bool ILiveSceneCollectionPolicy.ShouldAppendCombat(in PacketObservationSource packet, int sourceId, int targetId, IRuntimeObservationSink sink)
    {
        if (_kind == SceneKind.Standard)
        {
            if (_hydrateBossSceneRuntimeStateOnCombat &&
                TryResolveFocusTargetPlayerCombat(sourceId, targetId, out var activeTargetId, out _))
            {
                SeedBossSceneRuntimeState(in packet, activeTargetId, sink);
                if (_seededBossSceneRuntimeStates.Contains(activeTargetId))
                    _hydrateBossSceneRuntimeStateOnCombat = false;
            }

            return true;
        }

        RefreshBossStateCore();
        if (_bossState == BossSceneState.Recording)
        {
            if (TryResolveFocusTargetPlayerCombat(sourceId, targetId, out var activeTargetId, out var recordingActivitySourceId))
            {
                SeedBossSceneRuntimeState(in packet, activeTargetId, sink);
                Owner.ObserveBossCombatTrigger(activeTargetId, recordingActivitySourceId, ResolveObservedAtMilliseconds(in packet));
            }
            return true;
        }

        Owner.Refresh();
        if (!TryResolveFocusTargetPlayerCombat(sourceId, targetId, out var focusTargetId, out var activitySourceId))
            return false;

        if (_bossState == BossSceneState.Frozen)
        {
            if (Owner.EntityVitals.TryGet(focusTargetId, out var focusTargetVital) && focusTargetVital.CurrentHp == 0)
                return false;

            _pendingArchives.Enqueue(GetFrozenArchivePayloadCore());
        }

        var started = packet.CaptureTimestampMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(packet.CaptureTimestampMilliseconds)
            : DateTimeOffset.Now;
        ResetCore(started, SceneKind.Boss);
        _bossState = BossSceneState.Recording;
        SeedBossSceneRuntimeState(in packet, focusTargetId, sink);
        Owner.SetBossFocusTracking(true);
        Owner.ObserveBossCombatTrigger(focusTargetId, activitySourceId, 0);
        return true;
    }

    private void SeedBossSceneRuntimeState(in PacketObservationSource packet, int instanceId, IRuntimeObservationSink sink)
    {
        if (instanceId <= 0 ||
            _seededBossSceneRuntimeStates.Contains(instanceId) ||
            !sink.TryGetNpcRuntimeState(instanceId, out var state))
        {
            return;
        }

        _seededBossSceneRuntimeStates.Add(instanceId);
        sink.SeedNpcRuntimeState(in packet, instanceId, in state);
    }

    bool ILiveSceneCollectionPolicy.ShouldAppendExtendedObservation() =>
        _kind == SceneKind.Standard || _bossState == BossSceneState.Recording;

    bool ILiveSceneCollectionPolicy.ShouldAppendEntityVitalObservation() =>
        _kind == SceneKind.Standard || _bossState == BossSceneState.Recording;

    void ILiveSceneCollectionPolicy.OnBossMetadataChanged()
    {
        if (_kind == SceneKind.Boss && _bossState == BossSceneState.Recording)
            RefreshBossStateCore();
    }

    private void RefreshBossStateCore()
    {
        if (_kind != SceneKind.Boss || _bossState != BossSceneState.Recording)
            return;

        if (Owner.GetActiveBossFocusState() == BossFocusGroupState.ActiveOrUnknown)
            return;

        FreezeBossSceneCore();
    }

    private void FreezeBossSceneCore()
    {
        if (_bossState == BossSceneState.Frozen)
            return;

        _frozenEndObservationOrdinalExclusive = Owner.AppliedNextObservationOrdinal;
        _frozenArchive = Owner.CreateArchivePayload(_frozenEndObservationOrdinalExclusive);
        FinalizePlaybackStateCore(_frozenArchive);
        _bossState = BossSceneState.Frozen;
        Owner.SetBossFocusTracking(false);
    }

    private void FinalizePlaybackStateCore(SceneArchivePayload? payload = null)
    {
        var state = _playbackState;
        if (state is null)
            return;

        state.Freeze(payload ?? CreateArchivePayloadCore());
        _playbackState = null;
        _playbackSourceCount = 0;
    }

    private SceneArchivePayload CreateArchivePayloadCore()
    {
        if (_kind == SceneKind.Boss &&
            _bossState == BossSceneState.Frozen &&
            _frozenEndObservationOrdinalExclusive >= 0)
        {
            return GetFrozenArchivePayloadCore();
        }

        return Owner.CreateArchivePayload();
    }

    private SceneArchivePayload GetFrozenArchivePayloadCore() =>
        _frozenArchive ?? throw new InvalidOperationException("Frozen boss scene has no archive payload.");

    private LiveScenePlaybackSource CreatePlaybackSourceCore()
    {
        if (_kind == SceneKind.Boss && _bossState == BossSceneState.Frozen)
            return new LiveScenePlaybackSource(LiveScenePlaybackState.CreateFrozen(this, GetFrozenArchivePayloadCore()));

        var state = _playbackState;
        if (state is null)
        {
            var snapshot = _owner.CreateSnapshot();
            state = new LiveScenePlaybackState(
                this,
                SessionId,
                SessionStarted,
                snapshot,
                Journal,
                _owner.SceneStartObservationOrdinal,
                _owner.AppliedNextObservationOrdinal);
            _playbackState = state;
        }

        _playbackSourceCount++;
        return new LiveScenePlaybackSource(state);
    }

    private bool TryResolveFocusTargetPlayerCombat(int sourceId, int targetId, out int focusTargetId, out int activitySourceId)
    {
        var sourceIsFocusTarget = IsFocusTarget(sourceId);
        var targetIsFocusTarget = IsFocusTarget(targetId);
        if (sourceIsFocusTarget && IsBossFocusActivitySource(targetId))
        {
            focusTargetId = sourceId;
            activitySourceId = targetId;
            return true;
        }

        if (targetIsFocusTarget && IsBossFocusActivitySource(sourceId))
        {
            focusTargetId = targetId;
            activitySourceId = sourceId;
            return true;
        }

        focusTargetId = 0;
        activitySourceId = 0;
        return false;
    }

    private long ResolveObservedAtMilliseconds(in PacketObservationSource packet) =>
        packet.CaptureTimestampMilliseconds > 0
            ? Math.Max(0, packet.CaptureTimestampMilliseconds - SessionStarted.ToUnixTimeMilliseconds())
            : 0;

    private bool IsFocusTarget(int entityId) =>
        entityId > 0 &&
        Owner.Entities.TryGet(entityId, out var entity) &&
        BossModeFocusTargets.IsFocusTarget(entity.Kind);

    private bool IsBossFocusActivitySource(int entityId) =>
        Owner.IsBossFocusActivitySource(entityId);

    private SceneReadModelOwner CreateOwner(
        Guid sessionId,
        DateTimeOffset sessionStarted,
        long startObservationOrdinal)
    {
        var owner = new SceneReadModelOwner(
            Journal,
            sessionId,
            sessionStarted,
            new EntityStore(),
            new SceneBoundaryStore(),
            new RuntimeMetadataRegistry(),
            new CombatStore(LiveCombatEventInitialCapacity, LiveCombatantInitialCapacity, LivePairInitialCapacity),
            _timeProvider,
            _combatOccurrenceObserver,
            _auraLifecycleObserver,
            startObservationOrdinal);

        if (_kind != SceneKind.Standard)
        {
            owner.ResetCombat(
                sessionId,
                startObservationOrdinal,
                sessionStarted,
                _kind,
                trackBossFocus: false);
        }

        owner.SetCombatantStatisticsScope(_combatantStatisticsScope);

        return owner;
    }
}

public sealed class ReplaySinkHolder(
    IRuntimeObservationSink sink,
    SceneLiveReadModel scene) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;

    public ObservedEventJournal Journal => scene.Journal;

    public SceneReadModelOwner Owner => scene.Owner;

    public void Dispose() { }
}
