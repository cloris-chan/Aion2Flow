using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatContributionResolver
{
    private const SkillQuantifiedFacet DirectFacets =
        SkillQuantifiedFacet.DirectDamage |
        SkillQuantifiedFacet.DirectHealing |
        SkillQuantifiedFacet.Shield;

    private const SkillQuantifiedFacet PeriodicFacets =
        SkillQuantifiedFacet.PeriodicDamage |
        SkillQuantifiedFacet.PeriodicHealing |
        SkillQuantifiedFacet.Shield;

    public static bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        CombatPacketRule packetRule,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        if (TryResolvePacketRule(in observation, packetRule, materialization, association, out contribution))
            return true;

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return TryResolvePeriodic(in observation, materialization, association, out contribution);

        return TryResolveDirect(sourceId, targetId, in observation, materialization, association, out contribution);
    }

    internal static bool TryResolvePeriodicPoolSemanticGrant(
        in CombatWireObservation observation,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        if (observation.PeriodicMode == 9 &&
            observation.PeriodicRelation != PeriodicEffectRelation.None &&
            observation.ResourceKind == CombatResourceKind.Unknown &&
            CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var semantics) &&
            IsAuthoritativePeriodicShield(in semantics))
        {
            return Create(
                CombatMetricKind.ShieldGranted,
                CombatDeliveryKind.Pool,
                observation.Damage,
                in observation,
                CombatPacketRule.PeriodicSemantic,
                CombatResolutionAuthority.SkillSemantic,
                CombatMaterializationKind.PeriodicPoolGrant,
                association,
                in semantics,
                out contribution);
        }

        contribution = default;
        return false;
    }

    private static bool TryResolvePacketRule(
        in CombatWireObservation observation,
        CombatPacketRule packetRule,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        switch (packetRule)
        {
            case CombatPacketRule.CompactDirectValue:
                return Create(CombatMetricKind.Damage, CombatDeliveryKind.Direct, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.CompactRecovery:
                return Create(CombatMetricKind.Healing, CombatDeliveryKind.Direct, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.CompactAvoidance:
            case CombatPacketRule.ActiveSkillInvincible:
            case CombatPacketRule.PeriodicLinkInvincible:
                contribution = default;
                return false;
            case CombatPacketRule.PeriodicRecovery:
                return Create(CombatMetricKind.Healing, CombatDeliveryKind.Periodic, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.PeriodicShieldGrant:
                return Create(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Pool, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.PeriodicShieldAbsorbed:
                return Create(CombatMetricKind.ShieldAbsorbed, CombatDeliveryKind.Pool, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.DrainSecondary:
                return Create(CombatMetricKind.Healing, CombatDeliveryKind.Drain, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            case CombatPacketRule.RegenerationSecondary:
                return Create(CombatMetricKind.Healing, CombatDeliveryKind.Regeneration, observation.Damage, in observation, packetRule, CombatResolutionAuthority.Packet, materialization, association, default, out contribution);
            default:
                contribution = default;
                return false;
        }
    }

    private static bool TryResolveDirect(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        if (IsAvoidanceOutcome(in observation))
        {
            contribution = default;
            return false;
        }

        if (observation.ResourceKind == CombatResourceKind.Health)
        {
            return Create(
                CombatMetricKind.Healing,
                CombatDeliveryKind.Direct,
                observation.Damage,
                in observation,
                CombatPacketRule.DirectHealthResource,
                CombatResolutionAuthority.Packet,
                materialization,
                association,
                default,
                out contribution);
        }

        if (observation.ResourceKind != CombatResourceKind.Unknown)
        {
            contribution = default;
            return false;
        }

        if (CombatResourceRegistry.TryResolveDirectCombatResourceSemantics(in observation, out var semantics) &&
            GetSemanticMatch(in semantics) != CombatSemanticMatchKind.None &&
            TryResolveSemanticMetric(ResolveDirectSemanticFacets(in semantics), periodic: false, out var metric, out var delivery))
        {
            return Create(
                metric,
                delivery,
                observation.Damage,
                in observation,
                CombatPacketRule.DirectSemantic,
                CombatResolutionAuthority.SkillSemantic,
                materialization,
                association,
                in semantics,
                out contribution);
        }

        if (sourceId > 0 && sourceId == targetId)
        {
            contribution = default;
            return false;
        }

        if (!IsQuantifiedShape(in observation))
        {
            contribution = default;
            return false;
        }

        return Create(
            CombatMetricKind.Damage,
            CombatDeliveryKind.Direct,
            observation.Damage,
            in observation,
            CombatPacketRule.DirectFallbackDamage,
            CombatResolutionAuthority.PacketDefault,
            materialization,
            association,
            default,
            out contribution);
    }

    private static bool TryResolvePeriodic(
        in CombatWireObservation observation,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        if (observation.ResourceKind == CombatResourceKind.Health)
        {
            var healthDelivery = CombatWireTraits.IsPeriodicTargetInitialEffect(in observation)
                ? CombatDeliveryKind.Direct
                : CombatDeliveryKind.Periodic;
            return Create(
                CombatMetricKind.Healing,
                healthDelivery,
                observation.Damage,
                in observation,
                CombatPacketRule.PeriodicHealthResource,
                CombatResolutionAuthority.Packet,
                materialization,
                association,
                default,
                out contribution);
        }

        if (observation.ResourceKind != CombatResourceKind.Unknown ||
            CombatWireTraits.IsPeriodicSelfMode(in observation, 10) ||
            CombatWireTraits.IsPeriodicTargetStateSeed(in observation))
        {
            contribution = default;
            return false;
        }

        if (CombatWireTraits.IsPeriodicSelfMode(in observation, 11))
        {
            return Create(
                CombatMetricKind.Healing,
                CombatDeliveryKind.Periodic,
                observation.Damage,
                in observation,
                CombatPacketRule.PeriodicValue,
                CombatResolutionAuthority.Packet,
                materialization,
                association,
                default,
                out contribution);
        }

        if (!CombatWireTraits.IsPeriodicTargetInitialEffect(in observation) &&
            CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var semantics) &&
            GetSemanticMatch(in semantics) != CombatSemanticMatchKind.None &&
            TryResolveSemanticMetric(semantics.Semantics.QuantifiedFacets & PeriodicFacets, periodic: true, out var metric, out var delivery))
        {
            return Create(
                metric,
                delivery,
                observation.Damage,
                in observation,
                CombatPacketRule.PeriodicSemantic,
                CombatResolutionAuthority.SkillSemantic,
                materialization,
                association,
                in semantics,
                out contribution);
        }

        if (observation.PeriodicRelation != PeriodicEffectRelation.Target || !IsQuantifiedShape(in observation))
        {
            contribution = default;
            return false;
        }

        var fallbackDelivery = CombatWireTraits.IsPeriodicTargetInitialEffect(in observation)
            ? CombatDeliveryKind.Direct
            : CombatDeliveryKind.Periodic;
        return Create(
            CombatMetricKind.Damage,
            fallbackDelivery,
            observation.Damage,
            in observation,
            CombatPacketRule.PeriodicFallbackDamage,
            CombatResolutionAuthority.PacketDefault,
            materialization,
            association,
            default,
            out contribution);
    }

    private static bool TryResolveSemanticMetric(
        SkillQuantifiedFacet facets,
        bool periodic,
        out CombatMetricKind metric,
        out CombatDeliveryKind delivery)
    {
        delivery = periodic ? CombatDeliveryKind.Periodic : CombatDeliveryKind.Direct;
        switch (facets)
        {
            case SkillQuantifiedFacet.DirectDamage:
            case SkillQuantifiedFacet.PeriodicDamage:
                metric = CombatMetricKind.Damage;
                return true;
            case SkillQuantifiedFacet.DirectHealing:
            case SkillQuantifiedFacet.PeriodicHealing:
                metric = CombatMetricKind.Healing;
                return true;
            case SkillQuantifiedFacet.Shield:
                metric = CombatMetricKind.ShieldGranted;
                delivery = periodic ? CombatDeliveryKind.Pool : CombatDeliveryKind.Direct;
                return true;
            default:
                metric = default;
                return false;
        }
    }

    internal static CombatSemanticMatchKind GetSemanticMatch(in SkillSemanticResourceResolution semantics)
    {
        if (semantics.NodeId <= 0)
            return CombatSemanticMatchKind.None;

        if (semantics.NodeKind == SkillSemanticResourceNodeKind.SkillEffect &&
            semantics.RawId == unchecked((uint)semantics.NodeId))
        {
            return CombatSemanticMatchKind.ExactNode;
        }

        return semantics.HasUnambiguousSlot
            ? CombatSemanticMatchKind.UnambiguousSlot
            : CombatSemanticMatchKind.None;
    }

    internal static bool IsAuthoritativePeriodicShield(in SkillSemanticResourceResolution semantics) =>
        GetSemanticMatch(in semantics) != CombatSemanticMatchKind.None &&
        (semantics.Semantics.QuantifiedFacets & PeriodicFacets) == SkillQuantifiedFacet.Shield;

    private static SkillQuantifiedFacet ResolveDirectSemanticFacets(in SkillSemanticResourceResolution semantics) =>
        (semantics.DirectSemantics.QuantifiedFacets &
         (SkillQuantifiedFacet.DirectDamage | SkillQuantifiedFacet.DirectHealing)) |
        (semantics.Semantics.QuantifiedFacets & SkillQuantifiedFacet.Shield);

    private static bool Create(
        CombatMetricKind metric,
        CombatDeliveryKind delivery,
        long amount,
        in CombatWireObservation observation,
        CombatPacketRule packetRule,
        CombatResolutionAuthority authority,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        in SkillSemanticResourceResolution semantics,
        out CombatContribution contribution)
    {
        var semanticMatch = GetSemanticMatch(in semantics);
        var trace = new CombatResolutionTrace(
            packetRule,
            semanticMatch,
            authority,
            materialization,
            association,
            semantics.DirectSemantics,
            semantics.Semantics,
            semantics.RawId == 0 ? default : ResourceEffectRef.FromRaw(semantics.RawId),
            semantics.NodeKind,
            semantics.NodeId,
            semantics.Slot?.SkillId ?? 0,
            semantics.Slot?.Slot ?? -1,
            semantics.CandidateSlotCount);
        contribution = new CombatContribution(
            metric,
            delivery,
            Math.Max(0, amount),
            trace);
        return amount > 0;
    }

    private static bool IsQuantifiedShape(in CombatWireObservation observation) =>
        observation.Damage > 0 ||
        Math.Max(observation.HitCount, observation.AttemptCount) > 0 ||
        (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0;

    private static bool IsAvoidanceOutcome(in CombatWireObservation observation) =>
        observation.Damage <= 0 &&
        (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0 &&
        Math.Max(observation.HitCount, observation.AttemptCount) > 0;
}
