using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatMetricsAggregationTests
{
    [Fact]
    public void SkillMetrics_Tracks_Drain_Damage_Separately_While_Keeping_Damage_Total()
    {
        var observation = DamageObservation(16046601, 1234);
        var contribution = Contribution(
            CombatMetricKind.Damage,
            CombatDeliveryKind.Drain,
            1234);
        var mechanic = Mechanic(hitCount: 1, attemptCount: 1);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessContribution(in contribution);
        metrics.ProcessMechanic(in mechanic);

        Assert.Equal(1234, metrics.DamageAmount);
        Assert.Equal(1234, metrics.DrainDamageAmount);
        Assert.Equal(1, metrics.DrainDamageTimes);
        Assert.Equal(1, metrics.Times);
    }

    [Fact]
    public void SkillMetrics_Tracks_Periodic_Healing_Separately_While_Keeping_Healing_Total()
    {
        var observation = new CombatWireObservation { SkillCode = 18160030, Damage = 612 };
        var contribution = Contribution(CombatMetricKind.Healing, CombatDeliveryKind.Periodic, 612);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessContribution(in contribution);

        Assert.Equal(612, metrics.HealingAmount);
        Assert.Equal(612, metrics.PeriodicHealingAmount);
        Assert.Equal(1, metrics.HealingTimes);
        Assert.Equal(1, metrics.PeriodicHealingTimes);
    }

    [Fact]
    public void CombatStore_Tracks_Shield_Without_Counting_It_As_Damage_Or_Healing()
    {
        var store = new CombatStore();
        var observation = new CombatWireObservation { SkillCode = 22120011, Damage = 1025 };
        var contribution = Contribution(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Direct, 1025);

        store.ApplyCombat(100, 200, in observation, in contribution, 1_000);

        Assert.True(store.TryGetCombatant(100, out var combatant));
        Assert.Equal(0, combatant!.OutgoingDamage);
        Assert.Equal(0, combatant.OutgoingHealing);
        Assert.Equal(1025, combatant.OutgoingShield);
        Assert.Equal(1, combatant.OutgoingShieldCount);
    }

    [Fact]
    public void SkillMetrics_Tracks_Shield_Amount_And_Times()
    {
        var observation = new CombatWireObservation { SkillCode = 22120011, Damage = 1025 };
        var contribution = Contribution(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Direct, 1025);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessContribution(in contribution);

        Assert.Equal(1025, metrics.ShieldAmount);
        Assert.Equal(1, metrics.ShieldTimes);
        Assert.Equal(0, metrics.HealingTimes);
        Assert.Equal(0, metrics.Times);
    }

    [Fact]
    public void CombatStore_Tracks_Drain_Healing_Separately_While_Keeping_Healing_Total()
    {
        var store = new CombatStore();
        var observation = new CombatWireObservation { SkillCode = 16046601, Damage = 567 };
        var contribution = Contribution(CombatMetricKind.Healing, CombatDeliveryKind.Drain, 567);

        store.ApplyCombat(100, 100, in observation, in contribution, 1_000);

        Assert.True(store.TryGetPair(100, 100, out var pair));
        Assert.Equal(567, pair!.TotalHealing);
        Assert.Equal(567, pair.TotalDrainHealing);
        Assert.Equal(0, pair.TotalDamage);
    }

    [Fact]
    public void SkillMetrics_Tracks_Direct_And_Periodic_Healing_Without_Primary_Category()
    {
        var observation = new CombatWireObservation { SkillCode = 18120150 };
        var periodic = Contribution(CombatMetricKind.Healing, CombatDeliveryKind.Periodic, 1200);
        var direct = Contribution(CombatMetricKind.Healing, CombatDeliveryKind.Direct, 4200);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessContribution(in periodic);
        metrics.ProcessContribution(in direct);

        Assert.Equal(5400, metrics.HealingAmount);
        Assert.Equal(2, metrics.HealingTimes);
        Assert.Equal(1200, metrics.PeriodicHealingAmount);
        Assert.Equal(1, metrics.PeriodicHealingTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Evade_Attempts_Without_Inflating_Damage_Or_Hits()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 1100020,
            Modifiers = DamageModifiers.Evade,
            AttemptCount = 1
        };
        var mechanic = Mechanic(
            modifiers: DamageModifiers.Evade,
            attemptCount: 1);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessMechanic(in mechanic);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(1, metrics.EvadeTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Invincible_Attempts_Separately_From_Evade()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 12000100,
            Modifiers = DamageModifiers.Invincible,
            AttemptCount = 1
        };
        var mechanic = Mechanic(
            modifiers: DamageModifiers.Invincible,
            attemptCount: 1);
        var metrics = CreateMetrics(in observation);

        metrics.ProcessMechanic(in mechanic);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(0, metrics.EvadeTimes);
        Assert.Equal(1, metrics.InvincibleTimes);
    }

    private static CombatWireObservation DamageObservation(int skillCode, long amount) => new()
    {
        SkillCode = skillCode,
        Damage = amount,
        HitCount = 1,
        AttemptCount = 1
    };

    private static SkillMetrics CreateMetrics(in CombatWireObservation observation) =>
        new(CombatEventKey.FromObservation(in observation));

    private static CombatContribution Contribution(
        CombatMetricKind metric,
        CombatDeliveryKind delivery,
        long amount) => new(metric, delivery, amount, default);

    private static CombatMechanicOccurrence Mechanic(
        DamageModifiers modifiers = default,
        int hitCount = 0,
        int attemptCount = 0,
        int multiHitSubCount = 0)
    {
        var normalizedHits = Math.Max(0, hitCount);
        var normalizedAttempts = Math.Max(normalizedHits, Math.Max(0, attemptCount));
        return new CombatMechanicOccurrence(
            modifiers,
            normalizedHits,
            normalizedAttempts,
            (modifiers & DamageModifiers.Evade) != 0 ? normalizedAttempts : 0,
            (modifiers & DamageModifiers.Invincible) != 0 ? normalizedAttempts : 0,
            (modifiers & DamageModifiers.MultiHit) != 0 ? 1 : 0,
            Math.Max(0, multiHitSubCount),
            default);
    }
}
