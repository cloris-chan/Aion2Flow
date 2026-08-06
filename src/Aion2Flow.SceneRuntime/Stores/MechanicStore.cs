using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class MechanicStore(int eventCapacity = 0, int pairCapacity = 0, int combatantCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private readonly List<CombatMechanicEventRecord> _events = eventCapacity > 0 ? new List<CombatMechanicEventRecord>(eventCapacity) : [];
    private readonly Dictionary<(int Source, int Target), CombatMechanicPairRecord> _pairs = pairCapacity > 0 ? new Dictionary<(int, int), CombatMechanicPairRecord>(pairCapacity) : [];
    private readonly Dictionary<int, CombatantMechanicRecord> _combatants = combatantCapacity > 0 ? new Dictionary<int, CombatantMechanicRecord>(combatantCapacity) : [];
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _outgoingBySource = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _incomingByTarget = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly List<CombatSnapshotChange> _changeLog = eventCapacity > 0 ? new List<CombatSnapshotChange>(ResolveChangeLogCapacity(eventCapacity)) : [];
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyList<CombatMechanicEventRecord> Events => _events;
    public IReadOnlyDictionary<(int Source, int Target), CombatMechanicPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantMechanicRecord> Combatants => _combatants;
    public long Revision => _revision;

    public void EnsureCapacity(int eventCapacity, int pairCapacity = 0, int combatantCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(ResolveChangeLogCapacity(eventCapacity));
        }
        if (pairCapacity > 0)
            _pairs.EnsureCapacity(pairCapacity);
        if (combatantCapacity > 0)
        {
            _combatants.EnsureCapacity(combatantCapacity);
            _outgoingBySource.EnsureCapacity(combatantCapacity);
            _incomingByTarget.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }
    }

    public void Apply(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatMechanicOccurrence mechanic,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        RawPacketReference raw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceObservationOrdinal, CombatStore.UnknownSourceObservationOrdinal);
        if (!mechanic.HasFacts)
            throw new ArgumentException("A mechanic occurrence must contain at least one packet-backed fact.", nameof(mechanic));

        var revision = ++_revision;
        _events.Add(new CombatMechanicEventRecord(
            sourceId,
            targetId,
            observation,
            mechanic,
            CombatEventKey.FromObservation(in observation),
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw,
            revision));

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var pairExists);
        if (!pairExists)
            pair = new CombatMechanicPairRecord(sourceId, targetId, observedAtMilliseconds);
        pair!.Apply(in mechanic, observation.SkillCode, observedAtMilliseconds, revision);
        AddPairIndex(pairKey);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, revision));
        MarkDetailRevision(sourceId, revision);
        MarkDetailRevision(targetId, revision);

        if (sourceId > 0)
        {
            GetOrAddCombatant(sourceId, observedAtMilliseconds).ApplyOutgoing(in mechanic, observedAtMilliseconds, revision);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, revision));
        }
        if (targetId > 0)
        {
            GetOrAddCombatant(targetId, observedAtMilliseconds).ApplyIncoming(in mechanic, observedAtMilliseconds, revision);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, revision));
        }
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatMechanicPairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantMechanicRecord? combatant) => _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int Source, int Target)> GetOutgoingPairs(int sourceId) =>
        _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyCollection<(int Source, int Target)> GetIncomingPairs(int targetId) =>
        _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    public bool TryGetLastCombatActivityObservedAt(int combatantId, out long observedAtMilliseconds)
        => TryGetLastCombatActivityObservedAt(combatantId, static _ => true, out observedAtMilliseconds);

    public bool TryGetLastCombatActivityObservedAt(int combatantId, Func<int, bool> includeCounterpart, out long observedAtMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(includeCounterpart);
        observedAtMilliseconds = 0;
        if (combatantId <= 0)
            return false;

        var hasActivity = TryApplyLastCombatActivityObservedAt(_outgoingBySource, combatantId, includeCounterpart, outgoing: true, ref observedAtMilliseconds);
        hasActivity |= TryApplyLastCombatActivityObservedAt(_incomingByTarget, combatantId, includeCounterpart, outgoing: false, ref observedAtMilliseconds);
        return hasActivity;
    }

    public bool TryGetEventByRevision(long revision, out CombatMechanicEventRecord record)
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
        _combatants.Clear();
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

    internal MechanicStoreSnapshot CreateSnapshot()
    {
        var pairs = new CombatMechanicPairRecordSnapshot[_pairs.Count];
        var index = 0;
        foreach (var pair in _pairs.Values)
            pairs[index++] = CombatMechanicPairRecordSnapshot.From(pair);

        var combatants = new CombatantMechanicRecordSnapshot[_combatants.Count];
        index = 0;
        foreach (var combatant in _combatants.Values)
            combatants[index++] = CombatantMechanicRecordSnapshot.From(combatant);

        var detailRevisions = new CombatantDetailRevisionSnapshot[_detailRevisionByCombatant.Count];
        index = 0;
        foreach (var (combatantId, revision) in _detailRevisionByCombatant)
            detailRevisions[index++] = new CombatantDetailRevisionSnapshot(combatantId, revision);

        return new MechanicStoreSnapshot(pairs, combatants, [.. _events], detailRevisions, _revision);
    }

    internal static MechanicStore FromSnapshot(MechanicStoreSnapshot snapshot)
    {
        var store = new MechanicStore(snapshot.Events.Length, snapshot.Pairs.Length, snapshot.Combatants.Length)
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
        for (var i = 0; i < snapshot.Combatants.Length; i++)
        {
            var combatant = snapshot.Combatants[i].ToRecord();
            store._combatants.Add(combatant.CombatantId, combatant);
        }
        for (var i = 0; i < snapshot.DetailRevisions.Length; i++)
        {
            var detail = snapshot.DetailRevisions[i];
            store._detailRevisionByCombatant.Add(detail.CombatantId, detail.Revision);
        }
        return store;
    }

    private static int ResolveChangeLogCapacity(int eventCapacity) =>
        eventCapacity > int.MaxValue / 3 ? int.MaxValue : eventCapacity * 3;

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

    private bool TryApplyLastCombatActivityObservedAt(
        Dictionary<int, HashSet<(int Source, int Target)>> index,
        int combatantId,
        Func<int, bool> includeCounterpart,
        bool outgoing,
        ref long observedAtMilliseconds)
    {
        if (!index.TryGetValue(combatantId, out var pairs))
            return false;

        var hasActivity = false;
        foreach (var key in pairs)
        {
            var counterpartId = outgoing ? key.Target : key.Source;
            if (includeCounterpart(counterpartId) && _pairs.TryGetValue(key, out var pair) && HasCombatActivity(pair))
            {
                observedAtMilliseconds = Math.Max(observedAtMilliseconds, pair.LastObserved);
                hasActivity = true;
            }
        }

        return hasActivity;
    }

    private static bool HasCombatActivity(CombatMechanicPairRecord pair) =>
        pair.Modifiers != DamageModifiers.None ||
        pair.HitCount > 0 ||
        pair.AttemptCount > 0 ||
        pair.EvadeCount > 0 ||
        pair.InvincibleCount > 0 ||
        pair.MultiHitCount > 0 ||
        pair.MultiHitSubCount > 0;

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

        var count = Math.Min(maxChanges, _changeLog.Count - start);
        if (count < _changeLog.Count - start)
        {
            var lastRevision = _changeLog[start + count - 1].Revision;
            if (_changeLog[start + count].Revision == lastRevision)
            {
                while (count > 0 && _changeLog[start + count - 1].Revision == lastRevision)
                    count--;
                if (count == 0)
                {
                    count = 1;
                    while (start + count < _changeLog.Count && _changeLog[start + count].Revision == lastRevision)
                        count++;
                }
            }
        }

        return (start, count);
    }

    private CombatantMechanicRecord GetOrAddCombatant(int combatantId, long observedAtMilliseconds)
    {
        ref var combatant = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);
        if (!exists)
            combatant = new CombatantMechanicRecord(combatantId, observedAtMilliseconds);
        return combatant!;
    }
}

