using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
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

    public static string DisplaySkillNameFor(int skillCode)
    {
        return TryGetDisplaySkillName(skillCode, out var skillName)
            ? skillName
            : string.Empty;
    }

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyDirect(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.ResourceKind == CombatResourceKind.Health)
            return (CombatEventKind.Healing, CombatValueKind.Healing);

        if (CombatObservationTraits.IsRestoreHp(in observation))
            return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);

        if (CombatObservationTraits.IsDirectHpRestoreShape(sourceId, targetId, in observation))
            return (CombatEventKind.Healing, CombatValueKind.Healing);

        if (CombatObservationTraits.IsDirectSupportValueShape(sourceId, targetId, in observation))
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (CombatObservationTraits.IsKnownDirectPeriodicHealing(sourceId, targetId, in observation))
            return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);

        if (CombatObservationTraits.IsKnownDirectHealing(in observation))
            return (CombatEventKind.Healing, CombatValueKind.Healing);

        if (CombatObservationTraits.IsKnownShield(in observation))
            return (CombatEventKind.Support, CombatValueKind.Shield);

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
                CombatObservationTraits.IsPeriodicSelfMode(in observation, 11) ||
                CombatObservationTraits.IsRestoreHp(in observation) ||
                CombatObservationTraits.IsKnownPeriodicHealing(sourceId, targetId, in observation))
                return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);

            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (observation.PeriodicRelation != PeriodicEffectRelation.Target)
            return (CombatEventKind.Damage, CombatValueKind.Damage);

        if (CombatObservationTraits.IsPeriodicTargetMode(in observation, 8))
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return (CombatEventKind.Support, CombatValueKind.Support);

        if (observation.ResourceKind == CombatResourceKind.Health ||
            CombatObservationTraits.IsKnownPeriodicHealing(sourceId, targetId, in observation))
        {
            return CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation)
                ? (CombatEventKind.Healing, CombatValueKind.Healing)
                : (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
        }

        if (CombatObservationTraits.IsTargetPeriodicSupportSeed(in observation))
            return (CombatEventKind.Support, CombatValueKind.Support);

        return CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation)
            ? (CombatEventKind.Damage, CombatValueKind.Damage)
            : (CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
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

    private static bool TryGetDisplaySkillName(int skillCode, out string skillName)
    {
        skillName = string.Empty;
        if (!TryGetDisplaySkill(skillCode, out var skill))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(skill.Name))
        {
            return false;
        }

        skillName = skill.Name;
        return true;
    }

    private static bool TryGetDisplaySkill(int skillCode, out Skill skill)
    {
        if (CombatResourceRegistry.SkillDisplayMap.TryGetValue(skillCode, out skill))
        {
            return true;
        }

        return TryGetSkill(skillCode, out skill);
    }

    private static bool TryGetSkill(int skillCode, out Skill skill) => CombatResourceRegistry.SkillMap.TryGetValue(skillCode, out skill);
}

public static class CombatObservationTraits
{
    private const int RestoreHpSkillCode = 1010000;
    private const int RestSkillCode = 10001;
    private const int EnhanceSpiritBenedictionBaseSkillCode = 16190000;
    private const long LightOfProtectionDirectHealingDetailRaw = 0x0000000267C58D55L;
    private const ulong HpAbsorptionDirectHealingDetailPrefix = 0x000000013B9A0000UL;
    private const ulong HpAbsorptionDirectHealingDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const ulong DirectHpRestoreDetailPrefix = 0x0000000163F40000UL;
    private const ulong DirectHpRestoreDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const ulong DirectHealingDetailPrefix = 0x000000016A180000UL;
    private const ulong DirectHealingDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const ulong AegisShieldHealingDetailPrefix = 0x000000014BD10000UL;
    private const ulong AegisShieldHealingDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const int WardingStrikeBaseSkillCode = 12350000;

    public static bool IsRestoreHp(in CombatObservation observation) =>
        MatchesExact(in observation, RestoreHpSkillCode, RestSkillCode);

    public static bool IsKnownDirectHealing(in CombatObservation observation) =>
        IsLightOfProtectionDirectHealing(in observation) ||
        IsDirectHealingDetailShape(in observation) ||
        IsAegisShieldHealingShape(in observation) ||
        MatchesExact(
            in observation,
            16120000,
            17720000) ||
        MatchesBase(in observation, 13710000, 13790000, 17090000, 17100000, 17120000, 18120000);

