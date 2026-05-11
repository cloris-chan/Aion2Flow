using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public readonly record struct SceneCombatantMetrics
{
    public SceneCombatantMetrics(string nickname)
        : this()
    {
        Nickname = nickname;
    }

    internal SceneCombatantMetrics(
        string nickname,
        CharacterClass? characterClass,
        bool isVisiblePlayerCombatant,
        double damagePerSecond,
        double healingPerSecond,
        long damageAmount,
        long healingAmount,
        long periodicHealingAmount,
        long drainDamageAmount,
        long drainHealingAmount,
        long regenerationHealingAmount,
        long shieldAmount,
        int shieldTimes,
        long shieldAbsorbedAmount,
        int shieldAbsorbedTimes,
        double damageContribution)
    {
        Nickname = nickname;
        CharacterClass = characterClass;
        IsVisiblePlayerCombatant = isVisiblePlayerCombatant;
        DamagePerSecond = damagePerSecond;
        HealingPerSecond = healingPerSecond;
        DamageAmount = damageAmount;
        HealingAmount = healingAmount;
        PeriodicHealingAmount = periodicHealingAmount;
        DrainDamageAmount = drainDamageAmount;
        DrainHealingAmount = drainHealingAmount;
        RegenerationHealingAmount = regenerationHealingAmount;
        ShieldAmount = shieldAmount;
        ShieldTimes = shieldTimes;
        ShieldAbsorbedAmount = shieldAbsorbedAmount;
        ShieldAbsorbedTimes = shieldAbsorbedTimes;
        DamageContribution = damageContribution;
    }

    public CharacterClass? CharacterClass { get; init; }

    public bool IsVisiblePlayerCombatant { get; init; }

    public double DamagePerSecond { get; init; }

    public double HealingPerSecond { get; init; }

    public long DamageAmount { get; init; }

    public long HealingAmount { get; init; }

    public long PeriodicHealingAmount { get; init; }

    public long DrainDamageAmount { get; init; }

    public long DrainHealingAmount { get; init; }

    public long RegenerationHealingAmount { get; init; }

    public long ShieldAmount { get; init; }

    public int ShieldTimes { get; init; }

    public long ShieldAbsorbedAmount { get; init; }

    public int ShieldAbsorbedTimes { get; init; }

    public double DamageContribution { get; init; }

    public string Nickname { get; init; } = string.Empty;
}

internal struct SceneCombatantMetricsAccumulator(string nickname)
{
    public string Nickname = nickname;
    public CharacterClass? CharacterClass;
    public bool IsVisiblePlayerCombatant;
    public double DamagePerSecond;
    public double HealingPerSecond;
    public long DamageAmount;
    public long HealingAmount;
    public long PeriodicHealingAmount;
    public long DrainDamageAmount;
    public long DrainHealingAmount;
    public long RegenerationHealingAmount;
    public long ShieldAmount;
    public int ShieldTimes;
    public long ShieldAbsorbedAmount;
    public int ShieldAbsorbedTimes;
    public double DamageContribution;

    public void Reset(string nickname)
    {
        this = new SceneCombatantMetricsAccumulator(nickname);
    }

    public void ProcessCombatObservation(in CombatObservation observation)
    {
        var contribution = CombatContributionClassifier.Evaluate(in observation);
        ApplyContribution(
            contribution,
            observation.ValueKind,
            observation.EffectTag,
            observation.DrainHealAmount);
    }

    private void ApplyContribution(
        in CombatContribution contribution,
        CombatValueKind valueKind,
        PacketEffectTag effectTag,
        int drainHealAmount)
    {
        DamageAmount += contribution.DamageAmount;
        HealingAmount += contribution.HealingAmount;
        ShieldAmount += contribution.ShieldGrantAmount;
        ShieldTimes += contribution.ShieldGrantCount;
        ShieldAbsorbedAmount += contribution.ShieldAbsorbedAmount;
        ShieldAbsorbedTimes += contribution.ShieldAbsorbedCount;

        if (valueKind == CombatValueKind.PeriodicHealing)
        {
            PeriodicHealingAmount += contribution.HealingAmount;
        }
        else if (valueKind == CombatValueKind.DrainHealing)
        {
            DrainHealingAmount += contribution.HealingAmount;
        }
        else if (valueKind == CombatValueKind.DrainDamage)
        {
            DrainDamageAmount += contribution.DamageAmount;
            if (drainHealAmount > 0)
            {
                DrainHealingAmount += drainHealAmount;
                HealingAmount += drainHealAmount;
            }
        }
        else if (effectTag == PacketEffectTag.RegenerationHealing)
        {
            RegenerationHealingAmount += contribution.HealingAmount;
        }
    }

    public readonly SceneCombatantMetrics ToSnapshot()
    {
        return new SceneCombatantMetrics(
            Nickname,
            CharacterClass,
            IsVisiblePlayerCombatant,
            DamagePerSecond,
            HealingPerSecond,
            DamageAmount,
            HealingAmount,
            PeriodicHealingAmount,
            DrainDamageAmount,
            DrainHealingAmount,
            RegenerationHealingAmount,
            ShieldAmount,
            ShieldTimes,
            ShieldAbsorbedAmount,
            ShieldAbsorbedTimes,
            DamageContribution);
    }
}
