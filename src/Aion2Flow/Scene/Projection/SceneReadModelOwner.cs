using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid battleId, EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    private readonly Lock _gate = new();
    private DomainEventApplier _applier = new DomainEventApplier(entities, metadata, combat);
    private readonly CombatPairProjection _pairs = new();
    private readonly ObservedEventEnvelope[] _entryBuffer = new ObservedEventEnvelope[256];
    private JournalCursor _cursor = journal.CreateCursor(0);
    private long _appliedBatchOrdinal = -1;
    private long _projectionRevision = -1;

    public SceneReadModelOwner(ObservedEventJournal journal) : this(journal, Guid.NewGuid())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid battleId) : this(journal, battleId, new EntityStore(), new MetadataStore(), new CombatStore())
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
    public Guid BattleId { get; private set; } = battleId;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;

    public DamageMeterSnapshot CreateSnapshot()
    {
        Refresh();
        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata, _applier.BossFocus, BattleId);
        return adapter.CreateSnapshot();
    }

    public CombatDetailDelta CreateDetailDelta(DamageMeterSnapshot snapshot, int combatantId)
    {
        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata, _applier.BossFocus, BattleId);
        var subscription = new CombatDetailSubscription(combat, _pairs, combatantId);
        return subscription.CreateSnapshotDelta(adapter, snapshot);
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

    public void ResetCombat(Guid battleId, long startOrdinal)
    {
        lock (_gate)
        {
            BattleId = battleId;
            combat.Clear();
            _applier = new DomainEventApplier(entities, metadata, combat);
            _pairs.Rebuild(combat);
            _cursor = journal.CreateCursor(startOrdinal);
            AppliedObservationOrdinal = 0;
            _appliedBatchOrdinal = journal.LastCompletedBatchOrdinal;
            _projectionRevision = combat.Revision;
        }
    }
}
