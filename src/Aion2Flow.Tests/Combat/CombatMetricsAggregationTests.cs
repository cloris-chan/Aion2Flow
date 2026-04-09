using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsAggregationTests
{
    [Fact]
    public void SkillMetrics_Tracks_Drain_Damage_Separately_While_Keeping_Damage_Total()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 16046601,
            Damage = 1234,
            ValueKind = CombatValueKind.DrainDamage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(1234, metrics.DamageAmount);
        Assert.Equal(1234, metrics.DrainDamageAmount);
        Assert.Equal(1, metrics.DrainDamageTimes);
        Assert.Equal(1, metrics.Times);
    }

    [Fact]
    public void SkillMetrics_Tracks_Periodic_Healing_Separately_While_Keeping_Healing_Total()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 18160030,
            Damage = 612,
            ValueKind = CombatValueKind.PeriodicHealing,
            EventKind = CombatEventKind.Healing
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(612, metrics.HealingAmount);
        Assert.Equal(612, metrics.PeriodicHealingAmount);
        Assert.Equal(1, metrics.HealingTimes);
        Assert.Equal(1, metrics.PeriodicHealingTimes);
    }

    [Fact]
    public void CombatantMetrics_Tracks_Shield_Without_Counting_It_As_Damage_Or_Healing()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 22120011,
            Damage = 1025,
            ValueKind = CombatValueKind.Shield,
            EventKind = CombatEventKind.Support
        };

        var metrics = new CombatantMetrics("test");
        var counted = metrics.ProcessCombatEvent(packet);

        Assert.False(counted);
        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.HealingAmount);
        Assert.Equal(1025, metrics.ShieldAmount);
        Assert.Equal(1, metrics.ShieldTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Shield_Amount_And_Times()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 22120011,
            Damage = 1025,
            ValueKind = CombatValueKind.Shield,
            EventKind = CombatEventKind.Support
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(1025, metrics.ShieldAmount);
        Assert.Equal(1, metrics.ShieldTimes);
        Assert.Equal(1, metrics.SupportTimes);
    }

    [Fact]
    public void CombatantMetrics_Tracks_Drain_Healing_Separately_While_Keeping_Healing_Total()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 16046601,
            Damage = 567,
            ValueKind = CombatValueKind.DrainHealing,
            EventKind = CombatEventKind.Healing
        };

        var metrics = new CombatantMetrics("test");
        var counted = metrics.ProcessCombatEvent(packet);

        Assert.False(counted);
        Assert.Equal(567, metrics.HealingAmount);
        Assert.Equal(567, metrics.DrainHealingAmount);
        Assert.Equal(0, metrics.DamageAmount);
    }

    [Fact]
    public void SkillMetrics_PrimaryValueKind_Follows_Dominant_Observed_Healing_Flow()
    {
        var hotPacket = new ParsedCombatPacket
        {
            SkillCode = 18120150,
            Damage = 1200,
            ValueKind = CombatValueKind.PeriodicHealing,
            EventKind = CombatEventKind.Healing
        };

        var directPacket = new ParsedCombatPacket
        {
            SkillCode = 18120150,
            Damage = 4200,
            ValueKind = CombatValueKind.Healing,
            EventKind = CombatEventKind.Healing
        };

        var metrics = new SkillMetrics(hotPacket);
        metrics.ProcessEvent(hotPacket);
        Assert.Equal(CombatValueKind.PeriodicHealing, metrics.PrimaryValueKind);

        metrics.ProcessEvent(directPacket);
        Assert.Equal(CombatValueKind.Healing, metrics.PrimaryValueKind);
    }

    [Fact]
    public void SkillMetrics_PrimaryValueKind_Folds_DrainDamage_Into_Damage()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 12240010,
            Damage = 1800,
            ValueKind = CombatValueKind.DrainDamage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(CombatValueKind.Damage, metrics.PrimaryValueKind);
    }

    [Fact]
    public void SkillMetrics_Tracks_Evade_Attempts_Without_Inflating_Damage_Or_Hits()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 1100020,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Evade,
            ValueKind = CombatValueKind.Damage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(1, metrics.EvadeTimes);
    }

    [Fact]
    public void SkillMetrics_Tracks_Invincible_Attempts_Separately_From_Evade()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 12000100,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Invincible,
            ValueKind = CombatValueKind.Damage,
            EventKind = CombatEventKind.Damage
        };

        var metrics = new SkillMetrics(packet);
        metrics.ProcessEvent(packet);

        Assert.Equal(0, metrics.DamageAmount);
        Assert.Equal(0, metrics.Times);
        Assert.Equal(1, metrics.AttemptTimes);
        Assert.Equal(0, metrics.EvadeTimes);
        Assert.Equal(1, metrics.InvincibleTimes);
    }
}
