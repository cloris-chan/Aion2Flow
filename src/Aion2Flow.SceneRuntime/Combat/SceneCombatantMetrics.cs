using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public readonly record struct SceneCombatantMetrics
{
    internal SceneCombatantMetrics(CharacterClass? characterClass, bool isVisiblePlayerCombatant, double damagePerSecond, double healingPerSecond, long damageAmount, long healingAmount, long periodicHealingAmount, long drainDamageAmount, long drainHealingAmount, long regenerationHealingAmount, long shieldAmount, int shieldTimes, long shieldAbsorbedAmount, int shieldAbsorbedTimes, double damageContribution)
    {
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
}

internal struct SceneCombatantMetricsAccumulator
{
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

    public void Reset()
    {
        this = default;
    }

    public void ProcessCombatObservation(in CombatObservation observation)
    {
        var contribution = CombatContributionClassifier.Evaluate(in observation);
        ApplyContribution(contribution, observation.ValueKind, observation.EffectTag);
    }

    public void ApplyCombatTotals(long damageAmount, long healingAmount, long periodicHealingAmount, long drainDamageAmount, long drainHealingAmount, long regenerationHealingAmount, long shieldAmount, int shieldTimes, long shieldAbsorbedAmount, int shieldAbsorbedTimes)
    {
        DamageAmount += damageAmount;
        HealingAmount += healingAmount;
        PeriodicHealingAmount += periodicHealingAmount;
        DrainDamageAmount += drainDamageAmount;
        DrainHealingAmount += drainHealingAmount;
        RegenerationHealingAmount += regenerationHealingAmount;
        ShieldAmount += shieldAmount;
        ShieldTimes += shieldTimes;
        ShieldAbsorbedAmount += shieldAbsorbedAmount;
        ShieldAbsorbedTimes += shieldAbsorbedTimes;
    }

    private void ApplyContribution(
        in CombatContribution contribution,
        CombatValueKind valueKind,
        PacketEffectTag effectTag)
        => ApplyValues(contribution.DamageAmount, contribution.HealingAmount, contribution.ShieldGrantAmount, contribution.ShieldGrantCount, contribution.ShieldAbsorbedAmount, contribution.ShieldAbsorbedCount, valueKind, effectTag);

    private void ApplyValues(long damageAmount, long healingAmount, long shieldGrantAmount, int shieldGrantCount, long shieldAbsorbedAmount, int shieldAbsorbedCount, CombatValueKind valueKind, PacketEffectTag effectTag)
    {
        DamageAmount += damageAmount;
        HealingAmount += healingAmount;
        ShieldAmount += shieldGrantAmount;
        ShieldTimes += shieldGrantCount;
        ShieldAbsorbedAmount += shieldAbsorbedAmount;
        ShieldAbsorbedTimes += shieldAbsorbedCount;

        if (valueKind == CombatValueKind.PeriodicHealing)
        {
            PeriodicHealingAmount += healingAmount;
        }
        else if (valueKind == CombatValueKind.DrainHealing)
        {
            DrainHealingAmount += healingAmount;
        }
        else if (valueKind == CombatValueKind.DrainDamage)
        {
            DrainDamageAmount += damageAmount;
        }
        else if (effectTag == PacketEffectTag.RegenerationHealing)
        {
            RegenerationHealingAmount += healingAmount;
        }
    }

    public readonly SceneCombatantMetrics ToSnapshot()
    {
        return new SceneCombatantMetrics(CharacterClass, IsVisiblePlayerCombatant, DamagePerSecond, HealingPerSecond, DamageAmount, HealingAmount, PeriodicHealingAmount, DrainDamageAmount, DrainHealingAmount, RegenerationHealingAmount, ShieldAmount, ShieldTimes, ShieldAbsorbedAmount, ShieldAbsorbedTimes, DamageContribution);
    }
}
