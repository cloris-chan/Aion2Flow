using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum CombatPacketEvidenceStrength : byte
{
    None = 0,
    Default = 1,
    Proven = 2
}

public readonly record struct CombatContributionCandidate(
    CombatMetricKind Metric,
    CombatDeliveryKind Delivery,
    long Amount)
{
    public bool IsPositive => Amount > 0;
}

public readonly record struct CombatPacketEvidence(
    CombatPacketEvidenceStrength Strength,
    CombatPacketRule Rule,
    CombatContributionCandidate? Candidate)
{
    public bool HasCandidate => Candidate is { IsPositive: true };
}

public readonly record struct CombatSemanticEvidence(
    CombatSemanticMatchKind Match,
    SkillSemanticResourceResolution Resource,
    CombatContributionCandidate? Candidate)
{
    public bool HasResourceEvidence => Resource.NodeId > 0;
    public bool HasCandidate => Candidate is { IsPositive: true };
}

public readonly record struct CombatOccurrenceContext(
    int SourceId,
    int TargetId,
    CombatWireObservation Wire,
    CombatOccurrenceResolution Resolution,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    long FlushId,
    RawPacketReference Raw,
    CombatOccurrenceMaterialization ProductionMaterialization);

public interface ICombatOccurrenceObserver
{
    void Observe(in CombatOccurrenceContext context);
}

public static class CombatPacketEvidenceResolver
{
    public static CombatPacketEvidence Evaluate(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        if (TryResolveExplicitRule(in observation, in occurrence, out var evidence))
            return evidence;

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return EvaluatePeriodic(sourceId, targetId, in observation, in occurrence);

        return EvaluateDirect(sourceId, targetId, in observation, in occurrence);
    }

    private static bool TryResolveExplicitRule(
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatPacketEvidence evidence)
    {
        var rule = occurrence.PacketRule;
        switch (rule)
        {
            case CombatPacketRule.CompactDirectValue:
                evidence = Default(rule, Damage(observation.Damage));
                return true;
            case CombatPacketRule.CompactRecovery:
                evidence = Proven(rule, Healing(observation.Damage));
                return true;
            case CombatPacketRule.CompactAvoidance:
            case CombatPacketRule.ActiveSkillInvincible:
            case CombatPacketRule.PeriodicLinkInvincible:
                evidence = ProvenWithoutCandidate(rule);
                return true;
            case CombatPacketRule.PeriodicRecovery:
                evidence = Proven(rule, PeriodicHealing(observation.Damage));
                return true;
            case CombatPacketRule.PeriodicShieldGrant:
                evidence = Proven(rule, Shield(observation.Damage));
                return true;
            case CombatPacketRule.PeriodicShieldAbsorbed:
                evidence = Proven(rule, ShieldAbsorbed(observation.Damage));
                return true;
            case CombatPacketRule.PeriodicPoolClosed:
                evidence = ProvenWithoutCandidate(rule);
                return true;
            case CombatPacketRule.DrainSecondary:
                evidence = Proven(rule, Drain(observation.Damage));
                return true;
            case CombatPacketRule.RegenerationSecondary:
                evidence = Proven(rule, Regeneration(observation.Damage));
                return true;
            case CombatPacketRule.DirectHealthResource:
                evidence = Proven(rule, Healing(observation.Damage));
                return true;
            case CombatPacketRule.PeriodicHealthResource:
                var delivery = CombatWireTraits.IsPeriodicTargetInitialEffect(in observation)
                    ? CombatDeliveryKind.Direct
                    : CombatDeliveryKind.Periodic;
                evidence = Proven(rule, new CombatContributionCandidate(CombatMetricKind.Healing, delivery, observation.Damage));
                return true;
            case CombatPacketRule.PeriodicValue:
                if (CombatWireTraits.IsPeriodicSelfMode(in observation, 11))
                {
                    evidence = Proven(rule, PeriodicHealing(observation.Damage));
                    return true;
                }

                break;
        }

        evidence = default;
        return false;
    }

    private static CombatPacketEvidence EvaluateDirect(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        var rule = occurrence.PacketRule == CombatPacketRule.None
            ? CombatPacketRule.DirectValue
            : occurrence.PacketRule;

        if (IsAvoidanceOutcome(in observation))
            return ProvenWithoutCandidate(CombatPacketRule.CompactAvoidance);

        if (observation.ResourceKind == CombatResourceKind.Health)
            return Proven(CombatPacketRule.DirectHealthResource, Healing(observation.Damage));

        if (observation.ResourceKind != CombatResourceKind.Unknown)
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);

