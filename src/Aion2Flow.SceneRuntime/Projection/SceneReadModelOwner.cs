using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    private const long BossFocusVisibilityTimeoutMilliseconds = 2_000;
    private readonly Lock _gate = new();
    private DomainEventApplier _applier = new(entities, metadata, combat);
    private readonly CombatPairProjection _pairs = new();
    private readonly Dictionary<int, CombatDetailSubscription> _detailSubscriptions = [];
    private readonly Dictionary<int, CombatDetailDelta> _lastDetailDeltas = [];
    private readonly ObservedEventEnvelope[] _entryBuffer = new ObservedEventEnvelope[256];
    private JournalCursor _cursor = journal.CreateCursor(0);
    private long _appliedBatchOrdinal = -1;
    private long _projectionRevision = -1;

    public SceneReadModelOwner(ObservedEventJournal journal) : this(journal, Guid.NewGuid(), DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId) : this(journal, encounterId, DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted) : this(journal, encounterId, sceneStarted, new EntityStore(), new MetadataStore(), new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, MetadataStore metadata, CombatStore combat) : this(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, metadata, combat)
    {
    }

    public EntityStore Entities => entities;
    public MetadataStore Metadata => metadata;
    public CombatStore Combat => combat;
    public DomainEventApplier Applier => _applier;
    public BossFocusStore BossFocus => _applier.BossFocus;
    public CombatPairProjection Pairs => _pairs;
    public Guid EncounterId { get; private set; } = encounterId;
    public DateTimeOffset SceneStarted { get; private set; } = sceneStarted;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;

    public SceneCombatSnapshot CreateSnapshot()
        => CreateFrame().Snapshot;

    public SceneReadModelFrame CreateFrame(int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateFrameCore(detailCombatantId, forceDetailRefresh);
        }
    }

    public SceneArchivePayload CreateArchivePayload(SceneCombatSnapshot snapshot)
    {
        lock (_gate)
        {
            RefreshCore();
            return SceneArchivePayload.CreateLocked(snapshot, SceneStarted, entities, metadata, _applier.BossFocus, _pairs, CreateAdapter());
        }
    }

    public T ReadLocked<T>(Func<EntityStore, MetadataStore, CombatStore, T> reader)
    {
        lock (_gate)
        {
            RefreshCore();
            return reader(entities, metadata, combat);
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
            var count = journal.CopyEntries(_cursor, _entryBuffer);
            if (count == 0)
                break;

            var entries = _entryBuffer.AsSpan(0, count);
            var appliedCount = 0L;
            foreach (ref readonly var entry in entries)
            {
                if (entry.Stamp.ObservationOrdinal >= _cursor.StartOrdinal)
                {
                    _applier.ApplyEntry(in entry);
                    appliedCount++;
                }
            }

            _cursor = new JournalCursor(_cursor.Position + count, _cursor.StartOrdinal);
            AppliedObservationOrdinal += appliedCount;
        }

        var completedBatch = journal.LastCompletedBatchOrdinal;
        if (completedBatch > _appliedBatchOrdinal)
        {
            _applier.CompleteBatch(completedBatch);
            _appliedBatchOrdinal = completedBatch;
        }

        if (_projectionRevision != combat.Revision)
        {
            _pairs.Rebuild(combat);
            _projectionRevision = combat.Revision;
        }
    }

    private SceneReadModelFrame CreateFrameCore(int detailCombatantId, bool forceDetailRefresh)
    {
        var snapshot = CreateSnapshotCore();
        CombatDetailDelta? detail = null;
        if (detailCombatantId > 0)
        {
            var adapter = CreateAdapter();
            detail = CreateDetailDeltaCore(adapter, snapshot, detailCombatantId, forceDetailRefresh);
        }

        return new SceneReadModelFrame
        {
            Snapshot = snapshot,
            ReadModelRevision = snapshot.ReadModelRevision,
            DetailCombatantId = detailCombatantId,
            Detail = detail,
            BossFocuses = snapshot.BossFocuses
        };
    }

    private SceneCombatSnapshot CreateSnapshotCore()
    {
        var snapshot = CreateAdapter().CreateSnapshot();
        snapshot.ReadModelRevision = combat.Revision;
        ApplyBossFocusSnapshots(snapshot);
        return snapshot;
    }

    private SceneCombatSnapshotAdapter CreateAdapter()
        => new(entities, combat, metadata, _applier.BossFocus, EncounterId);

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

    private void ApplyBossFocusSnapshots(SceneCombatSnapshot snapshot)
    {
        var now = snapshot.EncounterEndTime > 0
            ? snapshot.EncounterEndTime
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bosses = _applier.BossFocus.GetObservedBosses(now, BossFocusVisibilityTimeoutMilliseconds);
        var adapter = CreateAdapter();
        for (var i = 0; i < bosses.Count; i++)
        {
            var boss = bosses[i];
            snapshot.BossFocuses.Add(new SceneBossFocusSnapshot
            {
                InstanceId = boss.InstanceId,
                DisplayName = adapter.ResolveDetailDisplayName(boss.InstanceId),
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
            subscription = new CombatDetailSubscription(combat, _pairs, combatantId);
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
            _applier = new DomainEventApplier(entities, metadata, combat);
            _pairs.Rebuild(combat);
            _detailSubscriptions.Clear();
            _lastDetailDeltas.Clear();
            _cursor = journal.CreateCursor(startOrdinal);
            AppliedObservationOrdinal = 0;
            _appliedBatchOrdinal = journal.LastCompletedBatchOrdinal;
            _projectionRevision = combat.Revision;
        }
    }
}

public sealed class SceneReadModelFrame
{
    public SceneCombatSnapshot Snapshot { get; init; } = new();
    public long ReadModelRevision { get; init; }
    public int DetailCombatantId { get; init; }
    public CombatDetailDelta? Detail { get; init; }
    public IReadOnlyList<SceneBossFocusSnapshot> BossFocuses { get; init; } = [];
}