    public static bool IsKnownDirectPeriodicHealing(int sourceId, int targetId, in CombatObservation observation) =>
        IsDirectSelfHpRecoveryEffect(sourceId, targetId, in observation) ||
        MatchesExact(in observation, 16120350, 2011101) ||
        MatchesBase(in observation, 18160000);

    public static bool IsDirectSupportValueShape(int sourceId, int targetId, in CombatObservation observation) =>
        IsPositiveDirect0438Value(sourceId, targetId, in observation) &&
        IsEnhanceSpiritBenedictionDirectSupportShape(in observation);

    public static bool IsKnownPeriodicHealing(int sourceId, int targetId, in CombatObservation observation) =>
        IsRestoreHp(in observation) ||
        IsKnownDirectHealing(in observation) ||
        IsKnownDirectPeriodicHealing(sourceId, targetId, in observation) ||
        IsKnownPeriodicHealingPool(in observation);

    public static bool IsKnownShield(in CombatObservation observation) =>
        MatchesExact(in observation, 2212001, 22120011, 15160000, 18730000) ||
        MatchesExact(in observation, 12070000, 12130040) ||
        MatchesBase(in observation, 1742000000);

    public static bool IsDirectHpRestoreShape(int sourceId, int targetId, in CombatObservation observation) =>
        IsPositiveDirect0438Value(sourceId, targetId, in observation) &&
        observation.Loop == 1 &&
        HasDetailPrefix(observation.DetailRaw, DirectHpRestoreDetailPrefix, DirectHpRestoreDetailMask);

    public static bool IsKnownPeriodicHealingPool(in CombatObservation observation) =>
        (IsPeriodicSelfMode(in observation, 9) ||
         IsPeriodicSelfMode(in observation, 11) ||
         IsPeriodicTargetMode(in observation, 9) ||
         IsPeriodicTargetMode(in observation, 11)) &&
        IsEnhanceSpiritBenediction(in observation);

    public static bool IsTargetPeriodicSupportSeed(in CombatObservation observation) =>
        IsPeriodicTargetMode(in observation, 9) ||
        IsPeriodicTargetMode(in observation, 11);

    public static bool IsPeriodicSelfMode(in CombatObservation observation, int mode) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Self && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetMode(in CombatObservation observation, int mode) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetInitialEffect(in CombatObservation observation) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == 1;

    public static string FormatEffectLabel(in CombatObservation observation)
    {
        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return FormatPeriodicEffectLabel(observation.PeriodicRelation, observation.PeriodicMode);

        return observation.EffectTag == PacketEffectTag.None
            ? string.Empty
            : FormatEffectTagLabel(observation.EffectTag);
    }

    private static bool IsLightOfProtectionDirectHealing(in CombatObservation observation) =>
        observation.Damage > 0 &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2 &&
        observation.Loop == 2 &&
        observation.DetailRaw == LightOfProtectionDirectHealingDetailRaw;

    private static bool IsDirectHealingDetailShape(in CombatObservation observation) =>
        observation.Damage > 0 &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2 &&
        observation.Loop == 1 &&
        HasDetailPrefix(observation.DetailRaw, DirectHealingDetailPrefix, DirectHealingDetailMask);

    private static bool IsAegisShieldHealingShape(in CombatObservation observation) =>
        observation.Damage > 0 &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2 &&
        observation.Loop == 1 &&
        HasDetailPrefix(observation.DetailRaw, AegisShieldHealingDetailPrefix, AegisShieldHealingDetailMask);

    private static bool IsDirectSelfHpRecoveryEffect(int sourceId, int targetId, in CombatObservation observation) =>
        IsHpAbsorptionDirectSelfRestore(sourceId, targetId, in observation) ||
        IsWardingStrikeDirectSelfRestore(sourceId, targetId, in observation);

    private static bool IsHpAbsorptionDirectSelfRestore(int sourceId, int targetId, in CombatObservation observation) =>
        IsPositiveSelfDirect0438Value(sourceId, targetId, in observation) &&
        HasDetailPrefix(
            observation.DetailRaw,
            HpAbsorptionDirectHealingDetailPrefix,
            HpAbsorptionDirectHealingDetailMask);

    private static bool IsWardingStrikeDirectSelfRestore(int sourceId, int targetId, in CombatObservation observation) =>
        IsPositiveSelfDirect0438Value(sourceId, targetId, in observation) &&
        MatchesBase(in observation, WardingStrikeBaseSkillCode);

