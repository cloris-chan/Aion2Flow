using System.Collections.Concurrent;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Combat.Classification;

public static class CombatEventClassifier
{
    private const int RestoreHpSkillCode = 1010000;
    private const int InstanceClearRestoreBaseSkillCode = 1900000;
    private const int SpiritDescentSummonRestoreSkillCode = 16990004;
    private static readonly ConcurrentDictionary<int, bool> NonHealthResourceRestoreBaseSkillCache = [];

    internal static void ClearCaches()
    {
        NonHealthResourceRestoreBaseSkillCache.Clear();
    }

    public static CombatEventKind Classify(ParsedCombatPacket packet)
    {
        if (IsOutcomeOnlyAvoidance(packet))
        {
            return CombatEventKind.Damage;
        }

        if (IsDrainHealSynthesis(packet))
        {
            return CombatEventKind.Healing;
        }

        if (IsObservedRecoveryTick(packet))
        {
            return CombatEventKind.Healing;
        }

        if (IsInstanceClearRestore(packet))
        {
            return CombatEventKind.Healing;
        }

        if (IsSpiritDescentSummonRestore(packet))
        {
            return CombatEventKind.Healing;
        }

        var semantics = ResolveSkillSemantics(packet.SkillCode);
        if (IsOffensiveHitFromDualPurposeSkill(packet, semantics))
        {
            return CombatEventKind.Damage;
        }

        if (IsObservedOtherTargetSupportDamage(packet, semantics))
        {
            return CombatEventKind.Damage;
        }

        if (IsObservedDirectHealing(packet, semantics))
        {
            return CombatEventKind.Healing;
        }

        if (IsDamageTaggedDirectSelfSupportSignal(packet, semantics))
        {
            return CombatEventKind.Support;
        }

        if (IsObservedSelfResourceSupportProc(packet, semantics))
        {
            return CombatEventKind.Support;
        }

        if (IsObservedBaseSkillResourceSupportProc(packet, semantics))
        {
            return CombatEventKind.Support;
        }

        if (TryClassifyPeriodicEvent(packet, semantics, out var periodicEventKind))
        {
            return periodicEventKind;
        }

        if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
        {
            return CombatEventKind.Support;
        }

        if ((semantics & SkillSemantics.DrainOrAbsorb) != 0)
        {
            return packet.TargetId == packet.SourceId
                ? CombatEventKind.Healing
                : CombatEventKind.Damage;
        }

        if ((semantics & SkillSemantics.Healing) != 0 || (semantics & SkillSemantics.PeriodicHealing) != 0)
        {
            return CombatEventKind.Healing;
        }

        if (IsObservedUnknownSelfSupportProc(packet, semantics))
        {
            return CombatEventKind.Support;
        }

        if (!TryGetSkill(packet.SkillCode, out var skill))
        {
            return CombatEventKind.Damage;
        }

        switch (skill.Kind)
        {
            case SkillKind.Healing:
            case SkillKind.PeriodicHealing:
                return CombatEventKind.Healing;
            case SkillKind.ShieldOrBarrier:
            case SkillKind.Support:
                return CombatEventKind.Support;
            case SkillKind.DrainOrAbsorb:
                return packet.TargetId == packet.SourceId
                    ? CombatEventKind.Healing
                    : CombatEventKind.Damage;
            case SkillKind.Damage:
            case SkillKind.PeriodicDamage:
                return CombatEventKind.Damage;
        }

        return CombatEventKind.Damage;
    }

