namespace Cloris.Aion2Flow.Scene.Projection;

using Cloris.Aion2Flow.Scene.Stores;

public readonly record struct DirectedPairKey(int SourceId, int TargetId);

public sealed class DirectedPairSnapshot
{
    public DirectedPairKey Key { get; init; }
    public long TotalDamage { get; init; }
    public long TotalHealing { get; init; }
    public long TotalShield { get; init; }
    public long TotalShieldAbsorbed { get; init; }
    public int ShieldCount { get; init; }
    public int ShieldAbsorbedCount { get; init; }
    public int HitCount { get; init; }
    public int AttemptCount { get; init; }
    public int EvadeCount { get; init; }
    public int InvincibleCount { get; init; }
    public int MultiHitCount { get; init; }
    public int LastSkillCode { get; init; }
    public long FirstObserved { get; init; }
    public long LastObserved { get; init; }
    public long Revision { get; init; }
}

public sealed class CombatantSummary
{
    public int CombatantId { get; init; }
    public long OutgoingDamage { get; init; }
    public int OutgoingHits { get; init; }
    public int OutgoingAttempts { get; init; }
    public int OutgoingEvades { get; init; }
    public int OutgoingInvincibles { get; init; }
    public int OutgoingMultiHits { get; init; }
    public long IncomingDamage { get; init; }
    public int IncomingHits { get; init; }
    public int IncomingAttempts { get; init; }
    public int IncomingEvades { get; init; }
    public int IncomingInvincibles { get; init; }
    public int IncomingMultiHits { get; init; }
    public long OutgoingHealing { get; init; }
    public long IncomingHealing { get; init; }
    public long OutgoingShield { get; init; }
    public long IncomingShield { get; init; }
    public long OutgoingShieldAbsorbed { get; init; }
    public long IncomingShieldAbsorbed { get; init; }
    public int OutgoingShieldCount { get; init; }
    public int IncomingShieldCount { get; init; }
    public int OutgoingShieldAbsorbedCount { get; init; }
    public int IncomingShieldAbsorbedCount { get; init; }
    public long FirstObserved { get; init; }
    public long LastObserved { get; init; }
    public long Revision { get; init; }
}

public sealed class CombatPairProjection
{
    private readonly Dictionary<DirectedPairKey, DirectedPairSnapshot> _pairs = [];
    private readonly Dictionary<int, CombatantSummary> _combatants = [];
    private readonly Dictionary<int, List<DirectedPairKey>> _outgoingBySource = [];
    private readonly Dictionary<int, List<DirectedPairKey>> _incomingByTarget = [];
    private long _revision;

    public long Revision => _revision;
    public IReadOnlyDictionary<DirectedPairKey, DirectedPairSnapshot> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantSummary> Combatants => _combatants;

    public static CombatPairProjection FromCombatStore(CombatStore store)
    {
        var projection = new CombatPairProjection();
        projection.Rebuild(store);
        return projection;
    }

    public void Rebuild(CombatStore store)
    {
        _pairs.Clear();
        _combatants.Clear();
        _outgoingBySource.Clear();
        _incomingByTarget.Clear();
        _revision = store.Revision;
        foreach (var (_, record) in store.Pairs)
        {
            var pairKey = new DirectedPairKey(record.SourceId, record.TargetId);
            _pairs[pairKey] = new DirectedPairSnapshot
            {
                Key = pairKey,
                TotalDamage = record.TotalDamage,
                TotalHealing = record.TotalHealing,
                TotalShield = record.TotalShield,
                TotalShieldAbsorbed = record.TotalShieldAbsorbed,
                ShieldCount = record.ShieldCount,
                ShieldAbsorbedCount = record.ShieldAbsorbedCount,
                HitCount = record.HitCount,
                AttemptCount = record.AttemptCount,
                EvadeCount = record.EvadeCount,
                InvincibleCount = record.InvincibleCount,
                MultiHitCount = record.MultiHitCount,
                LastSkillCode = record.LastSkillCode,
                FirstObserved = record.FirstObserved,
                LastObserved = record.LastObserved,
                Revision = record.Revision
            };

            if (!_outgoingBySource.TryGetValue(record.SourceId, out var outgoing))
            {
                outgoing = [];
                _outgoingBySource[record.SourceId] = outgoing;
            }
            outgoing.Add(pairKey);

            if (record.TargetId > 0)
            {
                if (!_incomingByTarget.TryGetValue(record.TargetId, out var incoming))
                {
                    incoming = [];
                    _incomingByTarget[record.TargetId] = incoming;
                }
                incoming.Add(pairKey);
            }
        }

        foreach (var (id, record) in store.Combatants)
        {
            _combatants[id] = new CombatantSummary
            {
                CombatantId = id,
                OutgoingDamage = record.OutgoingDamage,
                OutgoingHits = record.OutgoingHits,
                OutgoingAttempts = record.OutgoingAttempts,
                OutgoingEvades = record.OutgoingEvades,
                OutgoingInvincibles = record.OutgoingInvincibles,
                OutgoingMultiHits = record.OutgoingMultiHits,
                IncomingDamage = record.IncomingDamage,
                IncomingHits = record.IncomingHits,
                IncomingAttempts = record.IncomingAttempts,
                IncomingEvades = record.IncomingEvades,
                IncomingInvincibles = record.IncomingInvincibles,
                IncomingMultiHits = record.IncomingMultiHits,
                OutgoingHealing = record.OutgoingHealing,
                IncomingHealing = record.IncomingHealing,
                OutgoingShield = record.OutgoingShield,
                IncomingShield = record.IncomingShield,
                OutgoingShieldAbsorbed = record.OutgoingShieldAbsorbed,
                IncomingShieldAbsorbed = record.IncomingShieldAbsorbed,
                OutgoingShieldCount = record.OutgoingShieldCount,
                IncomingShieldCount = record.IncomingShieldCount,
                OutgoingShieldAbsorbedCount = record.OutgoingShieldAbsorbedCount,
                IncomingShieldAbsorbedCount = record.IncomingShieldAbsorbedCount,
                FirstObserved = record.FirstObserved,
                LastObserved = record.LastObserved,
                Revision = record.Revision
            };
        }
    }

    public IReadOnlyList<DirectedPairKey> GetOutgoingPairs(int sourceId) => _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyList<DirectedPairKey> GetIncomingPairs(int targetId) => _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    public DirectedPairSnapshot? GetPair(int sourceId, int targetId) => _pairs.TryGetValue(new DirectedPairKey(sourceId, targetId), out var pair) ? pair : null;

    public CombatantSummary? GetCombatant(int combatantId) => _combatants.TryGetValue(combatantId, out var c) ? c : null;
}