    private static bool IsEnhanceSpiritBenedictionDirectSupportShape(in CombatObservation observation) =>
        IsEnhanceSpiritBenediction(in observation) &&
        observation.Loop == 2;

    private static bool IsPositiveSelfDirect0438Value(int sourceId, int targetId, in CombatObservation observation) =>
        IsPositiveDirect0438Value(sourceId, targetId, in observation) &&
        sourceId == targetId;

    private static bool IsPositiveDirect0438Value(int sourceId, int targetId, in CombatObservation observation) =>
        observation.Damage > 0 &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        sourceId > 0 &&
        targetId > 0 &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2;

    private static bool HasDetailPrefix(long detailRaw, ulong prefix, ulong mask) =>
        detailRaw > 0 &&
        (((ulong)detailRaw) & mask) == prefix;

    private static bool IsEnhanceSpiritBenediction(in CombatObservation observation) =>
        MatchesBase(in observation, EnhanceSpiritBenedictionBaseSkillCode) ||
        MatchesExact(in observation, EnhanceSpiritBenedictionBaseSkillCode, 16190010, 16190020, 16190030) ||
        MatchesByHundred(in observation, EnhanceSpiritBenedictionBaseSkillCode);

    private static bool MatchesExact(in CombatObservation observation, int skillCode)
    {
        return MatchesSkillCode(in observation, skillCode);
    }

    private static bool MatchesExact(in CombatObservation observation, int skillCode0, int skillCode1)
    {
        return MatchesSkillCode(in observation, skillCode0) ||
               MatchesSkillCode(in observation, skillCode1);
    }

    private static bool MatchesExact(in CombatObservation observation, int skillCode0, int skillCode1, int skillCode2)
    {
        return MatchesSkillCode(in observation, skillCode0) ||
               MatchesSkillCode(in observation, skillCode1) ||
               MatchesSkillCode(in observation, skillCode2);
    }

    private static bool MatchesExact(in CombatObservation observation, int skillCode0, int skillCode1, int skillCode2, int skillCode3)
    {
        return MatchesSkillCode(in observation, skillCode0) ||
               MatchesSkillCode(in observation, skillCode1) ||
               MatchesSkillCode(in observation, skillCode2) ||
               MatchesSkillCode(in observation, skillCode3);
    }

    private static bool MatchesSkillCode(in CombatObservation observation, int skillCode)
    {
        if (skillCode <= 0)
            return false;

        if (observation.SkillCode == skillCode || observation.OriginalSkillCode == skillCode)
            return true;

        return CombatResourceRegistry.InferOriginalSkillCode(observation.OriginalSkillCode) == skillCode ||
               CombatResourceRegistry.InferOriginalSkillCode(observation.SkillCode) == skillCode;
    }

    private static bool MatchesBase(in CombatObservation observation, int baseSkillCode)
    {
        return MatchesBaseSkillCode(in observation, baseSkillCode);
    }

    private static bool MatchesBase(in CombatObservation observation, int baseSkillCode0, int baseSkillCode1, int baseSkillCode2, int baseSkillCode3, int baseSkillCode4, int baseSkillCode5)
    {
        return MatchesBaseSkillCode(in observation, baseSkillCode0) ||
               MatchesBaseSkillCode(in observation, baseSkillCode1) ||
               MatchesBaseSkillCode(in observation, baseSkillCode2) ||
               MatchesBaseSkillCode(in observation, baseSkillCode3) ||
               MatchesBaseSkillCode(in observation, baseSkillCode4) ||
               MatchesBaseSkillCode(in observation, baseSkillCode5);
    }

    private static bool MatchesBaseSkillCode(in CombatObservation observation, int baseSkillCode)
    {
        if (baseSkillCode <= 0)
            return false;

        if (observation.BaseSkillCode == baseSkillCode)
            return true;

        return CombatResourceRegistry.ParseSkillVariant(observation.OriginalSkillCode).BaseSkillCode == baseSkillCode ||
               CombatResourceRegistry.ParseSkillVariant(observation.SkillCode).BaseSkillCode == baseSkillCode;
    }

    private static bool MatchesByHundred(in CombatObservation observation, int skillCode) =>
        MatchesByHundred(observation.SkillCode, skillCode) ||
        MatchesByHundred(observation.OriginalSkillCode, skillCode);

    private static bool MatchesByHundred(int candidateSkillCode, int skillCode) =>
        candidateSkillCode > 0 &&
        skillCode > 0 &&
        candidateSkillCode / 100 == skillCode;

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
