using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class ResourceStore(int eventCapacity = 0, int pairCapacity = 0, int combatantCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private readonly List<CombatResourceEventRecord> _events = eventCapacity > 0 ? new List<CombatResourceEventRecord>(eventCapacity) : [];
    private readonly Dictionary<(int Source, int Target), CombatResourcePairRecord> _pairs = pairCapacity > 0 ? new Dictionary<(int, int), CombatResourcePairRecord>(pairCapacity) : [];
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _outgoingBySource = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _incomingByTarget = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly List<CombatSnapshotChange> _changeLog = eventCapacity > 0 ? new(eventCapacity) : [];
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyList<CombatResourceEventRecord> Events => _events;
    public IReadOnlyDictionary<(int Source, int Target), CombatResourcePairRecord> Pairs => _pairs;
    public long Revision => _revision;

    public void EnsureCapacity(int eventCapacity, int pairCapacity = 0, int combatantCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(eventCapacity);
        }
        if (pairCapacity > 0)
            _pairs.EnsureCapacity(pairCapacity);
        if (combatantCapacity > 0)
        {
            _outgoingBySource.EnsureCapacity(combatantCapacity);
            _incomingByTarget.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }
    }

    public void Apply(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatResourceOccurrence resource,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        RawPacketReference raw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceObservationOrdinal, CombatStore.UnknownSourceObservationOrdinal);
        if (resource.Resource == CombatResourceKind.Unknown)
            throw new ArgumentException("A resource occurrence must identify a packet-backed resource kind.", nameof(resource));

        var revision = ++_revision;
        _events.Add(new CombatResourceEventRecord(
            sourceId,
            targetId,
            observation,
            resource,
            CombatEventKey.FromObservation(in observation),
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw,
            revision));

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var exists);
        if (!exists)
            pair = new CombatResourcePairRecord(sourceId, targetId, observedAtMilliseconds);
        pair!.Apply(in resource, observation.SkillCode, observedAtMilliseconds, revision);
        AddPairIndex(pairKey);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, revision));
        MarkDetailRevision(sourceId, revision);
        MarkDetailRevision(targetId, revision);
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatResourcePairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public IReadOnlyCollection<(int Source, int Target)> GetOutgoingPairs(int sourceId) =>
        _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyCollection<(int Source, int Target)> GetIncomingPairs(int targetId) =>
        _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    public bool TryGetEventByRevision(long revision, out CombatResourceEventRecord record)
    {
        var index = revision - 1;
        if ((ulong)index < (uint)_events.Count)
        {
            var candidate = _events[(int)index];
            if (candidate.Revision == revision)
            {
                record = candidate;
                return true;
            }
        }

        record = default;
        return false;
    }

    public long GetCombatantDetailRevision(int combatantId) =>
        combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    public void Clear()
    {
        _events.Clear();
        _pairs.Clear();
        _outgoingBySource.Clear();
        _incomingByTarget.Clear();
        _changeLog.Clear();
        _detailRevisionByCombatant.Clear();
        _revision = 0;
    }

    public SnapshotChangeCursor CreateCursor(long afterRevision) => new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
    {
        var (start, count) = ResolveChangeReadBounds(cursor, Math.Max(1, maxChanges));
        if (count == 0)
            return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, _revision, [], false);

        var changes = _changeLog.GetRange(start, count);
        return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, changes[^1].Revision, changes, start + count < _changeLog.Count);
    }

    public SnapshotChangeCopyResult CopyChanges(SnapshotChangeCursor cursor, Span<CombatSnapshotChange> destination)
    {
        var (start, count) = ResolveChangeReadBounds(cursor, destination.Length);
        if (count == 0)
            return new SnapshotChangeCopyResult(cursor, cursor.Revision, _revision, 0, false);

        for (var i = 0; i < count; i++)
            destination[i] = _changeLog[start + i];

        var toRevision = _changeLog[start + count - 1].Revision;
        return new SnapshotChangeCopyResult(new SnapshotChangeCursor(toRevision, 0), cursor.Revision, toRevision, count, start + count < _changeLog.Count);
    }

    internal ResourceStoreSnapshot CreateSnapshot()
    {
        var pairs = new CombatResourcePairRecordSnapshot[_pairs.Count];
        var index = 0;
        foreach (var pair in _pairs.Values)
            pairs[index++] = CombatResourcePairRecordSnapshot.From(pair);

        var detailRevisions = new CombatantDetailRevisionSnapshot[_detailRevisionByCombatant.Count];
        index = 0;
        foreach (var (combatantId, revision) in _detailRevisionByCombatant)
            detailRevisions[index++] = new CombatantDetailRevisionSnapshot(combatantId, revision);

        return new ResourceStoreSnapshot(pairs, [.. _events], detailRevisions, _revision);
    }

    internal static ResourceStore FromSnapshot(ResourceStoreSnapshot snapshot)
    {
        var store = new ResourceStore(snapshot.Events.Length, snapshot.Pairs.Length, snapshot.DetailRevisions.Length)
        {
            _revision = snapshot.Revision
        };
        store._events.AddRange(snapshot.Events);
        for (var i = 0; i < snapshot.Pairs.Length; i++)
        {
            var pair = snapshot.Pairs[i].ToRecord();
            var key = (pair.SourceId, pair.TargetId);
            store._pairs.Add(key, pair);
            store.AddPairIndex(key);
        }
        for (var i = 0; i < snapshot.DetailRevisions.Length; i++)
        {
            var detail = snapshot.DetailRevisions[i];
            store._detailRevisionByCombatant.Add(detail.CombatantId, detail.Revision);
        }
        return store;
    }

    private void AddPairIndex((int Source, int Target) pairKey)
    {
        if (pairKey.Source > 0)
        {
            ref var outgoing = ref CollectionsMarshal.GetValueRefOrAddDefault(_outgoingBySource, pairKey.Source, out var exists);
            if (!exists)
                outgoing = [];
            outgoing!.Add(pairKey);
        }

        if (pairKey.Target > 0)
        {
            ref var incoming = ref CollectionsMarshal.GetValueRefOrAddDefault(_incomingByTarget, pairKey.Target, out var exists);
            if (!exists)
                incoming = [];
            incoming!.Add(pairKey);
        }
    }

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_detailRevisionByCombatant, combatantId, out var exists);
        current = exists ? Math.Max(current, revision) : revision;
    }

    private (int Start, int Count) ResolveChangeReadBounds(SnapshotChangeCursor cursor, int maxChanges)
    {
        if (maxChanges <= 0)
            return default;

        var low = 0;
        var high = _changeLog.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_changeLog[middle].Revision <= cursor.Revision)
                low = middle + 1;
            else
                high = middle;
        }

        var start = low + cursor.Offset;
        if (start >= _changeLog.Count)
            return default;

        return (start, Math.Min(maxChanges, _changeLog.Count - start));
    }
}

