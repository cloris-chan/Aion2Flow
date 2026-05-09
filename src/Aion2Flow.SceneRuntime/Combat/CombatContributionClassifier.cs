using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public readonly record struct CombatContribution
{
    public bool CountsAsDamage { get; init; }
    public bool CountsAsHealing { get; init; }
    public bool CountsAsShieldGrant { get; init; }
    public bool CountsAsShieldAbsorbed { get; init; }
    public long DamageAmount { get; init; }
    public long HealingAmount { get; init; }
    public long ShieldGrantAmount { get; init; }
    public long ShieldAbsorbedAmount { get; init; }
    public int ShieldGrantCount { get; init; }
    public int ShieldAbsorbedCount { get; init; }
    public int HitCount { get; init; }
    public int AttemptCount { get; init; }
    public int EvadeCount { get; init; }
    public int InvincibleCount { get; init; }
    public int MultiHitCount { get; init; }
}

public static class CombatContributionClassifier
{
    public static CombatContribution Evaluate(in CombatObservation observation)
        => EvaluateCore(
            observation.EventKind,
            observation.ValueKind,
            observation.EffectTag,
            observation.Modifiers,
            observation.Damage,
            observation.HitCount,
            observation.AttemptCount);

    public static CombatContribution Evaluate(ParsedCombatPacket packet)
        => EvaluateCore(
            packet.EventKind,
            packet.ValueKind,
            packet.EffectTag,
            packet.Modifiers,
            packet.Damage,
            packet.HitContribution,
            packet.AttemptContribution);

    private static CombatContribution EvaluateCore(
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        PacketEffectTag effectTag,
        DamageModifiers modifiers,
        long amount,
        int rawHitCount,
        int rawAttemptCount)
    {
        var countsAsDamage = CountsAsDamage(eventKind, valueKind, modifiers, amount, rawAttemptCount);
        var countsAsHealing = CountsAsHealing(eventKind, valueKind, amount);
        var countsAsShieldGrant = CountsAsShieldGrant(valueKind, effectTag, amount);
        var countsAsShieldAbsorbed = CountsAsShieldAbsorbed(valueKind, effectTag, amount);
        var hitCount = countsAsDamage ? Math.Max(0, rawHitCount) : 0;
        var attemptCount = countsAsDamage ? Math.Max(hitCount, Math.Max(0, rawAttemptCount)) : 0;
        var evadeCount = countsAsDamage && (modifiers & DamageModifiers.Evade) != 0 ? attemptCount : 0;
        var invincibleCount = countsAsDamage && (modifiers & DamageModifiers.Invincible) != 0 ? attemptCount : 0;
        var multiHitCount = countsAsDamage && (modifiers & DamageModifiers.MultiHit) != 0 ? 1 : 0;

        return new CombatContribution
        {
            CountsAsDamage = countsAsDamage,
            CountsAsHealing = countsAsHealing,
            CountsAsShieldGrant = countsAsShieldGrant,
            CountsAsShieldAbsorbed = countsAsShieldAbsorbed,
            DamageAmount = countsAsDamage ? amount : 0,
            HealingAmount = countsAsHealing ? amount : 0,
            ShieldGrantAmount = countsAsShieldGrant ? amount : 0,
            ShieldAbsorbedAmount = countsAsShieldAbsorbed ? amount : 0,
            ShieldGrantCount = countsAsShieldGrant ? 1 : 0,
            ShieldAbsorbedCount = countsAsShieldAbsorbed ? 1 : 0,
            HitCount = hitCount,
            AttemptCount = attemptCount,
            EvadeCount = evadeCount,
            InvincibleCount = invincibleCount,
            MultiHitCount = multiHitCount
        };
    }

    private static bool CountsAsDamage(CombatEventKind eventKind, CombatValueKind valueKind, DamageModifiers modifiers, long amount, int attemptCount)
    {
        if (eventKind == CombatEventKind.Damage &&
            valueKind is CombatValueKind.Damage or CombatValueKind.PeriodicDamage or CombatValueKind.DrainDamage or CombatValueKind.Unknown &&
            (attemptCount > 0 || (modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0))
        {
            return true;
        }

        return valueKind switch
        {
            CombatValueKind.Damage => amount > 0,
            CombatValueKind.PeriodicDamage => amount > 0,
            CombatValueKind.DrainDamage => amount > 0,
            CombatValueKind.Unknown => eventKind == CombatEventKind.Damage && amount > 0,
            _ => false
        };
    }

    private static bool CountsAsHealing(CombatEventKind eventKind, CombatValueKind valueKind, long amount) =>
        valueKind switch
        {
            CombatValueKind.Healing => amount > 0,
            CombatValueKind.PeriodicHealing => amount > 0,
            CombatValueKind.DrainHealing => amount > 0,
            CombatValueKind.Shield => false,
            _ => eventKind == CombatEventKind.Healing && amount > 0
        };

    private static bool CountsAsShieldGrant(CombatValueKind valueKind, PacketEffectTag effectTag, long amount) =>
        valueKind == CombatValueKind.Shield && effectTag != PacketEffectTag.ShieldAbsorbed && amount > 0;

    private static bool CountsAsShieldAbsorbed(CombatValueKind valueKind, PacketEffectTag effectTag, long amount) =>
        valueKind == CombatValueKind.Shield && effectTag == PacketEffectTag.ShieldAbsorbed && amount > 0;
}
