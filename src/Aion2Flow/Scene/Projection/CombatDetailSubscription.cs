using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class CombatDetailSubscription(CombatStore store, CombatPairProjection projection, int combatantId)
{
    private SnapshotChangeCursor _cursor = store.CreateCursor(0);
    private long _lastAppliedRevision;

    public int CombatantId => combatantId;
    public long LastAppliedRevision => _lastAppliedRevision;

    public CombatDetailDelta? Poll()
    {
        var detailRevision = store.GetCombatantDetailRevision(combatantId);
        if (detailRevision <= _lastAppliedRevision)
            return null;

        var batch = store.ReadChanges(_cursor, 64);
        if (batch.Changes.Count == 0)
            return null;

        bool affected = false;
        while (true)
        {
            for (int i = 0; i < batch.Changes.Count; i++)
            {
                var change = batch.Changes[i];
                if (change.CombatantId == combatantId || change.PairKey.Source == combatantId || change.PairKey.Target == combatantId)
                {
                    affected = true;
                    break;
                }
            }

            _cursor = new SnapshotChangeCursor(batch.ToRevision, 0);
            if (!batch.HasMore || batch.ToRevision >= detailRevision)
                break;

            batch = store.ReadChanges(_cursor, 64);
            if (batch.Changes.Count == 0)
                break;
        }

        if (!affected)
            return null;

        _lastAppliedRevision = detailRevision;
        projection.Rebuild(store);

        return new CombatDetailDelta
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            OutgoingPairs = projection.GetOutgoingPairs(combatantId),
            IncomingPairs = projection.GetIncomingPairs(combatantId),
            Combatant = projection.GetCombatant(combatantId)
        };
    }
}

public sealed class CombatDetailDelta
{
    public int CombatantId { get; init; }
    public long Revision { get; init; }
    public IReadOnlyList<DirectedPairKey> OutgoingPairs { get; init; } = [];
    public IReadOnlyList<DirectedPairKey> IncomingPairs { get; init; } = [];
    public CombatantSummary? Combatant { get; init; }
}
