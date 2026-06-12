using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class CombatStore(int eventCapacity = 0, int combatantCapacity = 0, int pairCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private readonly Dictionary<(int Source, int Target), CombatPairRecord> _pairs = pairCapacity > 0 ? new(pairCapacity) : [];
    private readonly Dictionary<int, CombatantRecord> _combatants = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly Dictionary<int, HashSet<(int, int)>> _outgoingBySource = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly Dictionary<int, HashSet<(int, int)>> _incomingByTarget = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly List<CombatEventRecord> _events = eventCapacity > 0 ? new(eventCapacity) : [];
    private readonly List<CombatSnapshotChange> _changeLog = eventCapacity > 0 ? new(ResolveChangeCapacity(eventCapacity)) : [];
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyDictionary<(int Source, int Target), CombatPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantRecord> Combatants => _combatants;
    public IReadOnlyList<CombatEventRecord> Events => _events;
    public ReadOnlySpan<CombatEventRecord> EventSpan => CollectionsMarshal.AsSpan(_events);
    public long Revision => _revision;

    public void EnsureCapacity(int eventCapacity, int combatantCapacity = 0, int pairCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(ResolveChangeCapacity(eventCapacity));
        }

        if (combatantCapacity > 0)
        {
            _combatants.EnsureCapacity(combatantCapacity);
            _outgoingBySource.EnsureCapacity(combatantCapacity);
            _incomingByTarget.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }

        if (pairCapacity > 0)
        {
            _pairs.EnsureCapacity(pairCapacity);
        }
    }

    public void ApplyCombat(int sourceId, int targetId, in CombatObservation observation, long observedAtMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        _revision++;
        var contribution = CombatContributionClassifier.Evaluate(in observation);
        var eventRecord = new CombatEventRecord
        {
            SourceId = sourceId,
            TargetId = targetId,
            Observation = observation,
            ObservedAtMilliseconds = observedAtMilliseconds,
            Revision = _revision,
            ContributesDamage = contribution.CountsAsDamage,
            ContributesHealing = contribution.CountsAsHealing,
            ContributesShieldGrant = contribution.CountsAsShieldGrant,
            ContributesShieldAbsorbed = contribution.CountsAsShieldAbsorbed,
            HitCount = contribution.HitCount,
            AttemptCount = contribution.AttemptCount,
            EvadeCount = contribution.EvadeCount,
            InvincibleCount = contribution.InvincibleCount,
            MultiHitCount = contribution.MultiHitCount
        };
        _events.Add(eventRecord);
        var totalHealing = contribution.HealingAmount;
        var periodicHealing = observation.ValueKind == CombatValueKind.PeriodicHealing ? contribution.HealingAmount : 0;
        var drainDamage = observation.ValueKind == CombatValueKind.DrainDamage ? contribution.DamageAmount : 0;
        var drainHealing = observation.ValueKind == CombatValueKind.DrainHealing ? contribution.HealingAmount : 0;
        var regenerationHealing = observation.EffectTag == PacketEffectTag.RegenerationHealing ? contribution.HealingAmount : 0;

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var pairExists);
        CombatPairRecord pairRecord;
        if (!pairExists)
        {
            pairRecord = new CombatPairRecord
            {
                SourceId = sourceId,
                TargetId = targetId,
                FirstObserved = observedAtMilliseconds,
                LastObserved = observedAtMilliseconds,
                FirstRevision = _revision
            };
            pair = pairRecord;
        }
        else if (pair is { } existingPair)
        {
            pairRecord = existingPair;
        }
        else
        {
            throw new InvalidOperationException("Combat pair dictionary returned a null record.");
        }
        pairRecord.TotalDamage += contribution.DamageAmount;
        pairRecord.TotalHealing += totalHealing;
        pairRecord.TotalPeriodicHealing += periodicHealing;
        pairRecord.TotalDrainDamage += drainDamage;
        pairRecord.TotalDrainHealing += drainHealing;
        pairRecord.TotalRegenerationHealing += regenerationHealing;
        pairRecord.TotalShield += contribution.ShieldGrantAmount;
        pairRecord.TotalShieldAbsorbed += contribution.ShieldAbsorbedAmount;
        pairRecord.ShieldCount += contribution.ShieldGrantCount;
        pairRecord.ShieldAbsorbedCount += contribution.ShieldAbsorbedCount;
        pairRecord.HitCount += contribution.HitCount;
        pairRecord.AttemptCount += contribution.AttemptCount;
        pairRecord.EvadeCount += contribution.EvadeCount;
        pairRecord.InvincibleCount += contribution.InvincibleCount;
        pairRecord.MultiHitCount += contribution.MultiHitCount;
        pairRecord.LastSkillCode = observation.SkillCode;
        pairRecord.FirstRevision = pairRecord.FirstRevision > 0 ? Math.Min(pairRecord.FirstRevision, _revision) : _revision;
        pairRecord.Revision = _revision;
        ApplyObservedAt(pairRecord, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, _revision));
        MarkDetailRevision(sourceId, _revision);
        MarkDetailRevision(targetId, _revision);

        var source = GetOrAddCombatant(sourceId, observedAtMilliseconds);
        source.OutgoingDamage += contribution.DamageAmount;
        source.OutgoingHealing += totalHealing;
        source.OutgoingShield += contribution.ShieldGrantAmount;
        source.OutgoingShieldAbsorbed += contribution.ShieldAbsorbedAmount;
        source.OutgoingShieldCount += contribution.ShieldGrantCount;
        source.OutgoingShieldAbsorbedCount += contribution.ShieldAbsorbedCount;
        source.OutgoingHits += contribution.HitCount;
        source.OutgoingAttempts += contribution.AttemptCount;
        source.OutgoingEvades += contribution.EvadeCount;
        source.OutgoingInvincibles += contribution.InvincibleCount;
        source.OutgoingMultiHits += contribution.MultiHitCount;
        source.Revision = _revision;
        ApplyObservedAt(source, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, _revision));

        if (targetId > 0)
        {
            var target = GetOrAddCombatant(targetId, observedAtMilliseconds);
            target.IncomingDamage += contribution.DamageAmount;
            target.IncomingHealing += contribution.HealingAmount;
            target.IncomingShield += contribution.ShieldGrantAmount;
            target.IncomingShieldAbsorbed += contribution.ShieldAbsorbedAmount;
            target.IncomingShieldCount += contribution.ShieldGrantCount;
            target.IncomingShieldAbsorbedCount += contribution.ShieldAbsorbedCount;
            target.IncomingHits += contribution.HitCount;
            target.IncomingAttempts += contribution.AttemptCount;
            target.IncomingEvades += contribution.EvadeCount;
            target.IncomingInvincibles += contribution.InvincibleCount;
            target.IncomingMultiHits += contribution.MultiHitCount;
            target.Revision = _revision;
            ApplyObservedAt(target, observedAtMilliseconds);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, _revision));
        }

        AddPairIndex(pairKey);
    }

    private void AddPairIndex((int Source, int Target) pairKey)
    {
        AddOutgoingPairIndex(pairKey);
        AddIncomingPairIndex(pairKey);
    }

    private void AddOutgoingPairIndex((int Source, int Target) pairKey)
    {
        if (pairKey.Source <= 0)
            return;

        ref var outgoing = ref CollectionsMarshal.GetValueRefOrAddDefault(_outgoingBySource, pairKey.Source, out var outgoingExists);
        if (!outgoingExists)
            outgoing = [];
        outgoing!.Add(pairKey);
    }

    private void AddIncomingPairIndex((int Source, int Target) pairKey)
    {
        if (pairKey.Target <= 0)
            return;

        ref var incoming = ref CollectionsMarshal.GetValueRefOrAddDefault(_incomingByTarget, pairKey.Target, out var incomingExists);
        if (!incomingExists)
            incoming = [];
        incoming!.Add(pairKey);
    }

    private static void ApplyObservedAt(CombatPairRecord record, long observedAtMilliseconds)
    {
        record.FirstObserved = Math.Min(record.FirstObserved, observedAtMilliseconds);
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    private static void ApplyObservedAt(CombatantRecord record, long observedAtMilliseconds)
    {
        record.FirstObserved = Math.Min(record.FirstObserved, observedAtMilliseconds);
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatPairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantRecord? combatant) => _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int, int)> GetOutgoingPairs(int sourceId) => _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyCollection<(int, int)> GetIncomingPairs(int targetId) => _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    public bool TryGetLastCombatActivityObservedAt(int combatantId, out long observedAtMilliseconds)
    {
        observedAtMilliseconds = 0;
        if (combatantId <= 0)
            return false;

        var hasActivity = TryApplyLastCombatActivityObservedAt(_outgoingBySource, combatantId, ref observedAtMilliseconds);
        hasActivity |= TryApplyLastCombatActivityObservedAt(_incomingByTarget, combatantId, ref observedAtMilliseconds);
        return hasActivity;
    }

    public ref readonly CombatEventRecord GetEvent(int index) => ref CollectionsMarshal.AsSpan(_events)[index];

    public bool TryGetEventByRevision(long revision, out CombatEventRecord record)
    {
        if (revision > 0 && _events.Count > 0)
        {
            var firstRevision = _events[0].Revision;
            var index = revision - firstRevision;
            if ((ulong)index < (uint)_events.Count)
            {
                var candidate = _events[(int)index];
                if (candidate.Revision == revision)
                {
                    record = candidate;
                    return true;
                }
            }
        }

        record = default;
        return false;
    }

    public long GetCombatantDetailRevision(int combatantId) => combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    internal CombatStoreSnapshot CreateSnapshot()
    {
        var pairs = new CombatPairRecordSnapshot[_pairs.Count];
        var index = 0;
        foreach (var pair in _pairs.Values)
            pairs[index++] = CombatPairRecordSnapshot.From(pair);

        var combatants = new CombatantRecordSnapshot[_combatants.Count];
        index = 0;
        foreach (var combatant in _combatants.Values)
            combatants[index++] = CombatantRecordSnapshot.From(combatant);

        var detailRevisions = new CombatantDetailRevisionSnapshot[_detailRevisionByCombatant.Count];
        index = 0;
        foreach (var (combatantId, revision) in _detailRevisionByCombatant)
            detailRevisions[index++] = new CombatantDetailRevisionSnapshot(combatantId, revision);

        return new CombatStoreSnapshot(pairs, combatants, detailRevisions, _revision);
    }

    internal static CombatStore FromSnapshot(CombatStoreSnapshot snapshot)
    {
        var store = new CombatStore(0, snapshot.Combatants.Length, snapshot.Pairs.Length);
        store._revision = snapshot.Revision;

        for (var i = 0; i < snapshot.Pairs.Length; i++)
        {
            var pair = snapshot.Pairs[i].ToRecord();
            var key = (pair.SourceId, pair.TargetId);
            store._pairs[key] = pair;
            store.AddPairIndex(key);
        }

        for (var i = 0; i < snapshot.Combatants.Length; i++)
        {
            var combatant = snapshot.Combatants[i].ToRecord();
            store._combatants[combatant.CombatantId] = combatant;
        }

        for (var i = 0; i < snapshot.DetailRevisions.Length; i++)
        {
            var detail = snapshot.DetailRevisions[i];
            store._detailRevisionByCombatant[detail.CombatantId] = detail.Revision;
        }

        return store;
    }

    private CombatantRecord GetOrAddCombatant(int combatantId, long observedAtMilliseconds)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);

        if (!exists)
        {
            record = new CombatantRecord
            {
                CombatantId = combatantId,
                FirstObserved = observedAtMilliseconds,
                LastObserved = observedAtMilliseconds
            };
        }

        return record!;
    }

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_detailRevisionByCombatant, combatantId, out var exists);
        current = exists ? Math.Max(current, revision) : revision;
    }

    private bool TryApplyLastCombatActivityObservedAt(Dictionary<int, HashSet<(int, int)>> index, int combatantId, ref long observedAtMilliseconds)
    {
        if (!index.TryGetValue(combatantId, out var pairs))
            return false;

        var hasActivity = false;
        foreach (var key in pairs)
        {
            if (_pairs.TryGetValue(key, out var pair) && HasCombatActivity(pair))
            {
                observedAtMilliseconds = Math.Max(observedAtMilliseconds, pair.LastObserved);
                hasActivity = true;
            }
        }

        return hasActivity;
    }

    private static bool HasCombatActivity(CombatPairRecord pair) =>
        pair.TotalDamage > 0 ||
        pair.TotalHealing > 0 ||
        pair.TotalShield > 0 ||
        pair.TotalShieldAbsorbed > 0 ||
        pair.AttemptCount > 0 ||
        pair.HitCount > 0 ||
        pair.EvadeCount > 0 ||
        pair.InvincibleCount > 0 ||
        pair.MultiHitCount > 0;

    public void Clear()
    {
        _pairs.Clear();
        _combatants.Clear();
        _outgoingBySource.Clear();
        _incomingByTarget.Clear();
        _events.Clear();
        _changeLog.Clear();
        _detailRevisionByCombatant.Clear();
        _revision = 0;
    }

    public SnapshotChangeCursor CreateCursor(long afterRevision) => new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
    {
        maxChanges = Math.Max(1, maxChanges);
        var (Start, Count) = ResolveChangeReadBounds(cursor, maxChanges);
        if (Count == 0)
            return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, _revision, [], false);

        var changes = _changeLog.GetRange(Start, Count);
        return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, changes[^1].Revision, changes, Start + Count < _changeLog.Count);
    }

    public SnapshotChangeCopyResult CopyChanges(SnapshotChangeCursor cursor, Span<CombatSnapshotChange> destination)
    {
        var (Start, Count) = ResolveChangeReadBounds(cursor, destination.Length);
        if (Count == 0)
            return new SnapshotChangeCopyResult(cursor, cursor.Revision, _revision, 0, false);

        for (var i = 0; i < Count; i++)
            destination[i] = _changeLog[Start + i];

        var toRevision = _changeLog[Start + Count - 1].Revision;
        return new SnapshotChangeCopyResult(new SnapshotChangeCursor(toRevision, 0), cursor.Revision, toRevision, Count, Start + Count < _changeLog.Count);
    }

    private (int Start, int Count) ResolveChangeReadBounds(SnapshotChangeCursor cursor, int maxChanges)
    {
        maxChanges = Math.Max(1, maxChanges);

        int lo = 0, hi = _changeLog.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_changeLog[mid].Revision <= cursor.Revision)
                lo = mid + 1;
            else
                hi = mid;
        }

        int start = lo + cursor.Offset;
        if (start >= _changeLog.Count)
            return default;

        int count = Math.Min(maxChanges, _changeLog.Count - start);
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

    private static int ResolveChangeCapacity(int eventCapacity) => eventCapacity <= 0 ? 0 : eventCapacity <= int.MaxValue / 3 ? eventCapacity * 3 : int.MaxValue;

}

