using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.ViewModels;

internal static class SkillDetailSectionRules
{
    public static bool Matches(in CombatMetricDetailEvent packet, DetailSectionKind sectionKind, int combatantId)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.TargetId == combatantId,
            _ => false
        };
    }

    public static int GetCounterpartCombatantId(in CombatMetricDetailEvent packet, DetailSectionKind sectionKind)
    {
        if (sectionKind == DetailSectionKind.IncomingShield &&
            packet.Metric is CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed &&
            packet.SourceId > 0 &&
            packet.TargetId > 0 &&
            packet.SourceId != packet.TargetId)
        {
            return 0;
        }

        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.TargetId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.SourceId,
            _ => 0
        };
    }

    public static bool Contributes(in CombatMetricDetailEvent packet, DetailSectionKind sectionKind)
    {
        var contribution = packet.Contribution;
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => contribution.Metric == CombatMetricKind.Damage,
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => contribution.Metric == CombatMetricKind.Healing,
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => contribution.Metric is CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed,
            _ => false
        };
    }

    public static long GetContributionAmount(in CombatMetricDetailEvent packet, DetailSectionKind sectionKind)
    {
        return Contributes(in packet, sectionKind) ? packet.Contribution.Amount : 0;
    }

    public static bool Matches(in CombatMechanicDetailEvent packet, DetailSectionKind sectionKind, int combatantId) =>
        sectionKind switch
        {
            DetailSectionKind.OutgoingDamage => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage => packet.TargetId == combatantId,
            _ => false
        };

    public static int GetCounterpartCombatantId(in CombatMechanicDetailEvent packet, DetailSectionKind sectionKind) =>
        sectionKind switch
        {
            DetailSectionKind.OutgoingDamage => packet.TargetId,
            DetailSectionKind.IncomingDamage => packet.SourceId,
            _ => 0
        };

    public static bool Matches(in CombatResourceDetailEvent packet, DetailSectionKind sectionKind, int combatantId) =>
        sectionKind switch
        {
            DetailSectionKind.OutgoingResource => packet.SourceId == combatantId,
            DetailSectionKind.IncomingResource => packet.TargetId == combatantId,
            _ => false
        };

    public static int GetCounterpartCombatantId(in CombatResourceDetailEvent packet, DetailSectionKind sectionKind) =>
        sectionKind switch
        {
            DetailSectionKind.OutgoingResource => packet.TargetId,
            DetailSectionKind.IncomingResource => packet.SourceId,
            _ => 0
        };
}
