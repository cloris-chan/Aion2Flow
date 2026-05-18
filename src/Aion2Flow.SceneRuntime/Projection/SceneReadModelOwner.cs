using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
{
    private const long BossFocusVisibilityTimeoutMilliseconds = 2_000;
    private const long RuntimeCheckpointIntervalMilliseconds = 5_000;
    private readonly Lock _gate = new();
    private DomainEventApplier _applier = new(entities, boundary, metadataRegistry, combat);
    private readonly SceneCombatSnapshotBuilder _snapshotBuilder = new();
    private readonly Dictionary<int, CombatDetailSubscription> _detailSubscriptions = [];
    private readonly Dictionary<int, CombatDetailDelta> _lastDetailDeltas = [];
    private readonly List<SceneRuntimeCheckpoint> _runtimeCheckpoints = [];
    private readonly ObservedEventEnvelope[] _entryBuffer = new ObservedEventEnvelope[256];
    private JournalCursor _cursor = journal.CreateCursor(0);
    private long _appliedBatchOrdinal = -1;
    private long _lastAppliedTimestampMilliseconds;
    private long _nextRuntimeCheckpointAtMilliseconds;
    private bool _hasRuntimeCheckpointSchedule;
    private SnapshotCacheKey _snapshotCacheKey;
    private SceneCombatSnapshot? _snapshotCache;
    private ProjectionCacheStats _projectionCacheStats;

    public SceneReadModelOwner(ObservedEventJournal journal) : this(journal, Guid.NewGuid(), DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId) : this(journal, encounterId, DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted) : this(journal, encounterId, sceneStarted, new EntityStore(), new SceneBoundaryStore(), new RuntimeMetadataRegistry(), new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, RuntimeMetadataRegistry metadataRegistry) : this(journal, encounterId, sceneStarted, new EntityStore(), new SceneBoundaryStore(), metadataRegistry, new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat) : this(journal, encounterId, sceneStarted, entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat) : this(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat) : this(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, boundary, metadataRegistry, combat)
    {
    }

    public EntityStore Entities => entities;
    public SceneBoundaryStore Boundary => boundary;
    public RuntimeMetadataRegistry MetadataRegistry => metadataRegistry;
    public CombatStore Combat => combat;
    public DomainEventApplier Applier => _applier;
    public BossFocusStore BossFocus => _applier.BossFocus;
    public Guid EncounterId { get; private set; } = encounterId;
    public DateTimeOffset SceneStarted { get; private set; } = sceneStarted;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;
    public ProjectionCacheStats ProjectionCacheStats => _projectionCacheStats;
    public IReadOnlyList<SceneRuntimeCheckpoint> RuntimeCheckpoints => _runtimeCheckpoints;

    internal SceneReadModelOwner(
        ObservedEventJournal journal,
        Guid encounterId,
        DateTimeOffset sceneStarted,
        EntityStore entities,
        SceneBoundaryStore boundary,
        RuntimeMetadataRegistry metadataRegistry,
        CombatStore combat,
        DomainEventApplier applier,
        JournalCursor cursor,
        long appliedObservationOrdinal,
        long appliedBatchOrdinal,
        IEnumerable<SceneRuntimeCheckpoint>? checkpoints = null)
        : this(journal, encounterId, sceneStarted, entities, boundary, metadataRegistry, combat)
    {
        _applier = applier;
        _cursor = cursor;
        AppliedObservationOrdinal = appliedObservationOrdinal;
        _appliedBatchOrdinal = appliedBatchOrdinal;
        if (checkpoints is not null)
        {
            foreach (var checkpoint in checkpoints)
                _runtimeCheckpoints.Add(checkpoint.DeepClone());
            if (_runtimeCheckpoints.Count > 0)
            {
                _hasRuntimeCheckpointSchedule = true;
                _nextRuntimeCheckpointAtMilliseconds = _runtimeCheckpoints[^1].CapturedAtMilliseconds + RuntimeCheckpointIntervalMilliseconds;
                _lastAppliedTimestampMilliseconds = _runtimeCheckpoints[^1].CapturedAtMilliseconds;
            }
        }
    }

    public SceneCombatSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateSnapshotCore();
        }
    }

    public SceneCombatSnapshot CreateSnapshotAt(long observedAtMilliseconds)
    {
        lock (_gate)
        {
            RefreshCoreUntil(observedAtMilliseconds);
            return CreateSnapshotCore();
        }
    }

    public SceneReadModelFrame CreateFrameAt(long observedAtMilliseconds, int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCoreUntil(observedAtMilliseconds);
            return CreateFrameWithMarkers(detailCombatantId, forceDetailRefresh, null);
        }
    }

    public SceneRuntimeCheckpoint CreateRuntimeCheckpoint()
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateRuntimeCheckpointCore(_lastAppliedTimestampMilliseconds, _cursor);
        }
    }

    public static SceneReadModelOwner RestoreFromCheckpoint(ObservedEventJournal journal, SceneRuntimeCheckpoint checkpoint)
    {
        var entities = checkpoint.Entities.DeepClone();
        var boundary = checkpoint.Boundary.DeepClone();
        var metadataRegistry = checkpoint.MetadataRegistry.DeepClone();
        var combat = checkpoint.Combat.DeepClone();
        var applier = checkpoint.Applier.DeepClone(entities, boundary, metadataRegistry, combat);
        return new SceneReadModelOwner(
            journal,
            checkpoint.EncounterId,
            checkpoint.SceneStarted,
            entities,
            boundary,
            metadataRegistry,
            combat,
            applier,
            checkpoint.Cursor,
            checkpoint.AppliedObservationOrdinal,
            checkpoint.AppliedBatchOrdinal,
            [checkpoint]);
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateFrameWithMarkers(detailCombatantId, forceDetailRefresh, null);
        }
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId, ICombatDetailEventWriter detailWriter, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateFrameWithMarkers(detailCombatantId, forceDetailRefresh, detailWriter);
        }
    }

    public SceneArchivePayload CreateArchivePayload(SceneCombatSnapshot snapshot)
    {
        lock (_gate)
        {
            RefreshCore();
            return SceneArchivePayload.CreateLocked(
                snapshot,
                SceneStarted,
                entities,
                boundary,
                metadataRegistry,
                _applier.BossFocus,
                CreateAdapter(),
                journal.ToArray(),
                CreateArchiveCheckpoints());
        }
    }

    public SceneArchivePayload CreateArchivePayload()
    {
        lock (_gate)
        {
            RefreshCore();
            var snapshot = CreateSnapshotCore();
            return SceneArchivePayload.CreateLocked(
                snapshot,
                SceneStarted,
                entities,
                boundary,
                metadataRegistry,
                _applier.BossFocus,
                CreateAdapter(),
                journal.ToArray(),
                CreateArchiveCheckpoints());
        }
    }

    public T ReadLocked<T>(Func<EntityStore, SceneBoundaryStore, RuntimeMetadataRegistry, CombatStore, T> reader)
    {
        lock (_gate)
        {
            RefreshCore();
            return reader(entities, boundary, metadataRegistry, combat);
        }
    }

    public CombatDetailDelta CreateDetailDelta(SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            var adapter = CreateAdapter();
            return CreateDetailDeltaCore(adapter, snapshot, combatantId, forceRefresh);
        }
    }

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateAdapter().CreateSkillBreakdown(snapshot, combatantId);
        }
    }

    public void Refresh()
    {
        lock (_gate)
        {
            RefreshCore();
        }
    }

    private void RefreshCore()
    {
        while (true)
        {
            var result = journal.CopyEntries(_cursor, _entryBuffer);
            if (result.Count == 0)
                break;

            var entries = _entryBuffer.AsSpan(0, result.Count);
            foreach (ref readonly var entry in entries)
            {
                EnsureRuntimeCheckpointSchedule(in entry);
                _applier.ApplyEntry(in entry);
                AppliedObservationOrdinal++;
                if (entry.Raw.TimestampMilliseconds > 0)
                    _lastAppliedTimestampMilliseconds = Math.Max(_lastAppliedTimestampMilliseconds, entry.Raw.TimestampMilliseconds);
                MaybeCaptureRuntimeCheckpointAfterEntry(in entry);
            }

            _cursor = result.Cursor;
        }

        var completedBatch = journal.LastCompletedBatchOrdinal;
        if (completedBatch > _appliedBatchOrdinal)
        {
            _applier.CompleteBatch(completedBatch);
            _appliedBatchOrdinal = completedBatch;
        }
    }

    private void RefreshCoreUntil(long observedAtMilliseconds)
    {
        var maxAppliedBatchOrdinal = _appliedBatchOrdinal;
        while (true)
        {
            var result = journal.CopyEntries(_cursor, _entryBuffer);
            if (result.Count == 0)
                break;

            var entries = _entryBuffer.AsSpan(0, result.Count);
            var appliedAny = false;
            JournalCursor cursor = _cursor;
            foreach (ref readonly var entry in entries)
            {
                var timestamp = entry.Raw.TimestampMilliseconds;
                if (timestamp > 0 && timestamp > observedAtMilliseconds)
                    break;

                _applier.ApplyEntry(in entry);
                AppliedObservationOrdinal++;
                cursor = new JournalCursor(entry.Stamp.ObservationOrdinal + 1);
                maxAppliedBatchOrdinal = Math.Max(maxAppliedBatchOrdinal, entry.Stamp.BatchOrdinal);
                if (timestamp > 0)
                    _lastAppliedTimestampMilliseconds = Math.Max(_lastAppliedTimestampMilliseconds, timestamp);
                appliedAny = true;
            }

            _cursor = cursor;
            if (!appliedAny || _cursor.NextObservationOrdinal < result.Cursor.NextObservationOrdinal)
                break;
        }

        if (maxAppliedBatchOrdinal > _appliedBatchOrdinal)
        {
            _applier.CompleteBatch(maxAppliedBatchOrdinal);
            _appliedBatchOrdinal = maxAppliedBatchOrdinal;
        }
    }

    private void EnsureRuntimeCheckpointSchedule(in ObservedEventEnvelope entry)
    {
        var timestamp = entry.Raw.TimestampMilliseconds;
        if (timestamp <= 0 || _hasRuntimeCheckpointSchedule)
            return;

        _hasRuntimeCheckpointSchedule = true;
        _nextRuntimeCheckpointAtMilliseconds = timestamp + RuntimeCheckpointIntervalMilliseconds;
        _runtimeCheckpoints.Add(CreateRuntimeCheckpointCore(timestamp, new JournalCursor(entry.Stamp.ObservationOrdinal)));
    }

    private void MaybeCaptureRuntimeCheckpointAfterEntry(in ObservedEventEnvelope entry)
    {
        var timestamp = entry.Raw.TimestampMilliseconds;
        if (timestamp <= 0 || !_hasRuntimeCheckpointSchedule)
            return;

        if (timestamp < _nextRuntimeCheckpointAtMilliseconds)
            return;

        _runtimeCheckpoints.Add(CreateRuntimeCheckpointCore(timestamp, new JournalCursor(entry.Stamp.ObservationOrdinal + 1)));
        while (_nextRuntimeCheckpointAtMilliseconds <= timestamp)
            _nextRuntimeCheckpointAtMilliseconds += RuntimeCheckpointIntervalMilliseconds;
    }

    private SceneReadModelFrame CreateFrameWithMarkers(int detailCombatantId, bool forceDetailRefresh, ICombatDetailEventWriter? detailWriter)
        => CreateFrameCore(detailCombatantId, forceDetailRefresh, detailWriter);

    private SceneReadModelFrame CreateFrameCore(int detailCombatantId, bool forceDetailRefresh, ICombatDetailEventWriter? detailWriter)
    {
        var snapshot = CreateSnapshotCore();
        CombatDetailDelta? detail = null;
        CombatDetailUpdateResult detailUpdate = default;
        if (detailCombatantId > 0)
        {
            var adapter = CreateAdapter();
            if (detailWriter is not null)
            {
                detailUpdate = CreateDetailUpdateCore(adapter, snapshot, detailCombatantId, forceDetailRefresh, detailWriter);
            }
            else
            {
                detail = CreateDetailDeltaCore(adapter, snapshot, detailCombatantId, forceDetailRefresh);
            }
        }

        return new SceneReadModelFrame
        {
            Snapshot = snapshot,
            ReadModelRevision = snapshot.ReadModelRevision,
            DetailCombatantId = detailCombatantId,
            Detail = detail,
            DetailUpdate = detailUpdate,
            BossFocuses = snapshot.BossFocuses
        };
    }

    private SceneCombatSnapshot CreateSnapshotCore()
    {
        var key = SnapshotCacheKey.From(EncounterId, entities, boundary, combat, _applier.BossFocus);
        if (_snapshotCache is not null && _snapshotCacheKey == key && IsSnapshotCacheStable(_snapshotCache))
        {
            _projectionCacheStats = _projectionCacheStats.WithHit();
            return _snapshotCache;
        }

        var adapter = CreateAdapter();
        _snapshotBuilder.Reset(EncounterId, combat.Combatants.Count, 0);
        adapter.BuildSnapshot(_snapshotBuilder);
        ApplyBossFocusSnapshots(_snapshotBuilder);
        var snapshot = _snapshotBuilder.ToSnapshot(combat.Revision);
        if (IsSnapshotCacheStable(snapshot))
        {
            _snapshotCacheKey = SnapshotCacheKey.From(EncounterId, entities, boundary, combat, _applier.BossFocus);
            _snapshotCache = snapshot;
        }
        else
        {
            _snapshotCacheKey = default;
            _snapshotCache = null;
        }
        _projectionCacheStats = _projectionCacheStats.WithMiss();
        return snapshot;
    }

    private SceneCombatSnapshotAdapter CreateAdapter()
        => new(entities, combat, boundary, _applier.BossFocus, EncounterId);

    private SceneRuntimeCheckpoint CreateRuntimeCheckpointCore(long capturedAtMilliseconds, JournalCursor cursor)
    {
        var entitiesClone = entities.DeepClone();
        var boundaryClone = boundary.DeepClone();
        var metadataRegistryClone = metadataRegistry.DeepClone();
        var combatClone = combat.DeepClone();
        var applierClone = _applier.DeepClone(entitiesClone, boundaryClone, metadataRegistryClone, combatClone);
        return new SceneRuntimeCheckpoint(
            EncounterId,
            SceneStarted,
            capturedAtMilliseconds,
            cursor,
            AppliedObservationOrdinal,
            _appliedBatchOrdinal,
            entitiesClone,
            boundaryClone,
            metadataRegistryClone,
            combatClone,
            applierClone);
    }

    internal SceneRuntimeCheckpoint[] CreateArchiveCheckpoints()
    {
        if (_runtimeCheckpoints.Count == 0)
            return [CreateRuntimeCheckpointCore(_lastAppliedTimestampMilliseconds, _cursor)];

        var result = new SceneRuntimeCheckpoint[_runtimeCheckpoints.Count + 1];
        for (var i = 0; i < _runtimeCheckpoints.Count; i++)
            result[i] = _runtimeCheckpoints[i].DeepClone();
        result[^1] = CreateRuntimeCheckpointCore(_lastAppliedTimestampMilliseconds, _cursor);
        return result;
    }

    private static bool IsSnapshotCacheStable(SceneCombatSnapshot snapshot) =>
        snapshot.EncounterEndTime > 0 || snapshot.BossFocuses.Count == 0 && snapshot.Encounter.TrackingTargetId == 0;

    private CombatDetailDelta CreateDetailDeltaCore(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh)
    {
        var subscription = GetDetailSubscription(combatantId);
        if (forceRefresh || !_lastDetailDeltas.ContainsKey(combatantId))
        {
            var cold = subscription.CreateSnapshotDelta(adapter, snapshot);
            _lastDetailDeltas[combatantId] = cold;
            return cold;
        }

        if (subscription.Poll(adapter, snapshot) is { } delta)
        {
            _lastDetailDeltas[combatantId] = delta;
            return delta;
        }

        return _lastDetailDeltas[combatantId];
    }

    private CombatDetailUpdateResult CreateDetailUpdateCore(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh, ICombatDetailEventWriter writer)
    {
        var subscription = GetDetailSubscription(combatantId);
        return subscription.Update(adapter, snapshot, forceRefresh, writer);
    }

    private void ApplyBossFocusSnapshots(SceneCombatSnapshotBuilder builder)
    {
        var now = builder.EncounterEndTime > 0
            ? builder.EncounterEndTime
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bosses = _applier.BossFocus.GetObservedBosses(now, BossFocusVisibilityTimeoutMilliseconds);
        for (var i = 0; i < bosses.Count; i++)
        {
            var boss = bosses[i];
            builder.AddBossFocus(new SceneBossFocusSnapshot
            {
                InstanceId = boss.InstanceId,
                Hp = boss.Hp,
                MaxHp = boss.MaxHp,
                LastObservedAtMilliseconds = boss.LastObservedAtMilliseconds,
                HasHp = boss.HasHp
            });
        }
    }

    private CombatDetailSubscription GetDetailSubscription(int combatantId)
    {
        if (!_detailSubscriptions.TryGetValue(combatantId, out var subscription))
        {
            subscription = new CombatDetailSubscription(combat, combatantId);
            _detailSubscriptions[combatantId] = subscription;
        }

        return subscription;
    }

    public void ResetCombat(Guid encounterId, long startOrdinal)
        => ResetCombat(encounterId, startOrdinal, DateTimeOffset.Now);

    public void ResetCombat(Guid encounterId, long startOrdinal, DateTimeOffset sceneStarted)
    {
        lock (_gate)
        {
            EncounterId = encounterId;
            SceneStarted = sceneStarted;
            combat.Clear();
            _applier = new DomainEventApplier(entities, boundary, metadataRegistry, combat);
            _detailSubscriptions.Clear();
            _lastDetailDeltas.Clear();
            _cursor = journal.CreateCursor(startOrdinal);
            AppliedObservationOrdinal = 0;
            _appliedBatchOrdinal = journal.LastCompletedBatchOrdinal;
            _snapshotCache = null;
            _snapshotCacheKey = default;
        }
    }
}