internal sealed record CombatStoreSnapshot(
    CombatPairRecordSnapshot[] Pairs,
    CombatantRecordSnapshot[] Combatants,
    CombatantDetailRevisionSnapshot[] DetailRevisions,
    long Revision);

internal readonly record struct CombatantDetailRevisionSnapshot(int CombatantId, long Revision);

internal readonly record struct CombatPairRecordSnapshot(
    int SourceId,
    int TargetId,
    long TotalDamage,
    long TotalHealing,
    long TotalPeriodicHealing,
    long TotalDrainDamage,
    long TotalDrainHealing,
    long TotalRegenerationHealing,
    long TotalShield,
    long TotalShieldAbsorbed,
    int ShieldCount,
    int ShieldAbsorbedCount,
    int HitCount,
    int AttemptCount,
    int EvadeCount,
    int InvincibleCount,
    int MultiHitCount,
    int LastSkillCode,
    long FirstObserved,
    long LastObserved,
    long FirstRevision,
    long Revision)
{
    public static CombatPairRecordSnapshot From(CombatPairRecord record) => new(
        record.SourceId,
        record.TargetId,
        record.TotalDamage,
        record.TotalHealing,
        record.TotalPeriodicHealing,
        record.TotalDrainDamage,
        record.TotalDrainHealing,
        record.TotalRegenerationHealing,
        record.TotalShield,
        record.TotalShieldAbsorbed,
        record.ShieldCount,
        record.ShieldAbsorbedCount,
        record.HitCount,
        record.AttemptCount,
        record.EvadeCount,
        record.InvincibleCount,
        record.MultiHitCount,
        record.LastSkillCode,
        record.FirstObserved,
        record.LastObserved,
        record.FirstRevision,
        record.Revision);

    public CombatPairRecord ToRecord() => new()
    {
        SourceId = SourceId,
        TargetId = TargetId,
        TotalDamage = TotalDamage,
        TotalHealing = TotalHealing,
        TotalPeriodicHealing = TotalPeriodicHealing,
        TotalDrainDamage = TotalDrainDamage,
        TotalDrainHealing = TotalDrainHealing,
        TotalRegenerationHealing = TotalRegenerationHealing,
        TotalShield = TotalShield,
        TotalShieldAbsorbed = TotalShieldAbsorbed,
        ShieldCount = ShieldCount,
        ShieldAbsorbedCount = ShieldAbsorbedCount,
        HitCount = HitCount,
        AttemptCount = AttemptCount,
        EvadeCount = EvadeCount,
        InvincibleCount = InvincibleCount,
        MultiHitCount = MultiHitCount,
        LastSkillCode = LastSkillCode,
        FirstObserved = FirstObserved,
        LastObserved = LastObserved,
        FirstRevision = FirstRevision,
        Revision = Revision
    };
}