    public static CombatValueKind ClassifyValueKind(ParsedCombatPacket packet)
    {
        if (IsOutcomeOnlyAvoidance(packet))
        {
            return CombatValueKind.Damage;
        }

        if (IsDrainHealSynthesis(packet))
        {
            return CombatValueKind.DrainHealing;
        }

        if (IsObservedRecoveryTick(packet))
        {
            return CombatValueKind.PeriodicHealing;
        }

        if (IsObservedSelfHealingProc(packet))
        {
            return CombatValueKind.PeriodicHealing;
        }

        if (IsInstanceClearRestore(packet))
        {
            return CombatValueKind.Healing;
        }

        if (IsSpiritDescentSummonRestore(packet))
        {
            return CombatValueKind.Healing;
        }

        var semantics = ResolveSkillSemantics(packet.SkillCode);
        if (IsOffensiveHitFromDualPurposeSkill(packet, semantics))
        {
            return CombatValueKind.Damage;
        }

        if (IsObservedOtherTargetSupportDamage(packet, semantics))
        {
            return CombatValueKind.Damage;
        }

        if (IsObservedDirectHealing(packet, semantics))
        {
            return CombatValueKind.Healing;
        }

        if (IsDamageTaggedDirectSelfSupportSignal(packet, semantics))
        {
            return CombatValueKind.Support;
        }

        if (IsObservedSelfResourceSupportProc(packet, semantics))
        {
            return CombatValueKind.Support;
        }

        if (IsObservedBaseSkillResourceSupportProc(packet, semantics))
        {
            return CombatValueKind.Support;
        }

        if (TryClassifyPeriodicValueKind(packet, semantics, out var periodicValueKind))
        {
            return periodicValueKind;
        }

        if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
        {
            return CombatValueKind.Shield;
        }

        if ((semantics & SkillSemantics.DrainOrAbsorb) != 0)
        {
            return packet.TargetId == packet.SourceId
                ? CombatValueKind.DrainHealing
                : CombatValueKind.Damage;
        }

        if ((semantics & SkillSemantics.PeriodicHealing) != 0 && packet.IsPeriodicEffect)
        {
            return CombatValueKind.PeriodicHealing;
        }

        if ((semantics & SkillSemantics.PeriodicDamage) != 0)
        {
            return packet.IsPeriodicEffect
                ? CombatValueKind.PeriodicDamage
                : CombatValueKind.Damage;
        }

        if ((semantics & SkillSemantics.Healing) != 0)
        {
            return CombatValueKind.Healing;
        }

        if ((semantics & SkillSemantics.Support) != 0 && semantics == SkillSemantics.Support)
        {
            return CombatValueKind.Support;
        }

        if (IsObservedUnknownSelfSupportProc(packet, semantics))
        {
            return CombatValueKind.Support;
        }

        if (!TryGetSkill(packet.SkillCode, out var skill))
        {
            return CombatValueKind.Damage;
        }

        return skill.Kind switch
        {
            SkillKind.Healing => CombatValueKind.Healing,
            SkillKind.PeriodicHealing => packet.IsPeriodicEffect
                ? CombatValueKind.PeriodicHealing
                : CombatValueKind.Healing,
            SkillKind.ShieldOrBarrier => CombatValueKind.Shield,
            SkillKind.Support => CombatValueKind.Support,
            SkillKind.DrainOrAbsorb => packet.TargetId == packet.SourceId
                ? CombatValueKind.DrainHealing
                : CombatValueKind.Damage,
            SkillKind.PeriodicDamage => packet.IsPeriodicEffect
                ? CombatValueKind.PeriodicDamage
                : CombatValueKind.Damage,
            SkillKind.Damage => CombatValueKind.Damage,
            _ => CombatValueKind.Damage
        };
    }

    public static bool CountsTowardsDamage(ParsedCombatPacket packet) => packet.EventKind == CombatEventKind.Damage;

    public static string DisplaySkillNameFor(int skillCode)
    {
        return TryGetDisplaySkillName(skillCode, out var skillName)
            ? skillName
            : string.Empty;
    }

    public static SkillKind ResolveSkillKind(int skillCode)
    {
        return TryGetSkill(skillCode, out var skill)
            ? skill.Kind
            : SkillKind.Unknown;
    }

    public static SkillSemantics ResolveSkillSemantics(int skillCode)
    {
        return TryGetSkill(skillCode, out var skill)
            ? skill.Semantics
            : SkillSemantics.None;
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

    private static bool TryClassifyPeriodicEvent(
        ParsedCombatPacket packet,
        SkillSemantics semantics,
        out CombatEventKind eventKind)
    {
        eventKind = default;
        if (!packet.IsPeriodicEffect)
        {
            return false;
        }

        if (packet.IsPeriodicSelfEffect)
        {
            if (packet.IsPeriodicSelfMode(10))
            {
                eventKind = CombatEventKind.Support;
                return true;
            }

            if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
            {
                eventKind = CombatEventKind.Support;
                return true;
            }

            if (packet.IsPeriodicSelfMode(11))
            {
                eventKind = CombatEventKind.Healing;
                return true;
            }

            if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing | SkillSemantics.DrainOrAbsorb | SkillSemantics.ShieldOrBarrier)) == 0)
            {
                eventKind = CombatEventKind.Support;
                return true;
            }

