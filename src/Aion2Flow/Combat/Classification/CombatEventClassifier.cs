using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Combat.Classification;

public static class CombatEventClassifier
{
    public static CombatEventKind Classify(ParsedCombatPacket packet) => ClassifyPacket(packet).EventKind;

    public static CombatValueKind ClassifyValueKind(ParsedCombatPacket packet) => ClassifyPacket(packet).ValueKind;

    public static bool CountsTowardsDamage(ParsedCombatPacket packet) => packet.EventKind == CombatEventKind.Damage;

    public static string DisplaySkillNameFor(int skillCode)
    {
        return TryGetDisplaySkillName(skillCode, out var skillName)
            ? skillName
            : string.Empty;
    }

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyPacket(ParsedCombatPacket packet)
    {
        if (IsOutcomeOnlyAvoidance(packet))
        {
            return (CombatEventKind.Damage, CombatValueKind.Damage);
        }

        if (IsDrainHealSynthesis(packet))
        {
            return (CombatEventKind.Healing, CombatValueKind.DrainHealing);
        }

        if (packet.IsPeriodicEffect)
        {
            return ClassifyPeriodicPacket(packet);
        }

        return ClassifyDirectPacket(packet);
    }

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyDirectPacket(ParsedCombatPacket packet)
    {
        if (packet.ResourceKind == CombatResourceKind.Health)
        {
            return (CombatEventKind.Healing, CombatValueKind.Healing);
        }

        if (PacketSkillTraits.IsRestoreHp(packet))
        {
            return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
        }

        if (PacketSkillTraits.IsDirectHpRestoreShape(packet))
        {
            return (CombatEventKind.Healing, CombatValueKind.Healing);
        }

        if (PacketSkillTraits.IsDirectSupportValueShape(packet))
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (packet.ResourceKind == CombatResourceKind.Mana)
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (PacketSkillTraits.IsKnownDirectPeriodicHealing(packet))
        {
            return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
        }

        if (PacketSkillTraits.IsKnownDirectHealing(packet))
        {
            return (CombatEventKind.Healing, CombatValueKind.Healing);
        }

        if (PacketSkillTraits.IsKnownShield(packet))
        {
            return (CombatEventKind.Support, CombatValueKind.Shield);
        }

        if (packet.SourceId > 0 &&
            packet.TargetId > 0 &&
            packet.SourceId == packet.TargetId)
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        return (CombatEventKind.Damage, CombatValueKind.Damage);
    }