internal readonly record struct CombatantRecordSnapshot(
    int CombatantId,
    long OutgoingDamage,
    int OutgoingHits,
    int OutgoingAttempts,
    int OutgoingEvades,
    int OutgoingInvincibles,
    int OutgoingMultiHits,
    long IncomingDamage,
    int IncomingHits,
    int IncomingAttempts,
    int IncomingEvades,
    int IncomingInvincibles,
    int IncomingMultiHits,
    long OutgoingHealing,
    long IncomingHealing,
    long OutgoingShield,
    long IncomingShield,
    long OutgoingShieldAbsorbed,
    long IncomingShieldAbsorbed,
    int OutgoingShieldCount,
    int IncomingShieldCount,
    int OutgoingShieldAbsorbedCount,
    int IncomingShieldAbsorbedCount,
    long FirstObserved,
    long LastObserved,
    long Revision)
{
    public static CombatantRecordSnapshot From(CombatantRecord record) => new(
        record.CombatantId,
        record.OutgoingDamage,
        record.OutgoingHits,
        record.OutgoingAttempts,
        record.OutgoingEvades,
        record.OutgoingInvincibles,
        record.OutgoingMultiHits,
        record.IncomingDamage,
        record.IncomingHits,
        record.IncomingAttempts,
        record.IncomingEvades,
        record.IncomingInvincibles,
        record.IncomingMultiHits,
        record.OutgoingHealing,
        record.IncomingHealing,
        record.OutgoingShield,
        record.IncomingShield,
        record.OutgoingShieldAbsorbed,
        record.IncomingShieldAbsorbed,
        record.OutgoingShieldCount,
        record.IncomingShieldCount,
        record.OutgoingShieldAbsorbedCount,
        record.IncomingShieldAbsorbedCount,
        record.FirstObserved,
        record.LastObserved,
        record.Revision);

    public CombatantRecord ToRecord() => new()
    {
        CombatantId = CombatantId,
        OutgoingDamage = OutgoingDamage,
        OutgoingHits = OutgoingHits,
        OutgoingAttempts = OutgoingAttempts,
        OutgoingEvades = OutgoingEvades,
        OutgoingInvincibles = OutgoingInvincibles,
        OutgoingMultiHits = OutgoingMultiHits,
        IncomingDamage = IncomingDamage,
        IncomingHits = IncomingHits,
        IncomingAttempts = IncomingAttempts,
        IncomingEvades = IncomingEvades,
        IncomingInvincibles = IncomingInvincibles,
        IncomingMultiHits = IncomingMultiHits,
        OutgoingHealing = OutgoingHealing,
        IncomingHealing = IncomingHealing,
        OutgoingShield = OutgoingShield,
        IncomingShield = IncomingShield,
        OutgoingShieldAbsorbed = OutgoingShieldAbsorbed,
        IncomingShieldAbsorbed = IncomingShieldAbsorbed,
        OutgoingShieldCount = OutgoingShieldCount,
        IncomingShieldCount = IncomingShieldCount,
        OutgoingShieldAbsorbedCount = OutgoingShieldAbsorbedCount,
        IncomingShieldAbsorbedCount = IncomingShieldAbsorbedCount,
        FirstObserved = FirstObserved,
        LastObserved = LastObserved,
        Revision = Revision
    };
}

