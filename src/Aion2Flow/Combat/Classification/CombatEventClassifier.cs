using System.Collections.Concurrent;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Combat.Classification;

public static class CombatEventClassifier
{
    private const int RestoreHpSkillCode = 1010000;
    private static readonly ConcurrentDictionary<int, bool> NonHealthResourceRestoreFamilyCache = [];

    internal static void ClearCaches()
    {
        NonHealthResourceRestoreFamilyCache.Clear();
    }

    public static CombatEventKind Classify(ParsedCombatPacket packet)
    {
        if (IsObservedRecoveryTick(packet))
        {
            return CombatEventKind.Healing;
        }

        var semantics = ResolveSkillSemantics(packet.SkillCode);
        if (IsObservedDirectHealing(packet, semantics))
        {
            return CombatEventKind.Healing;
        }

        if (IsObservedSelfResourceSupportProc(packet, semantics))
        {
            return CombatEventKind.Support;
        }

        if (IsObservedFamilyResourceSupportProc(packet, semantics))
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
        if (IsObservedRecoveryTick(packet))
        {
            return CombatValueKind.PeriodicHealing;
        }

        if (IsObservedSelfHealingProc(packet))
        {
            return CombatValueKind.PeriodicHealing;
        }

        var semantics = ResolveSkillSemantics(packet.SkillCode);
        if (IsObservedDirectHealing(packet, semantics))
        {
            return CombatValueKind.Healing;
        }

        if (IsObservedSelfResourceSupportProc(packet, semantics))
        {
            return CombatValueKind.Support;
        }

        if (IsObservedFamilyResourceSupportProc(packet, semantics))
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

        if (packet.EffectFamily.StartsWith("periodic-self", StringComparison.Ordinal))
        {
            if (string.Equals(packet.EffectFamily, "periodic-self-mode-10", StringComparison.Ordinal))
            {
                eventKind = CombatEventKind.Support;
                return true;
            }

            if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
            {
                eventKind = CombatEventKind.Support;
                return true;
            }

            if (string.Equals(packet.EffectFamily, "periodic-self-mode-11", StringComparison.Ordinal))
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

        if (!packet.EffectFamily.StartsWith("periodic-target", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(packet.EffectFamily, "periodic-target-mode-8", StringComparison.Ordinal))
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

        if (packet.EffectFamily.StartsWith("periodic-self", StringComparison.Ordinal))
        {
            if (string.Equals(packet.EffectFamily, "periodic-self-mode-10", StringComparison.Ordinal))
            {
                valueKind = CombatValueKind.Support;
                return true;
            }

            if ((semantics & SkillSemantics.ShieldOrBarrier) != 0)
            {
                valueKind = CombatValueKind.Shield;
                return true;
            }

            if (string.Equals(packet.EffectFamily, "periodic-self-mode-11", StringComparison.Ordinal))
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

        if (!packet.EffectFamily.StartsWith("periodic-target", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(packet.EffectFamily, "periodic-target-mode-8", StringComparison.Ordinal))
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

        if (HasOffensivePeriodicSignal(semantics))
        {
            valueKind = string.Equals(packet.EffectFamily, "periodic-target-initial", StringComparison.Ordinal)
                ? CombatValueKind.Damage
                : CombatValueKind.PeriodicDamage;
            return true;
        }

        if ((semantics & (SkillSemantics.Healing | SkillSemantics.PeriodicHealing)) != 0)
        {
            valueKind = string.Equals(packet.EffectFamily, "periodic-target-initial", StringComparison.Ordinal)
                ? CombatValueKind.Healing
                : CombatValueKind.PeriodicHealing;
            return true;
        }

        if (IsSupportOnly(semantics))
        {
            valueKind = CombatValueKind.Support;
            return true;
        }

        valueKind = string.Equals(packet.EffectFamily, "periodic-target-initial", StringComparison.Ordinal)
            ? CombatValueKind.Damage
            : CombatValueKind.PeriodicDamage;
        return true;
    }

    private static bool HasOffensivePeriodicSignal(SkillSemantics semantics)
        => (semantics & (SkillSemantics.Damage |
                         SkillSemantics.PeriodicDamage |
                         SkillSemantics.DrainOrAbsorb)) != 0;

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
               && (semantics & SkillSemantics.DrainOrAbsorb) == 0;
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

    private static bool IsObservedFamilyResourceSupportProc(ParsedCombatPacket packet, SkillSemantics semantics)
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

        return FamilyHasNonHealthResourceRestoreSkill(baseSkillCode);
    }

    private static bool IsObservedPeriodicHealingSentinelOverflowTick(ParsedCombatPacket packet, SkillSemantics semantics)
    {
        if (!packet.IsPeriodicEffect ||
            packet.Damage < 2_000_000_000 ||
            (!string.Equals(packet.EffectFamily, "periodic-target-mode-9", StringComparison.Ordinal) &&
             !string.Equals(packet.EffectFamily, "periodic-target-mode-11", StringComparison.Ordinal)))
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

    private static bool FamilyHasNonHealthResourceRestoreSkill(int baseSkillCode)
    {
        if (baseSkillCode <= 0 || CombatMetricsEngine.SkillMap.Count == 0)
        {
            return false;
        }

        return NonHealthResourceRestoreFamilyCache.GetOrAdd(baseSkillCode, static candidateBaseSkillCode =>
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
}
