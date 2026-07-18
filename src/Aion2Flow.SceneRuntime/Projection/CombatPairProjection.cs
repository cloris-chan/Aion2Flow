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
    public int MultiHitSubCount { get; init; }
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
    public static IReadOnlyDictionary<DirectedPairKey, DirectedPairSnapshot> BuildPairSnapshotMap(CombatStore combat, MechanicStore mechanics, ResourceStore resources)
    {
        var pairs = new Dictionary<DirectedPairKey, DirectedPairSnapshot>(combat.Pairs.Count + mechanics.Pairs.Count + resources.Pairs.Count);
        foreach (var (_, record) in combat.Pairs)
        {
            var pairKey = new DirectedPairKey(record.SourceId, record.TargetId);
            mechanics.TryGetPair(record.SourceId, record.TargetId, out var mechanic);
            resources.TryGetPair(record.SourceId, record.TargetId, out var resource);
            pairs[pairKey] = ToSnapshot(record.SourceId, record.TargetId, record, mechanic, resource);
        }

        foreach (var (_, mechanic) in mechanics.Pairs)
        {
            var pairKey = new DirectedPairKey(mechanic.SourceId, mechanic.TargetId);
            if (!pairs.ContainsKey(pairKey))
            {
                resources.TryGetPair(mechanic.SourceId, mechanic.TargetId, out var resource);
                pairs.Add(pairKey, ToSnapshot(mechanic.SourceId, mechanic.TargetId, null, mechanic, resource));
            }
        }

        foreach (var (_, resource) in resources.Pairs)
        {
            var pairKey = new DirectedPairKey(resource.SourceId, resource.TargetId);
            if (!pairs.ContainsKey(pairKey))
                pairs.Add(pairKey, ToSnapshot(resource.SourceId, resource.TargetId, null, null, resource));
        }

        return pairs;
    }

    public static IReadOnlyDictionary<int, CombatantSummary> BuildCombatantSummaryMap(CombatStore combat, MechanicStore mechanics, ResourceStore resources)
    {
        var resourceCombatants = BuildResourceCombatants(resources);
        var combatants = new Dictionary<int, CombatantSummary>(combat.Combatants.Count + mechanics.Combatants.Count + resourceCombatants.Count);
        foreach (var (id, record) in combat.Combatants)
        {
            mechanics.TryGetCombatant(id, out var mechanic);
            resourceCombatants.TryGetValue(id, out var resource);
            combatants[id] = ToSummary(id, record, mechanic, resource);
        }

        foreach (var (id, mechanic) in mechanics.Combatants)
        {
            if (!combatants.ContainsKey(id))
            {
                resourceCombatants.TryGetValue(id, out var resource);
                combatants.Add(id, ToSummary(id, null, mechanic, resource));
            }
        }

        foreach (var (id, resource) in resourceCombatants)
        {
            if (!combatants.ContainsKey(id))
                combatants.Add(id, ToSummary(id, null, null, resource));
        }
        return combatants;
    }

    public static DirectedPairSnapshot? GetPair(CombatStore combat, MechanicStore mechanics, ResourceStore resources, int sourceId, int targetId)
    {
        combat.TryGetPair(sourceId, targetId, out var pair);
        mechanics.TryGetPair(sourceId, targetId, out var mechanic);
        resources.TryGetPair(sourceId, targetId, out var resource);
        return pair is null && mechanic is null && resource is null ? null : ToSnapshot(sourceId, targetId, pair, mechanic, resource);
    }

    public static CombatantSummary? GetCombatant(CombatStore combat, MechanicStore mechanics, ResourceStore resources, int combatantId)
    {
        combat.TryGetCombatant(combatantId, out var combatant);
        mechanics.TryGetCombatant(combatantId, out var mechanic);
        var hasResource = TryGetResourceCombatant(resources, combatantId, out var resource);
        return combatant is null && mechanic is null && !hasResource ? null : ToSummary(combatantId, combatant, mechanic, resource);
    }

    public static IReadOnlyList<DirectedPairKey> GetOutgoingPairs(CombatStore combat, MechanicStore mechanics, ResourceStore resources, int sourceId) =>
        ToPairKeys(combat.GetOutgoingPairs(sourceId), mechanics.GetOutgoingPairs(sourceId), resources.GetOutgoingPairs(sourceId));

    public static IReadOnlyList<DirectedPairKey> GetIncomingPairs(CombatStore combat, MechanicStore mechanics, ResourceStore resources, int targetId) =>
        ToPairKeys(combat.GetIncomingPairs(targetId), mechanics.GetIncomingPairs(targetId), resources.GetIncomingPairs(targetId));

    public static CombatDetailEventSet GetDetailEventSet(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId) =>
        adapter.CreateDetailEvents(snapshot, combatantId);

    private static DirectedPairSnapshot ToSnapshot(
        int sourceId,
        int targetId,
        CombatPairRecord? combat,
        CombatMechanicPairRecord? mechanic,
        CombatResourcePairRecord? resource) => new()
    {
        Key = new DirectedPairKey(sourceId, targetId),
        TotalDamage = combat?.TotalDamage ?? 0,
        TotalHealing = combat?.TotalHealing ?? 0,
        TotalShield = combat?.TotalShield ?? 0,
        TotalShieldAbsorbed = combat?.TotalShieldAbsorbed ?? 0,
        ShieldCount = combat?.ShieldCount ?? 0,
        ShieldAbsorbedCount = combat?.ShieldAbsorbedCount ?? 0,
        HitCount = mechanic?.HitCount ?? 0,
        AttemptCount = mechanic?.AttemptCount ?? 0,
        EvadeCount = mechanic?.EvadeCount ?? 0,
        InvincibleCount = mechanic?.InvincibleCount ?? 0,
        MultiHitCount = mechanic?.MultiHitCount ?? 0,
        MultiHitSubCount = mechanic?.MultiHitSubCount ?? 0,
        LastSkillCode = ResolveLastSkillCode(combat, mechanic, resource),
        FirstObserved = ResolveFirstObserved(combat?.FirstObserved, mechanic?.FirstObserved, resource?.FirstObserved),
        LastObserved = Math.Max(combat?.LastObserved ?? 0, Math.Max(mechanic?.LastObserved ?? 0, resource?.LastObserved ?? 0)),
        Revision = Math.Max(combat?.Revision ?? 0, Math.Max(mechanic?.Revision ?? 0, resource?.Revision ?? 0))
    };

    private static CombatantSummary ToSummary(
        int combatantId,
        CombatantRecord? combat,
        CombatantMechanicRecord? mechanic,
        ResourceCombatantObservation resource) => new()
    {
        CombatantId = combatantId,
        OutgoingDamage = combat?.OutgoingDamage ?? 0,
        OutgoingHits = mechanic?.OutgoingHits ?? 0,
        OutgoingAttempts = mechanic?.OutgoingAttempts ?? 0,
        OutgoingEvades = mechanic?.OutgoingEvades ?? 0,
        OutgoingInvincibles = mechanic?.OutgoingInvincibles ?? 0,
        OutgoingMultiHits = mechanic?.OutgoingMultiHits ?? 0,
        IncomingDamage = combat?.IncomingDamage ?? 0,
        IncomingHits = mechanic?.IncomingHits ?? 0,
        IncomingAttempts = mechanic?.IncomingAttempts ?? 0,
        IncomingEvades = mechanic?.IncomingEvades ?? 0,
        IncomingInvincibles = mechanic?.IncomingInvincibles ?? 0,
        IncomingMultiHits = mechanic?.IncomingMultiHits ?? 0,
        OutgoingHealing = combat?.OutgoingHealing ?? 0,
        IncomingHealing = combat?.IncomingHealing ?? 0,
        OutgoingShield = combat?.OutgoingShield ?? 0,
        IncomingShield = combat?.IncomingShield ?? 0,
        OutgoingShieldAbsorbed = combat?.OutgoingShieldAbsorbed ?? 0,
        IncomingShieldAbsorbed = combat?.IncomingShieldAbsorbed ?? 0,
        OutgoingShieldCount = combat?.OutgoingShieldCount ?? 0,
        IncomingShieldCount = combat?.IncomingShieldCount ?? 0,
        OutgoingShieldAbsorbedCount = combat?.OutgoingShieldAbsorbedCount ?? 0,
        IncomingShieldAbsorbedCount = combat?.IncomingShieldAbsorbedCount ?? 0,
        FirstObserved = ResolveFirstObserved(combat?.FirstObserved, mechanic?.FirstObserved, resource.HasObserved ? resource.FirstObserved : null),
        LastObserved = Math.Max(combat?.LastObserved ?? 0, Math.Max(mechanic?.LastObserved ?? 0, resource.LastObserved)),
        Revision = Math.Max(combat?.Revision ?? 0, Math.Max(mechanic?.Revision ?? 0, resource.Revision))
    };

    private static int ResolveLastSkillCode(CombatPairRecord? combat, CombatMechanicPairRecord? mechanic, CombatResourcePairRecord? resource)
    {
        var skillCode = 0;
        var lastObserved = long.MinValue;
        if (combat is not null)
        {
            skillCode = combat.LastSkillCode;
            lastObserved = combat.LastObserved;
        }
        if (mechanic is not null && mechanic.LastObserved > lastObserved)
        {
            skillCode = mechanic.LastSkillCode;
            lastObserved = mechanic.LastObserved;
        }
        if (resource is not null && resource.LastObserved > lastObserved)
            skillCode = resource.LastSkillCode;
        return skillCode;
    }

    private static long ResolveFirstObserved(long? combat, long? mechanic, long? resource)
    {
        var firstObserved = long.MaxValue;
        if (combat.HasValue)
            firstObserved = combat.Value;
        if (mechanic.HasValue)
            firstObserved = Math.Min(firstObserved, mechanic.Value);
        if (resource.HasValue)
            firstObserved = Math.Min(firstObserved, resource.Value);
        return firstObserved == long.MaxValue ? 0 : firstObserved;
    }

    private static DirectedPairKey[] ToPairKeys(
        IReadOnlyCollection<(int Source, int Target)> combatPairs,
        IReadOnlyCollection<(int Source, int Target)> mechanicPairs,
        IReadOnlyCollection<(int Source, int Target)> resourcePairs)
    {
        if (combatPairs.Count == 0 && mechanicPairs.Count == 0 && resourcePairs.Count == 0)
            return [];

        var union = new HashSet<(int Source, int Target)>(combatPairs);
        union.UnionWith(mechanicPairs);
        union.UnionWith(resourcePairs);
        var result = new DirectedPairKey[union.Count];
        var index = 0;
        foreach (var (sourceId, targetId) in union)
            result[index++] = new DirectedPairKey(sourceId, targetId);

        Array.Sort(result, static (left, right) =>
        {
            var cmp = left.SourceId.CompareTo(right.SourceId);
            return cmp != 0 ? cmp : left.TargetId.CompareTo(right.TargetId);
        });
        return result;
    }

    private static Dictionary<int, ResourceCombatantObservation> BuildResourceCombatants(ResourceStore resources)
    {
        var combatants = new Dictionary<int, ResourceCombatantObservation>(resources.Pairs.Count * 2);
        foreach (var pair in resources.Pairs.Values)
        {
            ApplyResourceCombatant(combatants, pair.SourceId, pair);
            if (pair.TargetId != pair.SourceId)
                ApplyResourceCombatant(combatants, pair.TargetId, pair);
        }
        return combatants;
    }

    private static void ApplyResourceCombatant(Dictionary<int, ResourceCombatantObservation> combatants, int combatantId, CombatResourcePairRecord pair)
    {
        if (combatantId <= 0)
            return;

        combatants.TryGetValue(combatantId, out var observation);
        observation.Apply(pair);
        combatants[combatantId] = observation;
    }

    private static bool TryGetResourceCombatant(ResourceStore resources, int combatantId, out ResourceCombatantObservation observation)
    {
        observation = default;
        if (combatantId <= 0)
            return false;

        foreach (var pairKey in resources.GetOutgoingPairs(combatantId))
        {
            if (resources.TryGetPair(pairKey.Source, pairKey.Target, out var pair))
                observation.Apply(pair!);
        }
        foreach (var pairKey in resources.GetIncomingPairs(combatantId))
        {
            if (pairKey.Source == pairKey.Target)
                continue;
            if (resources.TryGetPair(pairKey.Source, pairKey.Target, out var pair))
                observation.Apply(pair!);
        }
        return observation.HasObserved;
    }

    private struct ResourceCombatantObservation
    {
        public long FirstObserved { get; private set; }
        public long LastObserved { get; private set; }
        public long Revision { get; private set; }
        public bool HasObserved { get; private set; }

        public void Apply(CombatResourcePairRecord pair)
        {
            if (HasObserved)
                FirstObserved = Math.Min(FirstObserved, pair.FirstObserved);
            else
                FirstObserved = pair.FirstObserved;
            LastObserved = Math.Max(LastObserved, pair.LastObserved);
            Revision = Math.Max(Revision, pair.Revision);
            HasObserved = true;
        }
    }
}
