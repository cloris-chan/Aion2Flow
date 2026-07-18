using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class CombatDetailSubscription(CombatStore store, MechanicStore mechanics, ResourceStore resources, int combatantId)
{
    private const int ChangeBufferSize = 64;

    private SnapshotChangeCursor _combatCursor = store.CreateCursor(0);
    private SnapshotChangeCursor _mechanicCursor = mechanics.CreateCursor(0);
    private SnapshotChangeCursor _resourceCursor = resources.CreateCursor(0);
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
        var affected = ConsumeChanges(store, ref _combatCursor, changes, adapter);
        affected |= ConsumeChanges(mechanics, ref _mechanicCursor, changes, adapter);
        affected |= ConsumeChanges(resources, ref _resourceCursor, changes, adapter);
        if (!affected)
            return null;

        var delta = CreateDelta(GetDetailRevision(), adapter, snapshot);
        _lastAppliedRevision = delta.Revision;
        return delta;
    }

    public CombatDetailDelta CreateSnapshotDelta(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot)
    {
        var detailRevision = GetDetailRevision();
        _combatCursor = store.CreateCursor(store.Revision);
        _mechanicCursor = mechanics.CreateCursor(mechanics.Revision);
        _resourceCursor = resources.CreateCursor(resources.Revision);
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

    public CombatDetailUpdateResult CreateSnapshotUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, ICombatDetailEventWriter writer)
        => CreateSnapshotUpdate(adapter, snapshot, CombatDetailContextKey.From(snapshot, combatantId), writer);

    internal CombatDetailUpdateResult CreateSnapshotUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, CombatDetailProjectionScope scope, ICombatDetailEventWriter writer)
        => CreateSnapshotUpdate(adapter, snapshot, CombatDetailContextKey.From(snapshot, combatantId), scope, writer);

    private CombatDetailUpdateResult CreateSnapshotUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, CombatDetailContextKey context, ICombatDetailEventWriter writer)
        => CreateSnapshotUpdate(adapter, snapshot, context, CombatDetailProjectionScope.EncounterWindow, writer);

    private CombatDetailUpdateResult CreateSnapshotUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, CombatDetailContextKey context, CombatDetailProjectionScope scope, ICombatDetailEventWriter writer)
    {
        writer.Clear();
        var detailRevision = GetDetailRevision();
        var write = adapter.WriteDetailEvents(snapshot, combatantId, writer, scope);
        detailRevision = Math.Max(detailRevision, write.Revision);
        _combatCursor = store.CreateCursor(store.Revision);
        _mechanicCursor = mechanics.CreateCursor(mechanics.Revision);
        _resourceCursor = resources.CreateCursor(resources.Revision);
        _lastAppliedRevision = detailRevision;
        _liveContextKey = context;
        _hasLiveContext = true;

        return new CombatDetailUpdateResult
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            IsFullSnapshot = true,
            HasChanges = true,
            AddedMetricEventCount = write.MetricEventCount,
            AddedMechanicEventCount = write.MechanicEventCount,
            AddedResourceEventCount = write.ResourceEventCount,
            Combatant = CombatPairProjection.GetCombatant(store, mechanics, resources, combatantId)
        };
    }

    private CombatDetailUpdateResult PollUpdate(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, ICombatDetailEventWriter writer)
    {
        Span<CombatSnapshotChange> changes = stackalloc CombatSnapshotChange[ChangeBufferSize];
        var mechanicChanged = ConsumeChanges(mechanics, ref _mechanicCursor, changes, adapter);
        var resourceChanged = ConsumeChanges(resources, ref _resourceCursor, changes, adapter);
        var batch = store.CopyChanges(_combatCursor, changes);
        if (batch.Count == 0)
        {
            return mechanicChanged || resourceChanged
                ? CreateSnapshotUpdate(adapter, snapshot, CombatDetailContextKey.From(snapshot, combatantId), writer)
                : CombatDetailUpdateResult.None(combatantId, _lastAppliedRevision, CombatPairProjection.GetCombatant(store, mechanics, resources, combatantId));
        }

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
                    !adapter.TryCreateMetricDetailEvent(snapshot, combatantId, in record, out var detailEvent))
                {
                    continue;
                }

                writer.AddMetric(in detailEvent);
                addedEventCount++;
            }

            _combatCursor = batch.Cursor;
            if (!batch.HasMore)
                break;

            batch = store.CopyChanges(_combatCursor, changes);
            if (batch.Count == 0)
                break;
        }

        if (mechanicChanged || resourceChanged)
            return CreateSnapshotUpdate(adapter, snapshot, CombatDetailContextKey.From(snapshot, combatantId), writer);

        if (!affected)
            return CombatDetailUpdateResult.None(combatantId, _lastAppliedRevision, CombatPairProjection.GetCombatant(store, mechanics, resources, combatantId));

        _lastAppliedRevision = Math.Max(detailRevision, GetDetailRevision());
        return new CombatDetailUpdateResult
        {
            CombatantId = combatantId,
            Revision = _lastAppliedRevision,
            HasChanges = true,
            AddedMetricEventCount = addedEventCount,
            Combatant = CombatPairProjection.GetCombatant(store, mechanics, resources, combatantId)
        };
    }

    private CombatDetailDelta CreateDelta(long revision, SceneCombatSnapshotAdapter? adapter, SceneCombatSnapshot? snapshot)
    {
        var events = adapter is not null && snapshot is not null
            ? CombatPairProjection.GetDetailEventSet(adapter, snapshot, combatantId)
            : CombatDetailEventSet.Empty;
        var detailRevision = Math.Max(revision, ResolveDetailRevision(in events));

        return new CombatDetailDelta
        {
            CombatantId = combatantId,
            Revision = detailRevision,
            OutgoingPairs = CombatPairProjection.GetOutgoingPairs(store, mechanics, resources, combatantId),
            IncomingPairs = CombatPairProjection.GetIncomingPairs(store, mechanics, resources, combatantId),
            MetricEvents = events.MetricEvents,
            MechanicEvents = events.MechanicEvents,
            ResourceEvents = events.ResourceEvents,
            Combatant = CombatPairProjection.GetCombatant(store, mechanics, resources, combatantId)
        };
    }

    private bool ConsumeChanges(CombatStore source, ref SnapshotChangeCursor cursor, Span<CombatSnapshotChange> changes, SceneCombatSnapshotAdapter? adapter)
    {
        var affected = false;
        while (true)
        {
            var batch = source.CopyChanges(cursor, changes);
            if (batch.Count == 0)
                return affected;

            for (var i = 0; i < batch.Count; i++)
                affected |= IsRelevant(in changes[i], adapter);
            cursor = batch.Cursor;
            if (!batch.HasMore)
                return affected;
        }
    }

    private bool ConsumeChanges(MechanicStore source, ref SnapshotChangeCursor cursor, Span<CombatSnapshotChange> changes, SceneCombatSnapshotAdapter? adapter)
    {
        var affected = false;
        while (true)
        {
            var batch = source.CopyChanges(cursor, changes);
            if (batch.Count == 0)
                return affected;

            for (var i = 0; i < batch.Count; i++)
                affected |= IsRelevant(in changes[i], adapter);
            cursor = batch.Cursor;
            if (!batch.HasMore)
                return affected;
        }
    }

    private bool ConsumeChanges(ResourceStore source, ref SnapshotChangeCursor cursor, Span<CombatSnapshotChange> changes, SceneCombatSnapshotAdapter? adapter)
    {
        var affected = false;
        while (true)
        {
            var batch = source.CopyChanges(cursor, changes);
            if (batch.Count == 0)
                return affected;

            for (var i = 0; i < batch.Count; i++)
                affected |= IsRelevant(in changes[i], adapter);
            cursor = batch.Cursor;
            if (!batch.HasMore)
                return affected;
        }
    }

    private long GetDetailRevision()
    {
        var combatRevision = store.GetCombatantDetailRevision(combatantId);
        var mechanicRevision = mechanics.GetCombatantDetailRevision(combatantId);
        var resourceRevision = resources.GetCombatantDetailRevision(combatantId);
        return SaturatingAdd(combatRevision, mechanicRevision, resourceRevision);
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

    private static long ResolveDetailRevision(in CombatDetailEventSet events)
    {
        var revision = 0L;
        for (var i = 0; i < events.MetricEvents.Count; i++)
            revision = Math.Max(revision, events.MetricEvents[i].Revision);
        for (var i = 0; i < events.MechanicEvents.Count; i++)
            revision = Math.Max(revision, events.MechanicEvents[i].Revision);
        for (var i = 0; i < events.ResourceEvents.Count; i++)
            revision = Math.Max(revision, events.ResourceEvents[i].Revision);

        return revision;
    }

    private static long SaturatingAdd(long first, long second, long third)
    {
        var sum = first > long.MaxValue - second ? long.MaxValue : first + second;
        return sum > long.MaxValue - third ? long.MaxValue : sum + third;
    }
}

public sealed class CombatDetailDelta
{
    public int CombatantId { get; init; }
    public long Revision { get; init; }
    public IReadOnlyList<DirectedPairKey> OutgoingPairs { get; init; } = [];
    public IReadOnlyList<DirectedPairKey> IncomingPairs { get; init; } = [];
    public IReadOnlyList<CombatMetricDetailEvent> MetricEvents { get; init; } = [];
    public IReadOnlyList<CombatMechanicDetailEvent> MechanicEvents { get; init; } = [];
    public IReadOnlyList<CombatResourceDetailEvent> ResourceEvents { get; init; } = [];
    public CombatantSummary? Combatant { get; init; }
}
