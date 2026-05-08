using Cloris.Aion2Flow.Scene.Combat;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    private readonly Lock _gate = new();
    private DomainEventApplier _applier = new DomainEventApplier(entities, metadata, combat);
    private readonly CombatPairProjection _pairs = new();
    private readonly Dictionary<int, CombatDetailSubscription> _detailSubscriptions = [];
    private readonly Dictionary<int, CombatDetailDelta> _lastDetailDeltas = [];
    private readonly ObservedEventEnvelope[] _entryBuffer = new ObservedEventEnvelope[256];
    private JournalCursor _cursor = journal.CreateCursor(0);
    private long _appliedBatchOrdinal = -1;
    private long _projectionRevision = -1;

    public SceneReadModelOwner(ObservedEventJournal journal) : this(journal, Guid.NewGuid())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId) : this(journal, encounterId, new EntityStore(), new MetadataStore(), new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, MetadataStore metadata, CombatStore combat) : this(journal, Guid.NewGuid(), entities, metadata, combat)
    {
    }

    public EntityStore Entities => entities;
    public MetadataStore Metadata => metadata;
    public CombatStore Combat => combat;
    public DomainEventApplier Applier => _applier;
    public BossFocusStore BossFocus => _applier.BossFocus;
    public CombatPairProjection Pairs => _pairs;
    public Guid EncounterId { get; private set; } = encounterId;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;

    public SceneCombatSnapshot CreateSnapshot()
    {
        Refresh();
        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata, _applier.BossFocus, EncounterId);
        return adapter.CreateSnapshot();
    }

    public CombatDetailDelta CreateDetailDelta(SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false)
    {
        lock (_gate)
        {
            var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata, _applier.BossFocus, EncounterId);
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
    }

    public void Refresh()
    {
        lock (_gate)
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
    {
        lock (_gate)
        {
            EncounterId = encounterId;
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
