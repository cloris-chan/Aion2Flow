using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid battleId, EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    private readonly Lock _gate = new();
    private readonly DomainEventApplier _applier = new DomainEventApplier(entities, metadata, combat);
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
    public Guid BattleId { get; } = battleId;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;

    public DamageMeterSnapshot CreateSnapshot()
    {
        Refresh();
        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata, _applier.BossFocus, BattleId);
        return adapter.CreateSnapshot();
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
                foreach (ref readonly var entry in entries)
                    _applier.ApplyEntry(in entry);

                _cursor = new JournalCursor(_cursor.Position + count, _cursor.StartOrdinal);
                AppliedObservationOrdinal += count;
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
}