    private static (CombatEventKind EventKind, CombatValueKind ValueKind) ClassifyPeriodicPacket(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicSelfEffect)
        {
            if (packet.IsPeriodicSelfMode(10))
            {
                return (CombatEventKind.Support, CombatValueKind.Support);
            }

            if (PacketSkillTraits.IsKnownShield(packet))
            {
                return (CombatEventKind.Support, CombatValueKind.Shield);
            }

            if (packet.ResourceKind == CombatResourceKind.Mana)
            {
                return (CombatEventKind.Support, CombatValueKind.Support);
            }

            if (packet.ResourceKind == CombatResourceKind.Health ||
                packet.IsPeriodicSelfMode(11) ||
                PacketSkillTraits.IsRestoreHp(packet) ||
                PacketSkillTraits.IsKnownPeriodicHealing(packet))
            {
                return (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
            }

            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (!packet.IsPeriodicTargetEffect)
        {
            return (CombatEventKind.Damage, CombatValueKind.Damage);
        }

        if (packet.IsPeriodicTargetMode(8))
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (PacketSkillTraits.IsKnownShield(packet))
        {
            return (CombatEventKind.Support, CombatValueKind.Shield);
        }

        if (packet.ResourceKind == CombatResourceKind.Mana)
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        if (packet.ResourceKind == CombatResourceKind.Health ||
            PacketSkillTraits.IsKnownPeriodicHealing(packet))
        {
            return packet.IsPeriodicTargetInitialEffect
                ? (CombatEventKind.Healing, CombatValueKind.Healing)
                : (CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
        }

        if (PacketSkillTraits.IsTargetPeriodicSupportSeed(packet))
        {
            return (CombatEventKind.Support, CombatValueKind.Support);
        }

        return packet.IsPeriodicTargetInitialEffect
            ? (CombatEventKind.Damage, CombatValueKind.Damage)
            : (CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
    }

    private static bool IsOutcomeOnlyAvoidance(ParsedCombatPacket packet)
    {
        if (packet.Damage > 0)
        {
            return false;
        }

        if ((packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
        {
            return false;
        }

        return Math.Max(packet.HitContribution, packet.AttemptContribution) > 0;
    }

    private static bool IsDrainHealSynthesis(ParsedCombatPacket packet) =>
        packet.SourceId > 0
        && packet.SourceId == packet.TargetId
        && packet.Damage > 0
        && packet.DrainHealAmount > 0;

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
        skill = default;

        if (CombatMetricsEngine.SkillDisplayMap.TryGetValue(skillCode, out skill))
        {
            return true;
        }

        return TryGetSkill(skillCode, out skill);
    }

    private static bool TryGetSkill(int skillCode, out Skill skill)
    {
        skill = default;
        if (CombatMetricsEngine.SkillMap is null)
        {
            return false;
        }

        return CombatMetricsEngine.SkillMap.TryGetValue(skillCode, out skill);
    }
}

internal static class PacketSkillTraits
{
    private const int RestoreHpSkillCode = 1010000;
    private const int RestSkillCode = 10001;
    private const int EnhanceSpiritBenedictionBaseSkillCode = 16190000;
    private const int LightOfProtectionSkillCode = 17410040;
    private const long LightOfProtectionDirectHealingDetailRaw = 0x0000000267C58D55L;
    private const int HpAbsorptionEffectBaseSkillCode = 10000000;
    private const ulong HpAbsorptionDirectHealingDetailPrefix = 0x000000013B9A0000UL;
    private const ulong HpAbsorptionDirectHealingDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const ulong DirectHpRestoreDetailPrefix = 0x0000000163F40000UL;
    private const ulong DirectHpRestoreDetailMask = 0xFFFFFFFFFFFF0000UL;
    private const int WardingStrikeBaseSkillCode = 12350000;

    public static bool IsRestoreHp(ParsedCombatPacket packet) =>
        MatchesExact(packet, RestoreHpSkillCode, RestSkillCode);

    public static bool IsKnownDirectHealing(ParsedCombatPacket packet) =>
        IsLightOfProtectionDirectHealing(packet) ||
        MatchesExact(
            packet,
            17720000,
            17800000) ||
        MatchesBase(packet, 13710000, 13790000, 17090000, 17100000, 17120000, 18120000);

    public static bool IsKnownDirectPeriodicHealing(ParsedCombatPacket packet) =>
        IsDirectSelfHpRecoveryEffect(packet) ||
        MatchesExact(packet, 16120350, 2011101) ||
        MatchesBase(packet, 18160000);

    public static bool IsDirectSupportValueShape(ParsedCombatPacket packet) =>
        IsPositiveDirect0438Value(packet) &&
        IsEnhanceSpiritBenedictionDirectSupportShape(packet);

    public static bool IsKnownPeriodicHealing(ParsedCombatPacket packet) =>
        IsRestoreHp(packet) ||
        IsKnownDirectHealing(packet) ||
        IsKnownDirectPeriodicHealing(packet) ||
        IsKnownPeriodicHealingPool(packet);

    public static bool IsKnownShield(ParsedCombatPacket packet) =>
        MatchesExact(packet, 2212001, 22120011, 15160000, 18730000) ||
        MatchesBase(packet, 1742000000);

    public static bool IsDirectHpRestoreShape(ParsedCombatPacket packet) =>
        IsPositiveDirect0438Value(packet) &&
        packet.Loop == 1 &&
        HasDetailPrefix(packet.DetailRaw, DirectHpRestoreDetailPrefix, DirectHpRestoreDetailMask);

    public static bool IsTargetPeriodicSupportSeed(ParsedCombatPacket packet) =>
        packet.IsPeriodicTargetMode(9) ||
        packet.IsPeriodicTargetMode(11);

    public static bool IsKnownPeriodicHealingPool(ParsedCombatPacket packet) =>
        (packet.IsPeriodicSelfMode(9) ||
         packet.IsPeriodicSelfMode(11) ||
         packet.IsPeriodicTargetMode(9) ||
         packet.IsPeriodicTargetMode(11)) &&
        IsEnhanceSpiritBenediction(packet);

    private static bool IsLightOfProtectionDirectHealing(ParsedCombatPacket packet) =>
        MatchesExact(packet, LightOfProtectionSkillCode) &&
        packet.Damage > 0 &&
        packet.LayoutTag == 4 &&
        packet.Flag == 0 &&
        packet.Type == 2 &&
        packet.Loop == 2 &&
        packet.DetailRaw == LightOfProtectionDirectHealingDetailRaw;

    private static bool IsDirectSelfHpRecoveryEffect(ParsedCombatPacket packet) =>
        IsHpAbsorptionDirectSelfRestore(packet) ||
        IsWardingStrikeDirectSelfRestore(packet);

    private static bool IsHpAbsorptionDirectSelfRestore(ParsedCombatPacket packet) =>
        IsPositiveSelfDirect0438Value(packet) &&
        MatchesBase(packet, HpAbsorptionEffectBaseSkillCode) &&
        HasDetailPrefix(
            packet.DetailRaw,
            HpAbsorptionDirectHealingDetailPrefix,
            HpAbsorptionDirectHealingDetailMask);

    private static bool IsWardingStrikeDirectSelfRestore(ParsedCombatPacket packet) =>
        IsPositiveSelfDirect0438Value(packet) &&
        MatchesBase(packet, WardingStrikeBaseSkillCode);

    private static bool IsEnhanceSpiritBenedictionDirectSupportShape(ParsedCombatPacket packet) =>
        IsEnhanceSpiritBenediction(packet) &&
        packet.Loop == 2;

    private static bool IsPositiveSelfDirect0438Value(ParsedCombatPacket packet) =>
        IsPositiveDirect0438Value(packet) &&
        packet.SourceId == packet.TargetId;

    private static bool IsPositiveDirect0438Value(ParsedCombatPacket packet) =>
        packet.Damage > 0 &&
        !packet.IsPeriodicEffect &&
        packet.SourceId > 0 &&
        packet.TargetId > 0 &&
        packet.LayoutTag == 4 &&
        packet.Flag == 0 &&
        packet.Type == 2;

    private static bool HasDetailPrefix(long detailRaw, ulong prefix, ulong mask) =>
        detailRaw > 0 &&
        (((ulong)detailRaw) & mask) == prefix;

    private static bool IsEnhanceSpiritBenediction(ParsedCombatPacket packet) =>
        MatchesBase(packet, EnhanceSpiritBenedictionBaseSkillCode) ||
        MatchesExact(packet, EnhanceSpiritBenedictionBaseSkillCode, 16190010, 16190020, 16190030) ||
        MatchesByHundred(packet, EnhanceSpiritBenedictionBaseSkillCode);

    private static bool MatchesExact(ParsedCombatPacket packet, params int[] skillCodes)
    {
        foreach (var skillCode in skillCodes)
        {
            if (skillCode <= 0)
            {
                continue;
            }

            if (packet.SkillCode == skillCode || packet.OriginalSkillCode == skillCode)
            {
                return true;
            }

            if (CombatMetricsEngine.InferOriginalSkillCode(packet.OriginalSkillCode) == skillCode ||
                CombatMetricsEngine.InferOriginalSkillCode(packet.SkillCode) == skillCode)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesBase(ParsedCombatPacket packet, params int[] baseSkillCodes)
    {
        foreach (var baseSkillCode in baseSkillCodes)
        {
            if (baseSkillCode <= 0)
            {
                continue;
            }

            if (packet.BaseSkillCode == baseSkillCode)
            {
                return true;
            }

            if (CombatMetricsEngine.ParseSkillVariant(packet.OriginalSkillCode).BaseSkillCode == baseSkillCode ||
                CombatMetricsEngine.ParseSkillVariant(packet.SkillCode).BaseSkillCode == baseSkillCode)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesByHundred(ParsedCombatPacket packet, int skillCode) =>
        MatchesByHundred(packet.SkillCode, skillCode) ||
        MatchesByHundred(packet.OriginalSkillCode, skillCode);

    private static bool MatchesByHundred(int candidateSkillCode, int skillCode) =>
        candidateSkillCode > 0 &&
        skillCode > 0 &&
        candidateSkillCode / 100 == skillCode;
}
