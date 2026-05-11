using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatMetricsAggregationTests
{
    [Fact]
    public void SkillMetrics_Tracks_Drain_Damage_Separately_While_Keeping_Damage_Total()
    {
        var observation = new CombatObservation
        {
            SkillCode = 16046601,
            Damage = 1234,
            HitCount = 1,
            AttemptCount = 1,
            ValueKind = CombatValueKind.DrainDamage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(1234, metrics.DamageAmount);
        Assert.Equal(1234, metrics.DrainDamageAmount);
        Assert.Equal(1, metrics.DrainDamageTimes);
        Assert.Equal(1, metrics.Times);
    }

    [Fact]
    public void SkillMetrics_Tracks_Periodic_Healing_Separately_While_Keeping_Healing_Total()
    {
        var observation = new CombatObservation
        {
            SkillCode = 18160030,
            Damage = 612,
            ValueKind = CombatValueKind.PeriodicHealing,
            EventKind = CombatEventKind.Healing
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(612, metrics.HealingAmount);
        Assert.Equal(612, metrics.PeriodicHealingAmount);
        Assert.Equal(1, metrics.HealingTimes);
        Assert.Equal(1, metrics.PeriodicHealingTimes);
    }

    [Fact]
    public void CombatantMetrics_Tracks_Shield_Without_Counting_It_As_Damage_Or_Healing()
    {
        var observation = new CombatObservation
        {
            SkillCode = 22120011,
            Damage = 1025,
            ValueKind = CombatValueKind.Shield,
            EventKind = CombatEventKind.Support
        };

        var accumulator = new SceneCombatantMetricsAccumulator("test");
        accumulator.ProcessCombatObservation(in observation);
        var metrics = accumulator.ToSnapshot();

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.HealingAmount);
        Assert.Equal(1025, metrics.ShieldAmount);
        Assert.Equal(1, metrics.ShieldTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Shield_Amount_And_Times()
    {
        var observation = new CombatObservation
        {
            SkillCode = 22120011,
            Damage = 1025,
            ValueKind = CombatValueKind.Shield,
            EventKind = CombatEventKind.Support
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(1025, metrics.ShieldAmount);
        Assert.Equal(1, metrics.ShieldTimes);
        Assert.Equal(1, metrics.SupportTimes);
    }

    [Fact]
    public void CombatantMetrics_Tracks_Drain_Healing_Separately_While_Keeping_Healing_Total()
    {
        var observation = new CombatObservation
        {
            SkillCode = 16046601,
            Damage = 567,
            ValueKind = CombatValueKind.DrainHealing,
            EventKind = CombatEventKind.Healing
        };

        var accumulator = new SceneCombatantMetricsAccumulator("test");
        accumulator.ProcessCombatObservation(in observation);
        var metrics = accumulator.ToSnapshot();

        Assert.Equal(567, metrics.HealingAmount);
        Assert.Equal(567, metrics.DrainHealingAmount);
        Assert.Equal(0, metrics.DamageAmount);
    }

    [Fact]
    public void SkillMetrics_PrimaryValueKind_Follows_Dominant_Observed_Healing_Flow()
    {
        var hotObservation = new CombatObservation
        {
            SkillCode = 18120150,
            Damage = 1200,
            ValueKind = CombatValueKind.PeriodicHealing,
            EventKind = CombatEventKind.Healing
        };

        var directObservation = new CombatObservation
        {
            SkillCode = 18120150,
            Damage = 4200,
            ValueKind = CombatValueKind.Healing,
            EventKind = CombatEventKind.Healing
        };

        var metrics = new SkillMetrics(in hotObservation);
        metrics.ProcessObservation(in hotObservation);
        Assert.Equal(CombatValueKind.PeriodicHealing, metrics.PrimaryValueKind);

        metrics.ProcessObservation(in directObservation);
        Assert.Equal(CombatValueKind.Healing, metrics.PrimaryValueKind);
    }

    [Fact]
    public void SkillMetrics_PrimaryValueKind_Folds_DrainDamage_Into_Damage()
    {
        var observation = new CombatObservation
        {
            SkillCode = 12240010,
            Damage = 1800,
            ValueKind = CombatValueKind.DrainDamage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(CombatValueKind.Damage, metrics.PrimaryValueKind);
    }

    [Fact]
    public void SkillMetrics_Tracks_Evade_Attempts_Without_Inflating_Damage_Or_Hits()
    {
        var observation = new CombatObservation
        {
            SkillCode = 1100020,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Evade,
            ValueKind = CombatValueKind.Damage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(1, metrics.EvadeTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Invincible_Attempts_Separately_From_Evade()
    {
        var observation = new CombatObservation
        {
            SkillCode = 12000100,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Invincible,
            ValueKind = CombatValueKind.Damage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(in observation);
        metrics.ProcessObservation(in observation);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(0, metrics.EvadeTimes);
        Assert.Equal(1, metrics.InvincibleTimes);
    }
}