internal readonly record struct SnapshotCacheKey(Guid EncounterId, long CombatRevision, long EntityRevision, long BoundaryRevision, long SceneTransitionRevision, long BossFocusRevision)
{
    public static SnapshotCacheKey From(Guid encounterId, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat, BossFocusStore bossFocus) =>
        new(encounterId, combat.Revision, entities.Revision, boundary.Revision, boundary.SceneTransitionRevision, bossFocus.Revision);
}

public readonly record struct ProjectionCacheStats(long SnapshotBuilds, long SnapshotCacheHits)
{
    public ProjectionCacheStats WithMiss() => new(SnapshotBuilds + 1, SnapshotCacheHits);
    public ProjectionCacheStats WithHit() => new(SnapshotBuilds, SnapshotCacheHits + 1);
}

public readonly record struct SceneReadModelFrame
{
    public SceneReadModelFrame()
    {
        Snapshot = SceneCombatSnapshot.Empty;
        ReadModelRevision = 0;
        DetailCombatantId = 0;
        Detail = null;
        DetailUpdate = default;
        BossFocuses = default;
    }

    public SceneCombatSnapshot Snapshot { get; init; }
    public long ReadModelRevision { get; init; }
    public int DetailCombatantId { get; init; }
    public CombatDetailDelta? Detail { get; init; }
    public CombatDetailUpdateResult DetailUpdate { get; init; }
    public SnapshotList<SceneBossFocusSnapshot> BossFocuses { get; init; }
}