public sealed class CombatPairRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long TotalDamage { get; set; }
    public long TotalHealing { get; set; }
    public long TotalPeriodicHealing { get; set; }
    public long TotalDrainDamage { get; set; }
    public long TotalDrainHealing { get; set; }
    public long TotalRegenerationHealing { get; set; }
    public long TotalShield { get; set; }
    public long TotalShieldAbsorbed { get; set; }
    public int ShieldCount { get; set; }
    public int ShieldAbsorbedCount { get; set; }
    public int HitCount { get; set; }
    public int AttemptCount { get; set; }
    public int EvadeCount { get; set; }
    public int InvincibleCount { get; set; }
    public int MultiHitCount { get; set; }
    public int LastSkillCode { get; set; }
    public long FirstObserved { get; set; }
    public long LastObserved { get; set; }
    public long FirstRevision { get; set; }
    public long Revision { get; set; }

}

public readonly record struct CombatEventRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public CombatObservation Observation { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long Revision { get; init; }
    public bool ContributesDamage { get; init; }
    public bool ContributesHealing { get; init; }
    public bool ContributesShieldGrant { get; init; }
    public bool ContributesShieldAbsorbed { get; init; }
    public int HitCount { get; init; }
    public int AttemptCount { get; init; }
    public int EvadeCount { get; init; }
    public int InvincibleCount { get; init; }
    public int MultiHitCount { get; init; }
}

public sealed class CombatantRecord
{
    public int CombatantId { get; init; }
    public long OutgoingDamage { get; set; }
    public int OutgoingHits { get; set; }
    public int OutgoingAttempts { get; set; }
    public int OutgoingEvades { get; set; }
    public int OutgoingInvincibles { get; set; }
    public int OutgoingMultiHits { get; set; }
    public long IncomingDamage { get; set; }
    public int IncomingHits { get; set; }
    public int IncomingAttempts { get; set; }
    public int IncomingEvades { get; set; }
    public int IncomingInvincibles { get; set; }
    public int IncomingMultiHits { get; set; }
    public long OutgoingHealing { get; set; }
    public long IncomingHealing { get; set; }
    public long OutgoingShield { get; set; }
    public long IncomingShield { get; set; }
    public long OutgoingShieldAbsorbed { get; set; }
    public long IncomingShieldAbsorbed { get; set; }
    public int OutgoingShieldCount { get; set; }
    public int IncomingShieldCount { get; set; }
    public int OutgoingShieldAbsorbedCount { get; set; }
    public int IncomingShieldAbsorbedCount { get; set; }
    public long FirstObserved { get; set; }
    public long LastObserved { get; set; }
    public long Revision { get; set; }
}