        if (sourceId > 0 && sourceId == targetId)
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);

        if (!IsQuantifiedShape(in observation))
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);

        return Default(rule, Damage(observation.Damage));
    }

    private static CombatPacketEvidence EvaluatePeriodic(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        var rule = occurrence.PacketRule == CombatPacketRule.None
            ? CombatPacketRule.PeriodicValue
            : occurrence.PacketRule;

        if (observation.ResourceKind == CombatResourceKind.Health)
        {
            var delivery = CombatWireTraits.IsPeriodicTargetInitialEffect(in observation)
                ? CombatDeliveryKind.Direct
                : CombatDeliveryKind.Periodic;
            return Proven(CombatPacketRule.PeriodicHealthResource, new CombatContributionCandidate(CombatMetricKind.Healing, delivery, observation.Damage));
        }

        if (observation.ResourceKind != CombatResourceKind.Unknown ||
            CombatWireTraits.IsPeriodicSelfMode(in observation, 10) ||
            CombatWireTraits.IsPeriodicTargetStateSeed(in observation))
        {
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);
        }

        if (CombatWireTraits.IsPeriodicSelfMode(in observation, 11))
            return Proven(CombatPacketRule.PeriodicValue, PeriodicHealing(observation.Damage));

        if (observation.PeriodicRelation != PeriodicEffectRelation.Target || !IsQuantifiedShape(in observation))
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);

        if (sourceId > 0 && sourceId == targetId)
            return new CombatPacketEvidence(CombatPacketEvidenceStrength.None, rule, null);

        return Default(rule, new CombatContributionCandidate(
            CombatMetricKind.Damage,
            CombatWireTraits.IsPeriodicTargetInitialEffect(in observation) ? CombatDeliveryKind.Direct : CombatDeliveryKind.Periodic,
            observation.Damage));
    }

    private static CombatPacketEvidence Proven(CombatPacketRule rule, CombatContributionCandidate candidate) =>
        new(CombatPacketEvidenceStrength.Proven, rule, candidate);

    private static CombatPacketEvidence ProvenWithoutCandidate(CombatPacketRule rule) =>
        new(CombatPacketEvidenceStrength.Proven, rule, null);

    private static CombatPacketEvidence Default(CombatPacketRule rule, CombatContributionCandidate candidate) =>
        new(CombatPacketEvidenceStrength.Default, rule, candidate);

    private static CombatContributionCandidate Damage(long amount) =>
        new(CombatMetricKind.Damage, CombatDeliveryKind.Direct, amount);

    private static CombatContributionCandidate Healing(long amount) =>
        new(CombatMetricKind.Healing, CombatDeliveryKind.Direct, amount);

    private static CombatContributionCandidate PeriodicHealing(long amount) =>
        new(CombatMetricKind.Healing, CombatDeliveryKind.Periodic, amount);

    private static CombatContributionCandidate Drain(long amount) =>
        new(CombatMetricKind.Healing, CombatDeliveryKind.Drain, amount);

    private static CombatContributionCandidate Regeneration(long amount) =>
        new(CombatMetricKind.Healing, CombatDeliveryKind.Regeneration, amount);

    private static CombatContributionCandidate Shield(long amount) =>
        new(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Pool, amount);

    private static CombatContributionCandidate ShieldAbsorbed(long amount) =>
        new(CombatMetricKind.ShieldAbsorbed, CombatDeliveryKind.Pool, amount);

    private static bool IsQuantifiedShape(in CombatWireObservation observation) =>
        observation.Damage > 0 ||
        Math.Max(observation.HitCount, observation.AttemptCount) > 0 ||
        (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0;

    private static bool IsAvoidanceOutcome(in CombatWireObservation observation) =>
        observation.Damage <= 0 &&
        (observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0 &&
        Math.Max(observation.HitCount, observation.AttemptCount) > 0;
}

public static class CombatSemanticEvidenceResolver
{
    private const SkillQuantifiedFacet DirectFacets =
        SkillQuantifiedFacet.DirectDamage |
        SkillQuantifiedFacet.DirectHealing |
        SkillQuantifiedFacet.Shield;

    private const SkillQuantifiedFacet PeriodicFacets =
        SkillQuantifiedFacet.PeriodicDamage |
        SkillQuantifiedFacet.PeriodicHealing |
        SkillQuantifiedFacet.Shield;

    public static CombatSemanticEvidence Evaluate(in CombatWireObservation observation)
    {
        if (observation.ResourceKind != CombatResourceKind.Unknown)
            return default;

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
        {
            return observation.PeriodicMode == 9
                ? EvaluatePeriodicPoolGrant(in observation)
                : EvaluatePeriodic(in observation);
        }

        if (!CombatResourceRegistry.TryResolveDirectCombatResourceSemantics(in observation, out var resolution))
            return default;

        var match = GetSemanticMatch(in resolution);
        var facets = ResolveDirectSemanticFacets(in resolution);
        return new CombatSemanticEvidence(match, resolution, TryCreateCandidate(facets, periodic: false, observation.Damage));
    }

    private static CombatSemanticEvidence EvaluatePeriodicPoolGrant(in CombatWireObservation observation)
    {
        if (observation.PeriodicMode != 9 ||
            observation.PeriodicRelation == PeriodicEffectRelation.None ||
            observation.ResourceKind != CombatResourceKind.Unknown ||
            !CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var resolution))
        {
            return default;
        }

        var match = GetSemanticMatch(in resolution);
        var facets = resolution.Semantics.QuantifiedFacets & PeriodicFacets;
        CombatContributionCandidate? candidate = facets == SkillQuantifiedFacet.Shield
            ? new CombatContributionCandidate(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Pool, observation.Damage)
            : null;
        return new CombatSemanticEvidence(match, resolution, candidate);
    }

    private static CombatSemanticEvidence EvaluatePeriodic(in CombatWireObservation observation)
    {
        if (CombatWireTraits.IsPeriodicTargetInitialEffect(in observation) ||
            CombatWireTraits.IsPeriodicSelfMode(in observation, 10) ||
            CombatWireTraits.IsPeriodicTargetStateSeed(in observation) ||
            !CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var resolution))
        {
            return default;
        }

        var match = GetSemanticMatch(in resolution);
        var facets = resolution.Semantics.QuantifiedFacets & PeriodicFacets;
        return new CombatSemanticEvidence(match, resolution, TryCreateCandidate(facets, periodic: true, observation.Damage));
    }

    private static CombatContributionCandidate? TryCreateCandidate(
        SkillQuantifiedFacet facets,
        bool periodic,
        long amount)
    {
        if (facets == SkillQuantifiedFacet.DirectDamage || facets == SkillQuantifiedFacet.PeriodicDamage)
        {
            return new CombatContributionCandidate(
                CombatMetricKind.Damage,
                periodic ? CombatDeliveryKind.Periodic : CombatDeliveryKind.Direct,
                amount);
        }

        if (facets == SkillQuantifiedFacet.DirectHealing || facets == SkillQuantifiedFacet.PeriodicHealing)
        {
            return new CombatContributionCandidate(
                CombatMetricKind.Healing,
                periodic ? CombatDeliveryKind.Periodic : CombatDeliveryKind.Direct,
                amount);
        }

        if (facets == SkillQuantifiedFacet.Shield)
        {
            return new CombatContributionCandidate(
                CombatMetricKind.ShieldGranted,
                periodic ? CombatDeliveryKind.Pool : CombatDeliveryKind.Direct,
                amount);
        }

        return null;
    }

    private static CombatSemanticMatchKind GetSemanticMatch(in SkillSemanticResourceResolution resolution)
    {
        if (resolution.NodeId <= 0)
            return CombatSemanticMatchKind.None;

        if (resolution.NodeKind == SkillSemanticResourceNodeKind.SkillEffect &&
            resolution.RawId == unchecked((uint)resolution.NodeId))
        {
            return CombatSemanticMatchKind.ExactNode;
        }

        return resolution.HasUnambiguousSlot
            ? CombatSemanticMatchKind.UnambiguousSlot
            : CombatSemanticMatchKind.None;
    }

    internal static bool IsAuthoritativePeriodicShield(in CombatSemanticEvidence evidence) =>
        evidence.Match != CombatSemanticMatchKind.None &&
        evidence.Candidate is { Metric: CombatMetricKind.ShieldGranted, Delivery: CombatDeliveryKind.Pool };

    private static SkillQuantifiedFacet ResolveDirectSemanticFacets(in SkillSemanticResourceResolution resolution) =>
        (resolution.DirectSemantics.QuantifiedFacets & DirectFacets) |
        (resolution.Semantics.QuantifiedFacets & SkillQuantifiedFacet.Shield);
}
