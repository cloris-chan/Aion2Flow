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
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var sceneStarted = DateTimeOffset.UtcNow;
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, sceneId);
        var metadataRegistry = new RuntimeMetadataRegistry();
        return new ReplaySinkHolder(journaling, journal, new SceneReadModelOwner(journal, sceneId, sceneStarted, metadataRegistry));
    }
}

public sealed class SceneLiveReadModel : ILiveSceneCollectionPolicy
{
    private const int LiveJournalInitialCapacity = 4_096;
    private const int LiveCombatEventInitialCapacity = 4_096;
    private const int LiveCombatantInitialCapacity = 128;
    private const int LivePairInitialCapacity = 512;
    private readonly Lock _gate = new();
    private readonly Queue<SceneArchiveCapture> _pendingArchives = [];
    private readonly HashSet<int> _seededBossSceneRuntimeStates = [];
    private long _nextFlushId;
    private SceneKind _kind;
    private BossSceneState _bossState;
    private long _frozenEndObservationOrdinalExclusive = -1;
    private SceneArchiveCapture? _frozenArchive;
    private LiveScenePlaybackState? _playbackState;
    private int _playbackSourceCount;

    public Guid SessionId { get; private set; }
    public DateTimeOffset SessionStarted { get; private set; }
    public ObservedEventJournal Journal { get; }
    public SceneRuntimeClock Clock { get; }
    public RuntimeMetadataRegistry MetadataRegistry { get; } = new();
    public SceneReadModelOwner Owner { get; }
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
    public bool HasPendingProjectionChanges
    {
        get
        {
            lock (_gate)
                return _pendingArchives.Count > 0 || Owner.HasPendingProjectionChanges;
        }
    }

