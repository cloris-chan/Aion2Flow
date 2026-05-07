using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class CombatStore : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private readonly Dictionary<(int Source, int Target), CombatPairRecord> _pairs = [];
    private readonly Dictionary<int, CombatantRecord> _combatants = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _outgoingBySource = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _incomingByTarget = [];
    private readonly List<CombatEventRecord> _events = [];
    private readonly List<CombatSnapshotChange> _changeLog = [];
    private readonly Dictionary<int, long> _detailRevisionByCombatant = [];
    private long _revision;

    public IReadOnlyDictionary<(int Source, int Target), CombatPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantRecord> Combatants => _combatants;
    public IReadOnlyList<CombatEventRecord> Events => _events;
    public long Revision => _revision;

    public void ApplyCombat(int sourceId, int targetId, long damage, int hitCount, int attemptCount, int skillCode)
        => ApplyCombat(sourceId, targetId, new CombatObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = hitCount,
            AttemptCount = attemptCount,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

    public void ApplyCombat(int sourceId, int targetId, in CombatObservation observation)
        => ApplyCombat(sourceId, targetId, in observation, 0);

    public void ApplyCombat(int sourceId, int targetId, in CombatObservation observation, long observedAtMilliseconds)
    {
        _revision++;
        var contributesDamage = ContributesDamage(in observation);
        var contributesHealing = ContributesHealing(in observation);
        var contributesShieldGrant = ContributesShieldGrant(in observation);
        var contributesShieldAbsorbed = ContributesShieldAbsorbed(in observation);
        var hitCount = contributesDamage ? observation.HitCount : 0;
        var attemptCount = contributesDamage ? observation.AttemptCount : 0;
        var evadeCount = contributesDamage && (observation.Modifiers & DamageModifiers.Evade) != 0 ? attemptCount : 0;
        var invincibleCount = contributesDamage && (observation.Modifiers & DamageModifiers.Invincible) != 0 ? attemptCount : 0;
        var multiHitCount = contributesDamage && (observation.Modifiers & DamageModifiers.MultiHit) != 0 ? 1 : 0;
        _events.Add(new CombatEventRecord
        {
            SourceId = sourceId,
            TargetId = targetId,
            Observation = observation,
            ObservedAtMilliseconds = observedAtMilliseconds,
            Revision = _revision,
            ContributesDamage = contributesDamage,
            ContributesHealing = contributesHealing,
            ContributesShieldGrant = contributesShieldGrant,
            ContributesShieldAbsorbed = contributesShieldAbsorbed,
            HitCount = hitCount,
            AttemptCount = attemptCount,
            EvadeCount = evadeCount,
            InvincibleCount = invincibleCount,
            MultiHitCount = multiHitCount
        });

        var pairKey = (sourceId, targetId);
        if (!_pairs.TryGetValue(pairKey, out var pair))
        {
            pair = new CombatPairRecord { SourceId = sourceId, TargetId = targetId };
            _pairs[pairKey] = pair;
        }
        pair.TotalDamage += contributesDamage ? observation.Damage : 0;
        pair.TotalHealing += contributesHealing ? observation.Damage : 0;
        pair.TotalShield += contributesShieldGrant ? observation.Damage : 0;
        pair.TotalShieldAbsorbed += contributesShieldAbsorbed ? observation.Damage : 0;
        pair.ShieldCount += contributesShieldGrant ? 1 : 0;
        pair.ShieldAbsorbedCount += contributesShieldAbsorbed ? 1 : 0;
        pair.HitCount += hitCount;
        pair.AttemptCount += attemptCount;
        pair.EvadeCount += evadeCount;
        pair.InvincibleCount += invincibleCount;
        pair.MultiHitCount += multiHitCount;
        pair.LastSkillCode = observation.SkillCode;
        pair.Revision = _revision;
        ApplyObservedAt(pair, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, _revision));
        MarkDetailRevision(sourceId, _revision);
        MarkDetailRevision(targetId, _revision);

        var source = GetOrAddCombatant(sourceId);
        source.OutgoingDamage += contributesDamage ? observation.Damage : 0;
        source.OutgoingHealing += contributesHealing ? observation.Damage : 0;
        source.OutgoingShield += contributesShieldGrant ? observation.Damage : 0;
        source.OutgoingShieldAbsorbed += contributesShieldAbsorbed ? observation.Damage : 0;
        source.OutgoingShieldCount += contributesShieldGrant ? 1 : 0;
        source.OutgoingShieldAbsorbedCount += contributesShieldAbsorbed ? 1 : 0;
        source.OutgoingHits += hitCount;
        source.OutgoingAttempts += attemptCount;
        source.OutgoingEvades += evadeCount;
        source.OutgoingInvincibles += invincibleCount;
        source.OutgoingMultiHits += multiHitCount;
        source.Revision = _revision;
        ApplyObservedAt(source, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, _revision));

        if (targetId > 0)
        {
            var target = GetOrAddCombatant(targetId);
            target.IncomingDamage += contributesDamage ? observation.Damage : 0;
            target.IncomingHealing += contributesHealing ? observation.Damage : 0;
            target.IncomingShield += contributesShieldGrant ? observation.Damage : 0;
            target.IncomingShieldAbsorbed += contributesShieldAbsorbed ? observation.Damage : 0;
            target.IncomingShieldCount += contributesShieldGrant ? 1 : 0;
            target.IncomingShieldAbsorbedCount += contributesShieldAbsorbed ? 1 : 0;
            target.IncomingHits += hitCount;
            target.IncomingAttempts += attemptCount;
            target.IncomingEvades += evadeCount;
            target.IncomingInvincibles += invincibleCount;
            target.IncomingMultiHits += multiHitCount;
            target.Revision = _revision;
            ApplyObservedAt(target, observedAtMilliseconds);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, _revision));
        }

        if (!_outgoingBySource.TryGetValue(sourceId, out var outgoing))
        {
            outgoing = [];
            _outgoingBySource[sourceId] = outgoing;
        }
        outgoing.Add(pairKey);

        if (targetId > 0)
        {
            if (!_incomingByTarget.TryGetValue(targetId, out var incoming))
            {
                incoming = [];
                _incomingByTarget[targetId] = incoming;
            }
            incoming.Add(pairKey);
        }
    }

    private static bool ContributesDamage(in CombatObservation observation)
    {
        if (observation.EventKind == CombatEventKind.Damage &&
            observation.ValueKind is CombatValueKind.Damage or CombatValueKind.PeriodicDamage or CombatValueKind.DrainDamage or CombatValueKind.Unknown &&
            (observation.AttemptCount > 0 || (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0))
        {
            return true;
        }

        return observation.ValueKind switch
        {
            CombatValueKind.Damage => observation.Damage > 0,
            CombatValueKind.PeriodicDamage => observation.Damage > 0,
            CombatValueKind.DrainDamage => observation.Damage > 0,
            CombatValueKind.Unknown => observation.EventKind == CombatEventKind.Damage && observation.Damage > 0,
            _ => false
        };
    }

    private static bool ContributesHealing(in CombatObservation observation) =>
        observation.ValueKind switch
        {
            CombatValueKind.Healing => observation.Damage > 0,
            CombatValueKind.PeriodicHealing => observation.Damage > 0,
            CombatValueKind.DrainHealing => observation.Damage > 0,
            _ => observation.EventKind == CombatEventKind.Healing && observation.Damage > 0
        };

    private static bool ContributesShieldGrant(in CombatObservation observation) =>
        observation.ValueKind == CombatValueKind.Shield && observation.EffectTag != PacketEffectTag.ShieldAbsorbed && observation.Damage > 0;

    private static bool ContributesShieldAbsorbed(in CombatObservation observation) =>
        observation.ValueKind == CombatValueKind.Shield && observation.EffectTag == PacketEffectTag.ShieldAbsorbed && observation.Damage > 0;

    private static void ApplyObservedAt(CombatPairRecord record, long observedAtMilliseconds)
    {
        if (observedAtMilliseconds <= 0)
            return;

        record.FirstObserved = record.FirstObserved > 0 ? Math.Min(record.FirstObserved, observedAtMilliseconds) : observedAtMilliseconds;
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    private static void ApplyObservedAt(CombatantRecord record, long observedAtMilliseconds)
    {
        if (observedAtMilliseconds <= 0)
            return;

        record.FirstObserved = record.FirstObserved > 0 ? Math.Min(record.FirstObserved, observedAtMilliseconds) : observedAtMilliseconds;
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatPairRecord? pair) =>
        _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantRecord? combatant) =>
        _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int, int)> GetOutgoingPairs(int sourceId) =>
        _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyCollection<(int, int)> GetIncomingPairs(int targetId) =>
        _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    public long GetCombatantDetailRevision(int combatantId) =>
        combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    private CombatantRecord GetOrAddCombatant(int combatantId)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);

        if (!exists)
        {
            record = new CombatantRecord { CombatantId = combatantId };
        }

        return record!;
    }

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        _detailRevisionByCombatant[combatantId] = _detailRevisionByCombatant.TryGetValue(combatantId, out var current)
            ? Math.Max(current, revision)
            : revision;
    }

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

    public SnapshotChangeCursor CreateCursor(long afterRevision) =>
        new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
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
            return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, _revision, [], false);

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
        var changes = _changeLog.GetRange(start, count);
        return new SnapshotChangeBatch<CombatSnapshotChange>(
            cursor.Revision,
            changes[^1].Revision,
            changes,
            start + count < _changeLog.Count);
    }
}

public sealed class CombatPairRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long TotalDamage { get; set; }
    public long TotalHealing { get; set; }
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
    public long Revision { get; set; }
}

public sealed class CombatEventRecord
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