public readonly record struct CombatMechanicEventRecord(
    int SourceId,
    int TargetId,
    CombatWireObservation Observation,
    CombatMechanicOccurrence Mechanic,
    CombatEventKey EventKey,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    RawPacketReference Raw,
    long Revision);

public sealed class CombatMechanicPairRecord(int sourceId, int targetId, long observedAtMilliseconds)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public DamageModifiers Modifiers { get; private set; }
    public int HitCount { get; private set; }
    public int AttemptCount { get; private set; }
    public int EvadeCount { get; private set; }
    public int InvincibleCount { get; private set; }
    public int MultiHitCount { get; private set; }
    public int MultiHitSubCount { get; private set; }
    public int LastSkillCode { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void Apply(in CombatMechanicOccurrence mechanic, int skillCode, long observedAtMilliseconds, long revision)
    {
        Modifiers |= mechanic.Modifiers;
        HitCount += mechanic.HitCount;
        AttemptCount += mechanic.AttemptCount;
        EvadeCount += mechanic.EvadeCount;
        InvincibleCount += mechanic.InvincibleCount;
        MultiHitCount += mechanic.MultiHitCount;
        MultiHitSubCount += mechanic.MultiHitSubCount;
        LastSkillCode = skillCode;
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }

    internal void Restore(in CombatMechanicPairRecordSnapshot snapshot)
    {
        Modifiers = snapshot.Modifiers;
        HitCount = snapshot.HitCount;
        AttemptCount = snapshot.AttemptCount;
        EvadeCount = snapshot.EvadeCount;
        InvincibleCount = snapshot.InvincibleCount;
        MultiHitCount = snapshot.MultiHitCount;
        MultiHitSubCount = snapshot.MultiHitSubCount;
        LastSkillCode = snapshot.LastSkillCode;
        FirstObserved = snapshot.FirstObserved;
        LastObserved = snapshot.LastObserved;
        Revision = snapshot.Revision;
    }
}