    public SceneLiveReadModel() : this(DateTimeOffset.Now)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted) : this(sessionStarted, TimeProvider.System)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted, TimeProvider timeProvider)
    {
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock = new SceneRuntimeClock(sessionStarted.ToUnixTimeMilliseconds());
        Journal = new ObservedEventJournal(LiveJournalInitialCapacity);
        Owner = new SceneReadModelOwner(
            Journal,
            SessionId,
            sessionStarted,
            new EntityStore(),
            new SceneBoundaryStore(),
            MetadataRegistry,
            new CombatStore(LiveCombatEventInitialCapacity, LiveCombatantInitialCapacity, LivePairInitialCapacity),
            timeProvider);
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

    public long NextFlushId() => Interlocked.Increment(ref _nextFlushId);

    public IRuntimeObservationSink Synchronize(IRuntimeObservationSink sink) => new SynchronizedRuntimeObservationSink(sink, _gate);

    internal IRuntimeObservationSink CreateSink()
    {
        var journaling = new JournalingRuntimeObservationSink(Journal, Clock, () => SessionId, NextFlushId, this);
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

    public SceneArchiveCapture? ChangeKind(SceneKind kind, DateTimeOffset sessionStarted, bool archiveCurrent)
    {
        lock (_gate)
        {
            SceneArchiveCapture? archive = archiveCurrent ? CreateArchiveCaptureCore() : null;
            if (archive is { } capture)
                FinalizePlaybackStateCore(capture);
            ResetCore(sessionStarted, kind);
            return archive;
        }
    }

    public SceneArchiveCapture? ChangeKind(SceneKind kind, Func<DateTimeOffset> resolveSessionStarted, bool archiveCurrent)
    {
        lock (_gate)
        {
            SceneArchiveCapture? archive = archiveCurrent ? CreateArchiveCaptureCore() : null;
            if (archive is { } capture)
                FinalizePlaybackStateCore(capture);
            ResetCore(resolveSessionStarted(), kind);
            return archive;
        }
    }

    public bool TryDequeuePendingArchive(out SceneArchiveCapture archive)
    {
        lock (_gate)
        {
            if (_pendingArchives.Count == 0)
            {
                archive = default;
                return false;
            }

            archive = _pendingArchives.Dequeue();
            return true;
        }
    }

    public SceneArchiveCapture CreateArchiveCapture()
    {
        lock (_gate)
            return CreateArchiveCaptureCore();
    }

    public LiveScenePlaybackSource CreatePlaybackSource()
    {
        lock (_gate)
        {
            if (_kind == SceneKind.Boss && _bossState == BossSceneState.Frozen)
                return new LiveScenePlaybackSource(LiveScenePlaybackState.CreateFrozen(this, GetFrozenArchiveCore()));

            var state = _playbackState;
            if (state is null)
            {
                var snapshot = Owner.CreateSnapshot();
                state = new LiveScenePlaybackState(
                    this,
                    SessionId,
                    SessionStarted,
                    snapshot,
                    Journal,
                    Owner.SceneStartObservationOrdinal,
                    Owner.AppliedNextObservationOrdinal);
                _playbackState = state;
            }

            _playbackSourceCount++;
            return new LiveScenePlaybackSource(state);
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
        => ResetCore(sessionStarted, _kind);

    private void ResetCore(DateTimeOffset sessionStarted, SceneKind kind)
    {
        FinalizePlaybackStateCore();
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock.Reset(sessionStarted);
        _kind = kind;
        _bossState = BossSceneState.Waiting;
        _frozenEndObservationOrdinalExclusive = -1;
        _frozenArchive = null;
        _seededBossSceneRuntimeStates.Clear();
        Owner.ResetCombat(
            SessionId,
            Clock.NextObservationOrdinal,
            sessionStarted,
            kind,
            trackBossFocus: kind == SceneKind.Standard);
    }

    bool ILiveSceneCollectionPolicy.ShouldAppendCombat(in PacketObservationSource packet, int sourceId, int targetId, IRuntimeObservationSink sink)
    {
        if (_kind == SceneKind.Standard)
            return true;

        RefreshBossStateCore();
        if (_bossState == BossSceneState.Recording)
        {
            if (TryResolveFocusTargetPlayerCombat(sourceId, targetId, out var activeTargetId))
            {
                SeedBossSceneRuntimeState(in packet, activeTargetId, sink);
                Owner.ObserveBossCombatTrigger(activeTargetId, ResolveObservedAtMilliseconds(in packet));
            }
            return true;
        }

        Owner.Refresh();
        if (!TryResolveFocusTargetPlayerCombat(sourceId, targetId, out var focusTargetId))
            return false;

        if (_bossState == BossSceneState.Frozen)
        {
            if (Owner.EntityVitals.TryGet(focusTargetId, out var focusTargetVital) && focusTargetVital.CurrentHp == 0)
                return false;

            _pendingArchives.Enqueue(GetFrozenArchiveCore());
        }

        var started = packet.CaptureTimestampMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(packet.CaptureTimestampMilliseconds)
            : DateTimeOffset.Now;
        ResetCore(started, SceneKind.Boss);
        _bossState = BossSceneState.Recording;
        SeedBossSceneRuntimeState(in packet, focusTargetId, sink);
        Owner.SetBossFocusTracking(true);
        Owner.ObserveBossCombatTrigger(focusTargetId, 0);
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
        _frozenArchive = Owner.CreateArchiveCapture(_frozenEndObservationOrdinalExclusive);
        FinalizePlaybackStateCore(_frozenArchive.Value);
        _bossState = BossSceneState.Frozen;
        Owner.SetBossFocusTracking(false);
    }

    private void FinalizePlaybackStateCore(SceneArchiveCapture? capture = null)
    {
        var state = _playbackState;
        if (state is null)
            return;

        state.Freeze(capture ?? CreateArchiveCaptureCore());
        _playbackState = null;
        _playbackSourceCount = 0;
    }

    private SceneArchiveCapture CreateArchiveCaptureCore()
    {
        if (_kind == SceneKind.Boss &&
            _bossState == BossSceneState.Frozen &&
            _frozenEndObservationOrdinalExclusive >= 0)
        {
            return GetFrozenArchiveCore();
        }

        return Owner.CreateArchiveCapture();
    }

    private SceneArchiveCapture GetFrozenArchiveCore() =>
        _frozenArchive ?? throw new InvalidOperationException("Frozen boss scene has no archive capture.");

    private bool TryResolveFocusTargetPlayerCombat(int sourceId, int targetId, out int focusTargetId)
    {
        var sourceIsFocusTarget = IsFocusTarget(sourceId);
        var targetIsFocusTarget = IsFocusTarget(targetId);
        if (sourceIsFocusTarget && IsPlayerSide(targetId))
        {
            focusTargetId = sourceId;
            return true;
        }

        if (targetIsFocusTarget && IsPlayerSide(sourceId))
        {
            focusTargetId = targetId;
            return true;
        }

        focusTargetId = 0;
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

    private bool IsPlayerSide(int entityId)
    {
        if (entityId <= 0)
            return false;

        var currentId = entityId;
        for (var depth = 0; depth < 4; depth++)
        {
            if (Owner.MetadataRegistry.TryGetPcMetadata(currentId, out _))
                return true;

            if (!Owner.Entities.TryGet(currentId, out var entity))
                return true;

            if (entity.IsPlayer || entity.CharacterClass is not null and not CharacterClass.None)
                return true;

            if (entity.Kind == NpcKind.Summon && entity.OwnerEntityId is int ownerId && ownerId > 0 && ownerId != currentId)
            {
                currentId = ownerId;
                continue;
            }

            return entity.Kind == NpcKind.Unknown && entity.NpcCode is null;
        }

        return false;
    }
}

public readonly struct ReplaySinkHolder(IRuntimeObservationSink sink, ObservedEventJournal journal, SceneReadModelOwner owner) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;

    public ObservedEventJournal Journal { get; } = journal;

    public SceneReadModelOwner Owner { get; } = owner;

    public void Dispose() { }
}