            eventKind = CombatEventKind.Healing;
            return true;
        }

        if (!packet.IsPeriodicTargetEffect)
        {
            return false;
        }

        if (packet.IsPeriodicTargetMode(8))
        {
            eventKind = CombatEventKind.Support;
            return true;
        }

        if (IsObservedPeriodicHealingSentinelOverflowTick(packet, semantics))
        {
            eventKind = CombatEventKind.Support;
            return true;
        }

        if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
        {
            eventKind = CombatEventKind.Support;
            return true;
        }

        if (IsDamageTaggedPeriodicSupportSignal(semantics))
        {
            eventKind = CombatEventKind.Support;
            return true;
        }

        if (HasOffensivePeriodicSignal(semantics))
        {
            eventKind = CombatEventKind.Damage;
            return true;
        }

        if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing)) != 0)
        {
            eventKind = CombatEventKind.Healing;
            return true;
        }

        if (IsSupportOnly(semantics))
        {
            eventKind = CombatEventKind.Support;
            return true;
        }

        eventKind = CombatEventKind.Damage;
        return true;
    }

    private static bool TryClassifyPeriodicValueKind(
        ParsedCombatPacket packet,
        SkillSemantics semantics,
        out CombatValueKind valueKind)
    {
        valueKind = default;
        if (!packet.IsPeriodicEffect)
        {
            return false;
        }

        if (packet.IsPeriodicSelfEffect)
        {
            if (packet.IsPeriodicSelfMode(10))
            {
                valueKind = CombatValueKind.Support;
                return true;
            }

            if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
            {
                valueKind = CombatValueKind.Shield;
                return true;
            }

            if (packet.IsPeriodicSelfMode(11))
            {
                valueKind = (semantics & SkillSemantics.DrainOrAbsorb) != 0
                    ? CombatValueKind.DrainHealing
                    : CombatValueKind.PeriodicHealing;
                return true;
            }

            if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing | SkillSemantics.DrainOrAbsorb | SkillSemantics.ShieldOrBarrier)) == 0)
            {
                valueKind = CombatValueKind.Support;
                return true;
            }

            valueKind = (semantics & SkillSemantics.DrainOrAbsorb) != 0
                ? CombatValueKind.DrainHealing
                : CombatValueKind.PeriodicHealing;
            return true;
        }

        if (!packet.IsPeriodicTargetEffect)
        {
            return false;
        }

        if (packet.IsPeriodicTargetMode(8))
        {
            valueKind = CombatValueKind.Support;
            return true;
        }

        if (IsObservedPeriodicHealingSentinelOverflowTick(packet, semantics))
        {
            valueKind = CombatValueKind.Support;
            return true;
        }

        if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
        {
            valueKind = CombatValueKind.Shield;
            return true;
        }

        if (IsDamageTaggedPeriodicSupportSignal(semantics))
        {
            valueKind = CombatValueKind.Support;
            return true;
        }

        if (HasOffensivePeriodicSignal(semantics))
        {
            valueKind = packet.IsPeriodicTargetInitialEffect
                ? CombatValueKind.Damage
                : CombatValueKind.PeriodicDamage;
            return true;
        }

        if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing)) != 0)
        {
            valueKind = packet.IsPeriodicTargetInitialEffect
                ? CombatValueKind.Healing
                : CombatValueKind.PeriodicHealing;
            return true;
        }

        if (IsSupportOnly(semantics))
        {
            valueKind = CombatValueKind.Support;
            return true;
        }

        valueKind = packet.IsPeriodicTargetInitialEffect
            ? CombatValueKind.Damage
            : CombatValueKind.PeriodicDamage;
        return true;
    }

    private static bool IsDamageTaggedPeriodicSupportSignal(SkillSemantics semantics)
    {
        if ((semantics & SkillSemantics.Support) == 0 ||
            (semantics & SkillSemantics.Damage) == 0)
        {
            return false;
        }

        return (semantics & (SkillSemantics.PeriodicDamage |
                             SkillSemantics.Healing |
                             SkillSemantics.PeriodicHealing |
                             SkillSemantics.DrainOrAbsorb |
                             SkillSemantics.ShieldOrBarrier)) == 0;
    }

    private static bool HasOffensivePeriodicSignal(SkillSemantics semantics)
    {
        var offensive = semantics & (SkillSemantics.Damage |
                                     SkillSemantics.PeriodicDamage |
                                     SkillSemantics.DrainOrAbsorb);
        if (offensive == 0)
            return false;

        if (offensive == SkillSemantics.Damage
            && (semantics & SkillSemantics.PeriodicHealing) != 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsSupportOnly(SkillSemantics semantics)
    {
        if ((semantics & SkillSemantics.Support) == 0)
        {
            return false;
        }

        return (semantics & (SkillSemantics.Damage |
                             SkillSemantics.PeriodicDamage |
                             SkillSemantics.Healing |
                             SkillSemantics.PeriodicHealing |
                             SkillSemantics.DrainOrAbsorb |
                             SkillSemantics.ShieldOrBarrier)) == 0;
    }

    private static bool IsObservedDirectHealing(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect || packet.Damage <= 0)
        {
            return false;
        }

        return (semantics & SkillSemantics.Healing) != 0
               && (semantics & (SkillSemantics.DrainOrAbsorb | SkillSemantics.ShieldOrBarrier)) == 0;
    }

    private static bool IsOffensiveHitFromDualPurposeSkill(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect || packet.Damage <= 0 || packet.SourceId == packet.TargetId)
        {
            return false;
        }

        return (semantics & SkillSemantics.Damage) != 0
               && (semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing)) != 0
               && (semantics & SkillSemantics.DrainOrAbsorb) == 0;
    }

    private static bool IsObservedOtherTargetSupportDamage(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId == packet.TargetId)
        {
            return false;
        }

        if ((semantics & SkillSemantics.Support) == 0)
        {
            return false;
        }

        return (semantics & (SkillSemantics.Damage |
                             SkillSemantics.PeriodicDamage |
                             SkillSemantics.Healing |
                             SkillSemantics.PeriodicHealing |
                             SkillSemantics.DrainOrAbsorb |
                             SkillSemantics.ShieldOrBarrier |
                             SkillSemantics.NonHealthResourceRestore)) == 0;
    }

    private static bool IsDamageTaggedDirectSelfSupportSignal(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if ((semantics & (SkillSemantics.Support | SkillSemantics.Damage)) !=
            (SkillSemantics.Support | SkillSemantics.Damage))
        {
            return false;
        }

        return (semantics & (SkillSemantics.PeriodicDamage |
                             SkillSemantics.Healing |
                             SkillSemantics.PeriodicHealing |
                             SkillSemantics.DrainOrAbsorb |
                             SkillSemantics.ShieldOrBarrier |
                             SkillSemantics.NonHealthResourceRestore)) == 0;
    }

    private static bool IsObservedSelfResourceSupportProc(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        return (semantics & SkillSemantics.NonHealthResourceRestore) != 0;
    }

    private static bool IsObservedUnknownSelfSupportProc(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if (semantics != SkillSemantics.None)
        {
            return false;
        }

        return !TryGetSkill(packet.SkillCode, out var skill)
               || skill.Kind == SkillKind.Unknown;
    }

    private static bool IsObservedBaseSkillResourceSupportProc(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if ((semantics & (SkillSemantics.NonHealthResourceRestore |
                          SkillSemantics.Healing |
                          SkillSemantics.PeriodicHealing |
                          SkillSemantics.DrainOrAbsorb |
                          SkillSemantics.ShieldOrBarrier)) != 0)
        {
            return false;
        }

        if (!TryGetSkill(packet.SkillCode, out var skill))
        {
            return false;
        }

        if (skill.SourceType != SkillSourceType.PcSkill)
        {
            return false;
        }

        if (skill.Kind != SkillKind.Damage && skill.Kind != SkillKind.Unknown)
        {
            return false;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        var variant = originalSkillCode > 0
            ? CombatMetricsEngine.ParseSkillVariant(originalSkillCode)
            : default;
        var chargeStage = packet.ChargeStage > 0 ? packet.ChargeStage : variant.ChargeStage;
        if (chargeStage != 7)
        {
            return false;
        }

        var baseSkillCode = packet.BaseSkillCode > 0 ? packet.BaseSkillCode : variant.BaseSkillCode;
        if (baseSkillCode <= 0)
        {
            return false;
        }

        return BaseSkillHasNonHealthResourceRestoreVariant(baseSkillCode);
    }

    private static bool IsObservedPeriodicHealingSentinelOverflowTick(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (!packet.IsPeriodicEffect ||
            packet.Damage < 2_000_000_000 ||
            (!packet.IsPeriodicTargetMode(9) && !packet.IsPeriodicTargetMode(11)))
        {
            return false;
        }

        if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing | SkillSemantics.Support)) == 0)
        {
            return false;
        }

        return TryGetSkill(packet.SkillCode, out var skill)
               && skill.Kind == SkillKind.PeriodicHealing;
    }

    private static bool BaseSkillHasNonHealthResourceRestoreVariant(int baseSkillCode)
    {
        if (baseSkillCode <= 0 || CombatMetricsEngine.SkillMap.Count == 0)
        {
            return false;
        }

        return NonHealthResourceRestoreBaseSkillCache.GetOrAdd(baseSkillCode, static candidateBaseSkillCode =>
        {
            foreach (var skill in CombatMetricsEngine.SkillMap)
            {
                if ((skill.Semantics & SkillSemantics.NonHealthResourceRestore) == 0)
                {
                    continue;
                }

                if (CombatMetricsEngine.ParseSkillVariant(skill.Id).BaseSkillCode != candidateBaseSkillCode)
                {
                    continue;
                }

                return true;
            }

            return false;
        });
    }

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

        if (!CombatMetricsEngine.SkillMap.TryGetValue(skillCode, out skill))
        {
            return false;
        }

        return true;
    }

    private static bool IsDrainHealSynthesis(ParsedCombatPacket packet) =>
        packet.SourceId > 0
        && packet.SourceId == packet.TargetId
        && packet.Damage > 0
        && packet.DrainHealAmount > 0;

    private static bool IsObservedRecoveryTick(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicEffect || packet.Damage <= 0 || packet.SourceId <= 0 || packet.TargetId <= 0)
        {
            return false;
        }

        if (packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if (!TryGetSkill(packet.SkillCode, out var skill))
        {
            return false;
        }

        if (skill.SourceType != SkillSourceType.ItemSkill ||
            skill.Category != SkillCategory.Npc)
        {
            return false;
        }

        return skill.Id == RestoreHpSkillCode;
    }

    private static bool IsInstanceClearRestore(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicEffect || packet.Damage <= 0 || packet.SourceId <= 0 || packet.TargetId <= 0)
        {
            return false;
        }

        if (packet.SourceId != packet.TargetId)
        {
            return false;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        var variant = originalSkillCode > 0
            ? CombatMetricsEngine.ParseSkillVariant(originalSkillCode)
            : default;
        var baseSkillCode = packet.BaseSkillCode > 0
            ? packet.BaseSkillCode
            : variant.BaseSkillCode > 0
                ? variant.BaseSkillCode
                : packet.SkillCode - (packet.SkillCode % 10);

        return baseSkillCode == InstanceClearRestoreBaseSkillCode
             && packet.ResourceKind != CombatResourceKind.Mana;
    }

    private static bool IsObservedSelfHealingProc(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicEffect || packet.Damage <= 0 || packet.SourceId <= 0 || packet.TargetId <= 0)
        {
            return false;
        }

        if (packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if (!TryGetSkill(packet.SkillCode, out var skill))
        {
            return false;
        }

        if (skill.Kind != SkillKind.PeriodicHealing)
        {
            return false;
        }

        if (skill.SourceType != SkillSourceType.PcSkill &&
            skill.SourceType != SkillSourceType.Abnormal)
        {
            return false;
        }

        return true;
    }

    private static bool IsSpiritDescentSummonRestore(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicEffect ||
            packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        return packet.SkillCode == SpiritDescentSummonRestoreSkillCode ||
               originalSkillCode == SpiritDescentSummonRestoreSkillCode;
    }
}
