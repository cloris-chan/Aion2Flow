using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class CombatDetailSubscription(CombatStore store, int combatantId)
{
    private const int ChangeBufferSize = 64;

    private SnapshotChangeCursor _cursor = store.CreateCursor(0);
    private long _lastAppliedRevision;
    private CombatDetailContextKey _liveContextKey;
    private bool _hasLiveContext;

    public int CombatantId => combatantId;
    public long LastAppliedRevision => _lastAppliedRevision;

    public CombatDetailDelta? Poll()
        => PollCore(null, null);

    public CombatDetailDelta? Poll(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot)
        => PollCore(adapter, snapshot);

    private CombatDetailDelta? PollCore(SceneCombatSnapshotAdapter? adapter, SceneCombatSnapshot? snapshot)
    {
        Span<CombatSnapshotChange> changes = stackalloc CombatSnapshotChange[ChangeBufferSize];
        var batch = store.CopyChanges(_cursor, changes);
        if (batch.Count == 0)
            return null;

        bool affected = false;
        var detailRevision = _lastAppliedRevision;
        while (true)
        {
            for (var i = 0; i < batch.Count; i++)
            {
                var change = changes[i];
                if (IsRelevant(in change, adapter))
                {
                    affected = true;
                    detailRevision = Math.Max(detailRevision, change.Revision);
                }
            }

            _cursor = batch.Cursor;
            if (!batch.HasMore)
                break;

            batch = store.CopyChanges(_cursor, changes);
            if (batch.Count == 0)
                break;
        }

        if (!affected)
            return null;

        var delta = CreateDelta(detailRevision, adapter, snapshot);
        _lastAppliedRevision = delta.Revision;
        return delta;
    }

    public CombatDetailDelta CreateSnapshotDelta(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot)
    {
        var detailRevision = store.GetCombatantDetailRevision(combatantId);
        _cursor = store.CreateCursor(store.Revision);
        var delta = CreateDelta(detailRevision, adapter, snapshot);
        _lastAppliedRevision = delta.Revision;
        return delta;
    }

    public CombatDetailUpdateResult Update(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, bool forceRefresh, ICombatDetailEventWriter writer)
    {
        var context = CombatDetailContextKey.From(snapshot, combatantId);
        if (forceRefresh || !_hasLiveContext || _liveContextKey != context)
            return CreateSnapshotUpdate(adapter, snapshot, context, writer);

        return PollUpdate(adapter, snapshot, writer);
    }

    private CombatDetailUpdateResult CreateSnapshotUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, CombatDetailContextKey context, ICombatDetailEventWriter writer)
    {
        writer.Clear();
        var detailRevision = store.GetCombatantDetailRevision(combatantId);
        var write = adapter.WriteDetailEvents(snapshot, combatantId, writer);
        detailRevision = Math.Max(detailRevision, write.Revision);
        _cursor = store.CreateCursor(store.Revision);
        _lastAppliedRevision = detailRevision;
        _liveContextKey = context;
        _hasLiveContext = true;

        return new CombatDetailUpdateResult
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            IsFullSnapshot = true,
            HasChanges = true,
            AddedEventCount = write.Count,
            Combatant = CombatPairProjection.GetCombatant(store, combatantId)
        };
    }

    private CombatDetailUpdateResult PollUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, ICombatDetailEventWriter writer)
    {
        Span<CombatSnapshotChange> changes = stackalloc CombatSnapshotChange[ChangeBufferSize];
        var batch = store.CopyChanges(_cursor, changes);
        if (batch.Count == 0)
            return CombatDetailUpdateResult.None(combatantId, _lastAppliedRevision, CombatPairProjection.GetCombatant(store, combatantId));

        var affected = false;
        var addedEventCount = 0;
        var detailRevision = _lastAppliedRevision;
        while (true)
        {
            for (var i = 0; i < batch.Count; i++)
            {
                var change = changes[i];
                if (!IsRelevant(in change, adapter))
                    continue;

                affected = true;
                detailRevision = Math.Max(detailRevision, change.Revision);
                if (change.Kind != CombatSnapshotChangeKind.PairUpdated ||
                    !store.TryGetEventByRevision(change.Revision, out var record) ||
                    !adapter.TryCreateDetailEvent(snapshot, combatantId, in record, out var detailEvent))
                {
                    continue;
                }

                writer.Add(in detailEvent);
                addedEventCount++;
            }

            _cursor = batch.Cursor;
            if (!batch.HasMore)
                break;

            batch = store.CopyChanges(_cursor, changes);
            if (batch.Count == 0)
                break;
        }

        if (!affected)
            return CombatDetailUpdateResult.None(combatantId, _lastAppliedRevision, CombatPairProjection.GetCombatant(store, combatantId));

        _lastAppliedRevision = detailRevision;
        return new CombatDetailUpdateResult
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            HasChanges = true,
            AddedEventCount = addedEventCount,
            Combatant = CombatPairProjection.GetCombatant(store, combatantId)
        };
    }

    private CombatDetailDelta CreateDelta(long revision, SceneCombatSnapshotAdapter? adapter, SceneCombatSnapshot? snapshot)
    {
        var events = adapter is not null && snapshot is not null
            ? CombatPairProjection.GetDetailEvents(adapter, snapshot, combatantId)
            : [];
        var detailRevision = Math.Max(revision, ResolveDetailRevision(events));

        return new CombatDetailDelta
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            OutgoingPairs = CombatPairProjection.GetOutgoingPairs(store, combatantId),
            IncomingPairs = CombatPairProjection.GetIncomingPairs(store, combatantId),
            Events = events,
            Combatant = CombatPairProjection.GetCombatant(store, combatantId)
        };
    }

    private bool IsRelevant(in CombatSnapshotChange change, SceneCombatSnapshotAdapter? adapter)
    {
        if (ResolveDetailCombatantId(change.CombatantId, adapter) == combatantId)
            return true;

        return ResolveDetailCombatantId(change.PairKey.Source, adapter) == combatantId ||
               ResolveDetailCombatantId(change.PairKey.Target, adapter) == combatantId;
    }

    private static int ResolveDetailCombatantId(int entityId, SceneCombatSnapshotAdapter? adapter)
        => entityId > 0 && adapter is not null ? adapter.ResolveDetailCombatantId(entityId) : entityId;

    private static long ResolveDetailRevision(IReadOnlyList<CombatDetailEvent> events)
    {
        var revision = 0L;
        for (var i = 0; i < events.Count; i++)
            revision = Math.Max(revision, events[i].Revision);

        return revision;
    }
}

public sealed class CombatDetailDelta
{
    public int CombatantId { get; init; }
    public long Revision { get; init; }
    public IReadOnlyList<DirectedPairKey> OutgoingPairs { get; init; } = [];
    public IReadOnlyList<DirectedPairKey> IncomingPairs { get; init; } = [];
    public IReadOnlyList<CombatDetailEvent> Events { get; init; } = [];
    public CombatantSummary? Combatant { get; init; }
}
