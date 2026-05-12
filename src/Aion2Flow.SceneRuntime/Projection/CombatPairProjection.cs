using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public readonly record struct DirectedPairKey(int SourceId, int TargetId);

public readonly record struct DirectedPairSnapshot
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

public readonly record struct CombatantSummary
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

public static class CombatPairProjection
{
    public static IReadOnlyDictionary<DirectedPairKey, DirectedPairSnapshot> BuildPairSnapshotMap(CombatStore store)
    {
        var pairs = new Dictionary<DirectedPairKey, DirectedPairSnapshot>(store.Pairs.Count);
        foreach (var (_, record) in store.Pairs)
        {
            var pairKey = new DirectedPairKey(record.SourceId, record.TargetId);
            pairs[pairKey] = ToSnapshot(record);
        }

        return pairs;
    }

    public static IReadOnlyDictionary<int, CombatantSummary> BuildCombatantSummaryMap(CombatStore store)
    {
        var combatants = new Dictionary<int, CombatantSummary>(store.Combatants.Count);
        foreach (var (id, record) in store.Combatants)
            combatants[id] = ToSummary(record);
        return combatants;
    }

    public static DirectedPairSnapshot? GetPair(CombatStore store, int sourceId, int targetId) =>
        store.TryGetPair(sourceId, targetId, out var pair) && pair is not null ? ToSnapshot(pair) : null;

    public static CombatantSummary? GetCombatant(CombatStore store, int combatantId) =>
        store.TryGetCombatant(combatantId, out var combatant) && combatant is not null ? ToSummary(combatant) : null;

    public static IReadOnlyList<DirectedPairKey> GetOutgoingPairs(CombatStore store, int sourceId) => ToPairKeys(store.GetOutgoingPairs(sourceId));

    public static IReadOnlyList<DirectedPairKey> GetIncomingPairs(CombatStore store, int targetId) => ToPairKeys(store.GetIncomingPairs(targetId));

    public static IReadOnlyList<CombatDetailEvent> GetDetailEvents(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId) => adapter.CreateDetailEvents(snapshot, combatantId);

    private static DirectedPairSnapshot ToSnapshot(CombatPairRecord record) => new()
    {
        Key = new DirectedPairKey(record.SourceId, record.TargetId),
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

    private static CombatantSummary ToSummary(CombatantRecord record) => new()
    {
        CombatantId = record.CombatantId,
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

    private static DirectedPairKey[] ToPairKeys(IReadOnlyCollection<(int, int)> pairs)
    {
        if (pairs.Count == 0)
            return [];

        var result = new DirectedPairKey[pairs.Count];
        var index = 0;
        foreach (var (sourceId, targetId) in pairs)
            result[index++] = new DirectedPairKey(sourceId, targetId);

        Array.Sort(result, static (left, right) =>
        {
            var cmp = left.SourceId.CompareTo(right.SourceId);
            return cmp != 0 ? cmp : left.TargetId.CompareTo(right.TargetId);
        });
        return result;
    }
}