public sealed class CombatantMechanicRecord(int combatantId, long observedAtMilliseconds)
{
    public int CombatantId { get; } = combatantId;
    public int OutgoingHits { get; private set; }
    public int OutgoingAttempts { get; private set; }
    public int OutgoingEvades { get; private set; }
    public int OutgoingInvincibles { get; private set; }
    public int OutgoingMultiHits { get; private set; }
    public int IncomingHits { get; private set; }
    public int IncomingAttempts { get; private set; }
    public int IncomingEvades { get; private set; }
    public int IncomingInvincibles { get; private set; }
    public int IncomingMultiHits { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void ApplyOutgoing(in CombatMechanicOccurrence mechanic, long observedAtMilliseconds, long revision)
    {
        OutgoingHits += mechanic.HitCount;
        OutgoingAttempts += mechanic.AttemptCount;
        OutgoingEvades += mechanic.EvadeCount;
        OutgoingInvincibles += mechanic.InvincibleCount;
        OutgoingMultiHits += mechanic.MultiHitCount;
        ApplyObserved(observedAtMilliseconds, revision);
    }

    internal void ApplyIncoming(in CombatMechanicOccurrence mechanic, long observedAtMilliseconds, long revision)
    {
        IncomingHits += mechanic.HitCount;
        IncomingAttempts += mechanic.AttemptCount;
        IncomingEvades += mechanic.EvadeCount;
        IncomingInvincibles += mechanic.InvincibleCount;
        IncomingMultiHits += mechanic.MultiHitCount;
        ApplyObserved(observedAtMilliseconds, revision);
    }

    internal void Restore(in CombatantMechanicRecordSnapshot snapshot)
    {
        OutgoingHits = snapshot.OutgoingHits;
        OutgoingAttempts = snapshot.OutgoingAttempts;
        OutgoingEvades = snapshot.OutgoingEvades;
        OutgoingInvincibles = snapshot.OutgoingInvincibles;
        OutgoingMultiHits = snapshot.OutgoingMultiHits;
        IncomingHits = snapshot.IncomingHits;
        IncomingAttempts = snapshot.IncomingAttempts;
        IncomingEvades = snapshot.IncomingEvades;
        IncomingInvincibles = snapshot.IncomingInvincibles;
        IncomingMultiHits = snapshot.IncomingMultiHits;
        FirstObserved = snapshot.FirstObserved;
        LastObserved = snapshot.LastObserved;
        Revision = snapshot.Revision;
    }

    private void ApplyObserved(long observedAtMilliseconds, long revision)
    {
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }
}

internal sealed record MechanicStoreSnapshot(
    CombatMechanicPairRecordSnapshot[] Pairs,
    CombatantMechanicRecordSnapshot[] Combatants,
    CombatMechanicEventRecord[] Events,
    CombatantDetailRevisionSnapshot[] DetailRevisions,
    long Revision);

internal readonly record struct CombatMechanicPairRecordSnapshot(
    int SourceId,
    int TargetId,
    DamageModifiers Modifiers,
    int HitCount,
    int AttemptCount,
    int EvadeCount,
    int InvincibleCount,
    int MultiHitCount,
    int MultiHitSubCount,
    int LastSkillCode,
    long FirstObserved,
    long LastObserved,
    long Revision)
{
    public static CombatMechanicPairRecordSnapshot From(CombatMechanicPairRecord record) => new(
        record.SourceId,
        record.TargetId,
        record.Modifiers,
        record.HitCount,
        record.AttemptCount,
        record.EvadeCount,
        record.InvincibleCount,
        record.MultiHitCount,
        record.MultiHitSubCount,
        record.LastSkillCode,
        record.FirstObserved,
        record.LastObserved,
        record.Revision);

    public CombatMechanicPairRecord ToRecord()
    {
        var record = new CombatMechanicPairRecord(SourceId, TargetId, FirstObserved);
        record.Restore(in this);
        return record;
    }
}

internal readonly record struct CombatantMechanicRecordSnapshot(
    int CombatantId,
    int OutgoingHits,
    int OutgoingAttempts,
    int OutgoingEvades,
    int OutgoingInvincibles,
    int OutgoingMultiHits,
    int IncomingHits,
    int IncomingAttempts,
    int IncomingEvades,
    int IncomingInvincibles,
    int IncomingMultiHits,
    long FirstObserved,
    long LastObserved,
    long Revision)
{
    public static CombatantMechanicRecordSnapshot From(CombatantMechanicRecord record) => new(
        record.CombatantId,
        record.OutgoingHits,
        record.OutgoingAttempts,
        record.OutgoingEvades,
        record.OutgoingInvincibles,
        record.OutgoingMultiHits,
        record.IncomingHits,
        record.IncomingAttempts,
        record.IncomingEvades,
        record.IncomingInvincibles,
        record.IncomingMultiHits,
        record.FirstObserved,
        record.LastObserved,
        record.Revision);

    public CombatantMechanicRecord ToRecord()
    {
        var record = new CombatantMechanicRecord(CombatantId, FirstObserved);
        record.Restore(in this);
        return record;
    }
}
