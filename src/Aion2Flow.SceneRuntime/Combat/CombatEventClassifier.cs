using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatEventClassifier
{
    public static CombatEventKind Classify(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return Classify(packet.SourceId, packet.TargetId, in observation).EventKind;
    }

    public static CombatValueKind ClassifyValueKind(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return Classify(packet.SourceId, packet.TargetId, in observation).ValueKind;
    }

    public static (CombatEventKind EventKind, CombatValueKind ValueKind) Classify(int sourceId, int targetId, in CombatObservation observation)
    {
        if (IsOutcomeOnlyAvoidance(in observation))
            return (CombatEventKind.Damage, CombatValueKind.Damage);

        if (IsDrainHealSynthesis(sourceId, targetId, in observation))
            return (CombatEventKind.Healing, CombatValueKind.DrainHealing);

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return ClassifyPeriodic(sourceId, targetId, in observation);

        return ClassifyDirect(sourceId, targetId, in observation);
    }

    public static bool CountsTowardsDamage(ParsedCombatPacket packet) => packet.EventKind == CombatEventKind.Damage;

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyDirect(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.ResourceKind == CombatResourceKind.Health)
            return (CombatEventKind.Healing, CombatValueKind.Healing);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (sourceId > 0 && targetId > 0 && sourceId == targetId)
            return (CombatEventKind.Support, CombatValueKind.Support);

        return (CombatEventKind.Damage, CombatValueKind.Damage);
    }

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyPeriodic(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.PeriodicRelation == PeriodicEffectRelation.Self)
        {
            if (CombatObservationTraits.IsPeriodicSelfMode(in observation, 10))
                return (CombatEventKind.Support, CombatValueKind.Support);

            if (observation.ResourceKind == CombatResourceKind.Mana)
                return (CombatEventKind.Support, CombatValueKind.Support);

            if (observation.ResourceKind == CombatResourceKind.Health ||
                CombatObservationTraits.IsPeriodicSelfMode(in observation, 11))
                return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);

            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (observation.PeriodicRelation != PeriodicEffectRelation.Target)
            return (CombatEventKind.Damage, CombatValueKind.Damage);

        if (CombatObservationTraits.IsPeriodicTargetMode(in observation, 8))
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (observation.ResourceKind == CombatResourceKind.Health)
        {
            return CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation) ? (CombatEventKind.Healing, CombatValueKind.Healing) : (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
        }

        if (CombatObservationTraits.IsTargetPeriodicSupportSeed(in observation))
            return (CombatEventKind.Support, CombatValueKind.Support);

        return CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation) ? (CombatEventKind.Damage, CombatValueKind.Damage) : (CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
    }

    private static bool IsOutcomeOnlyAvoidance(in CombatObservation observation)
    {
        if (observation.Damage > 0)
            return false;

        if ((observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
            return false;

        return Math.Max(observation.HitCount, observation.AttemptCount) > 0;
    }

    private static bool IsDrainHealSynthesis(int sourceId, int targetId, in CombatObservation observation) =>
        sourceId > 0 && sourceId == targetId && observation.Damage > 0 && observation.DrainHealAmount > 0;

}

public static class CombatObservationTraits
{
    public static bool IsTargetPeriodicSupportSeed(in CombatObservation observation) => IsPeriodicTargetMode(in observation, 9) || IsPeriodicTargetMode(in observation, 11);

    public static bool IsPeriodicSelfMode(in CombatObservation observation, int mode) => observation.PeriodicRelation == PeriodicEffectRelation.Self && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetMode(in CombatObservation observation, int mode) => observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetInitialEffect(in CombatObservation observation) => observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == 1;

    public static string FormatEffectLabel(in CombatObservation observation)
    {
        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return FormatPeriodicEffectLabel(observation.PeriodicRelation, observation.PeriodicMode);

        return observation.EffectTag == PacketEffectTag.None ? string.Empty : FormatEffectTagLabel(observation.EffectTag);
    }

    private static string FormatPeriodicEffectLabel(PeriodicEffectRelation relation, int mode)
    {
        if (relation == PeriodicEffectRelation.None)
            return string.Empty;

        if (relation == PeriodicEffectRelation.Self)
        {
            return mode switch
            {
                1 => "periodic-self-initial",
                3 => "periodic-self-tick",
                _ => $"periodic-self-mode-{mode}"
            };
        }

        return mode switch
        {
            1 => "periodic-target-initial",
            2 => "periodic-target-tick",
            3 => "periodic-target-tick",
            _ => $"periodic-target-mode-{mode}"
        };
    }

    private static string FormatEffectTagLabel(PacketEffectTag effectTag) =>
        effectTag switch
        {
            PacketEffectTag.CompactEvade => "compact-evade",
            PacketEffectTag.PeriodicLinkInvincible => "periodic-link-invincible",
            PacketEffectTag.ActiveSkillInvincible => "active-skill-invincible",
            _ => string.Empty
        };
}