public readonly record struct CombatResourceEventRecord(
    int SourceId,
    int TargetId,
    CombatWireObservation Observation,
    CombatResourceOccurrence Resource,
    CombatEventKey EventKey,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    RawPacketReference Raw,
    long Revision);

public sealed class CombatResourcePairRecord(int sourceId, int targetId, long observedAtMilliseconds)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public long HealthRestored { get; private set; }
    public long HealthUnknown { get; private set; }
    public long ManaRestored { get; private set; }
    public long ManaSpent { get; private set; }
    public long ManaUnknown { get; private set; }
    public int LastSkillCode { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void Apply(in CombatResourceOccurrence resource, int skillCode, long observedAtMilliseconds, long revision)
    {
        switch (resource.Resource, resource.Flow)
        {
            case (CombatResourceKind.Health, CombatResourceFlowKind.Restore):
                HealthRestored += resource.Amount;
                break;
            case (CombatResourceKind.Health, _):
                HealthUnknown += resource.Amount;
                break;
            case (CombatResourceKind.Mana, CombatResourceFlowKind.Restore):
                ManaRestored += resource.Amount;
                break;
            case (CombatResourceKind.Mana, CombatResourceFlowKind.Spend):
                ManaSpent += resource.Amount;
                break;
            case (CombatResourceKind.Mana, _):
                ManaUnknown += resource.Amount;
                break;
        }
        LastSkillCode = skillCode;
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }

    internal void Restore(in CombatResourcePairRecordSnapshot snapshot)
    {
        HealthRestored = snapshot.HealthRestored;
        HealthUnknown = snapshot.HealthUnknown;
        ManaRestored = snapshot.ManaRestored;
        ManaSpent = snapshot.ManaSpent;
        ManaUnknown = snapshot.ManaUnknown;
        LastSkillCode = snapshot.LastSkillCode;
        FirstObserved = snapshot.FirstObserved;
        LastObserved = snapshot.LastObserved;
        Revision = snapshot.Revision;
    }

}

internal sealed record ResourceStoreSnapshot(
    CombatResourcePairRecordSnapshot[] Pairs,
    CombatResourceEventRecord[] Events,
    CombatantDetailRevisionSnapshot[] DetailRevisions,
    long Revision);

internal readonly record struct CombatResourcePairRecordSnapshot(
    int SourceId,
    int TargetId,
    long HealthRestored,
    long HealthUnknown,
    long ManaRestored,
    long ManaSpent,
    long ManaUnknown,
    int LastSkillCode,
    long FirstObserved,
    long LastObserved,
    long Revision)
{
    public static CombatResourcePairRecordSnapshot From(CombatResourcePairRecord record) => new(
        record.SourceId,
        record.TargetId,
        record.HealthRestored,
        record.HealthUnknown,
        record.ManaRestored,
        record.ManaSpent,
        record.ManaUnknown,
        record.LastSkillCode,
        record.FirstObserved,
        record.LastObserved,
        record.Revision);

    public CombatResourcePairRecord ToRecord()
    {
        var record = new CombatResourcePairRecord(SourceId, TargetId, FirstObserved);
        record.Restore(in this);
        return record;
    }
}
