using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatOccurrenceMaterializer
{
    public static CombatOccurrenceMaterialization Resolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        if (CombatContributionAdmission.IsSuppressedBeforeResolution(in occurrence))
            return default;

        var hasContribution = CombatContributionResolver.TryResolve(
            sourceId,
            targetId,
            in observation,
            in occurrence,
            out var contribution);

        return Resolve(sourceId, targetId, in observation, in occurrence, hasContribution, in contribution);
    }

    internal static CombatOccurrenceMaterialization Resolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        CombatContributionPathResolver pathResolver)
    {
        if (CombatContributionAdmission.IsSuppressedBeforeResolution(in occurrence))
            return default;

        var hasContribution = pathResolver.TryResolve(
            sourceId,
            targetId,
            in observation,
            in occurrence,
            out var contribution,
            out var suppressedOccurrence);
        if (suppressedOccurrence)
            return default;

        return Resolve(sourceId, targetId, in observation, in occurrence, hasContribution, in contribution);
    }

    private static CombatOccurrenceMaterialization Resolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        bool hasContribution,
        in CombatContribution contribution)
    {
        if (!CombatContributionAdmission.IsOccurrenceAdmissible(in occurrence, hasContribution, in contribution))
            return default;

        var hasMechanic = TryResolveMechanic(
            in observation,
            in occurrence,
            hasContribution,
            in contribution,
            out var mechanic);
        var hasResource = TryResolveResource(
            in observation,
            in occurrence,
            hasContribution,
            in contribution,
            out var resource);

        return new CombatOccurrenceMaterialization(
            CombatOccurrenceDisposition.Admitted,
            hasContribution ? contribution : null,
            hasMechanic ? mechanic : null,
            hasResource ? resource : null);
    }

    private static bool TryResolveMechanic(
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        bool hasContribution,
        in CombatContribution contribution,
        out CombatMechanicOccurrence mechanic)
    {
        var isDamageOccurrence = hasContribution && contribution.Metric == CombatMetricKind.Damage ||
                                 occurrence.PacketRule is CombatPacketRule.CompactAvoidance or CombatPacketRule.ActiveSkillInvincible or CombatPacketRule.PeriodicLinkInvincible ||
                                 IsAvoidanceOutcome(in observation);
        if (!isDamageOccurrence)
        {
            mechanic = default;
            return false;
        }

        var hitCount = Math.Max(0, observation.HitCount);
        var attemptCount = Math.Max(hitCount, Math.Max(0, observation.AttemptCount));
        var modifiers = observation.Modifiers;
        var multiHitCount = (modifiers & DamageModifiers.MultiHit) != 0 ? 1 : 0;
        var resolution = hasContribution
            ? contribution.Resolution
            : CombatResolutionTrace.FromPacket(
                ResolveMechanicPacketRule(in observation, in occurrence),
                occurrence.Materialization,
                occurrence.Association);
        mechanic = new CombatMechanicOccurrence(
            modifiers,
            hitCount,
            attemptCount,
            (modifiers & DamageModifiers.Evade) != 0 ? attemptCount : 0,
            (modifiers & DamageModifiers.Invincible) != 0 ? attemptCount : 0,
            multiHitCount,
            Math.Max(0, observation.MultiHitCount),
            resolution);
        return mechanic.HasFacts;
    }

    private static bool TryResolveResource(
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        bool hasContribution,
        in CombatContribution contribution,
        out CombatResourceOccurrence resource)
    {
        if (observation.ResourceKind == CombatResourceKind.Unknown)
        {
            resource = default;
            return false;
        }

        var delivery = observation.PeriodicRelation == PeriodicEffectRelation.None ||
                       CombatWireTraits.IsPeriodicTargetInitialEffect(in observation)
            ? CombatResourceDeliveryKind.Direct
            : CombatResourceDeliveryKind.Periodic;
        var packetRule = (observation.ResourceKind, delivery) switch
        {
            (CombatResourceKind.Health, CombatResourceDeliveryKind.Direct) => CombatPacketRule.DirectHealthResource,
            (CombatResourceKind.Health, CombatResourceDeliveryKind.Periodic) => CombatPacketRule.PeriodicHealthResource,
            (CombatResourceKind.Mana, CombatResourceDeliveryKind.Direct) => CombatPacketRule.DirectManaResource,
            (CombatResourceKind.Mana, CombatResourceDeliveryKind.Periodic) => CombatPacketRule.PeriodicManaResource,
            _ => CombatPacketRule.None
        };
        var resolution = hasContribution && contribution.Resolution.PacketRule == packetRule
            ? contribution.Resolution
            : CombatResolutionTrace.FromPacket(packetRule, occurrence.Materialization, occurrence.Association);
        resource = new CombatResourceOccurrence(
            observation.ResourceKind,
            observation.ResourceKind == CombatResourceKind.Health
                ? CombatResourceFlowKind.Restore
                : CombatResourceFlowKind.Unknown,
            delivery,
            Math.Max(0, observation.Damage),
            resolution);
        return true;
    }

    private static CombatPacketRule ResolveMechanicPacketRule(
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        if (occurrence.PacketRule != CombatPacketRule.None)
            return occurrence.PacketRule;

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return CombatPacketRule.PeriodicValue;

        return CombatPacketRule.DirectValue;
    }

    private static bool IsAvoidanceOutcome(in CombatWireObservation observation) =>
        observation.Damage <= 0 &&
        (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0 &&
        Math.Max(observation.HitCount, observation.AttemptCount) > 0;
}
