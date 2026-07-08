using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.ViewModels;

internal static class SkillDetailSectionRules
{
    public static bool Matches(in CombatDetailEvent packet, DetailSectionKind sectionKind, int combatantId)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.TargetId == combatantId,
            _ => false
        };
    }

    public static int GetCounterpartCombatantId(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        if (sectionKind == DetailSectionKind.IncomingShield &&
            packet.ValueKind == CombatValueKind.Shield &&
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

    public static bool Contributes(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        var contribution = packet.Contribution;
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => contribution.CountsAsDamage,
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => contribution.CountsAsHealing,
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => contribution.CountsAsShieldGrant || contribution.CountsAsShieldAbsorbed,
            _ => false
        };
    }

    public static long GetContributionAmount(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => packet.Contribution.DamageAmount,
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => packet.Contribution.HealingAmount,
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => packet.Contribution.ShieldGrantAmount + packet.Contribution.ShieldAbsorbedAmount,
            _ => 0L
        };
    }
}
