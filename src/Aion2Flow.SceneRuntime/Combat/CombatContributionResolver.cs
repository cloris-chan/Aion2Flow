using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatContributionResolver
{
    public static bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatContribution contribution)
    {
        var packet = CombatPacketEvidenceResolver.Evaluate(sourceId, targetId, in observation, in occurrence);
        if (packet.Strength == CombatPacketEvidenceStrength.Proven)
        {
            if (!packet.HasCandidate || !CombatContributionAdmission.IsCandidateAdmissible(sourceId, targetId, packet.Candidate!.Value))
            {
                contribution = default;
                return false;
            }

            return CreateContribution(
                packet.Candidate.Value,
                in packet,
                default,
                CombatResolutionAuthority.Packet,
                occurrence.Materialization,
                occurrence.Association,
                out contribution);
        }

        if (occurrence.Suppression == CombatSuppressionReason.PeriodicPoolSemanticCandidate)
        {
            var poolSemantic = CombatSemanticEvidenceResolver.Evaluate(in observation);
            if (CombatSemanticEvidenceResolver.IsAuthoritativePeriodicShield(in poolSemantic))
            {
                return CreateContribution(
                    poolSemantic.Candidate!.Value,
                    in packet,
                    in poolSemantic,
                    CombatResolutionAuthority.SkillSemantic,
                    occurrence.Materialization,
                    occurrence.Association,
                    out contribution);
            }

            contribution = default;
            return false;
        }

        var semantic = CombatSemanticEvidenceResolver.Evaluate(in observation);
        if (semantic.HasCandidate &&
            semantic.Match != CombatSemanticMatchKind.None &&
            CombatContributionAdmission.IsCandidateAdmissible(sourceId, targetId, semantic.Candidate!.Value))
        {
            return CreateContribution(
                semantic.Candidate.Value,
                in packet,
                in semantic,
                CombatResolutionAuthority.SkillSemantic,
                occurrence.Materialization,
                occurrence.Association,
                out contribution);
        }

        if (packet.Strength == CombatPacketEvidenceStrength.Default &&
            packet.HasCandidate &&
            CombatContributionAdmission.IsCandidateAdmissible(sourceId, targetId, packet.Candidate!.Value))
        {
            return CreateContribution(
                packet.Candidate.Value,
                in packet,
                default,
                CombatResolutionAuthority.PacketDefault,
                occurrence.Materialization,
                occurrence.Association,
                out contribution);
        }

        contribution = default;
        return false;
    }

    public static bool TryResolvePacketOnly(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatContribution contribution)
    {
        var packet = CombatPacketEvidenceResolver.Evaluate(sourceId, targetId, in observation, in occurrence);
        return TryResolvePacketOnly(sourceId, targetId, in observation, in occurrence, in packet, out contribution);
    }

    public static bool TryResolvePacketOnly(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        in CombatPacketEvidence packet,
        out CombatContribution contribution)
    {
        if (CombatContributionAdmission.IsSuppressedBeforeResolution(in occurrence) ||
            packet.Strength == CombatPacketEvidenceStrength.None ||
            !packet.HasCandidate ||
            !CombatContributionAdmission.IsCandidateAdmissible(sourceId, targetId, packet.Candidate!.Value))
        {
            contribution = default;
            return false;
        }

        var hasContribution = CreateContribution(
            packet.Candidate.Value,
            in packet,
            default,
            packet.Strength == CombatPacketEvidenceStrength.Proven
                ? CombatResolutionAuthority.Packet
                : CombatResolutionAuthority.PacketDefault,
            occurrence.Materialization,
            occurrence.Association,
            out contribution);
        if (CombatContributionAdmission.IsOccurrenceAdmissible(in occurrence, hasContribution, in contribution))
            return hasContribution;

        contribution = default;
        return false;
    }

    public static bool TryResolveSemanticOnly(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatContribution contribution)
    {
        var semantic = CombatSemanticEvidenceResolver.Evaluate(in observation);
        return TryResolveSemanticOnly(sourceId, targetId, in observation, in occurrence, in semantic, out contribution);
    }

    public static bool TryResolveSemanticOnly(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        in CombatSemanticEvidence semantic,
        out CombatContribution contribution)
    {
        if (CombatContributionAdmission.IsSuppressedBeforeResolution(in occurrence) ||
            !semantic.HasCandidate ||
            semantic.Match == CombatSemanticMatchKind.None ||
            !CombatContributionAdmission.IsCandidateAdmissible(sourceId, targetId, semantic.Candidate!.Value))
        {
            contribution = default;
            return false;
        }

        var packet = new CombatPacketEvidence(
            CombatPacketEvidenceStrength.None,
            ResolveTracePacketRule(in observation, in occurrence),
            null);
        var hasContribution = CreateContribution(
            semantic.Candidate.Value,
            in packet,
            in semantic,
            CombatResolutionAuthority.SkillSemantic,
            occurrence.Materialization,
            occurrence.Association,
            out contribution);
        if (CombatContributionAdmission.IsOccurrenceAdmissible(in occurrence, hasContribution, in contribution))
            return hasContribution;

        contribution = default;
        return false;
    }

    private static CombatPacketRule ResolveTracePacketRule(
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence)
    {
        if (occurrence.PacketRule != CombatPacketRule.None)
            return occurrence.PacketRule;

        return observation.PeriodicRelation == PeriodicEffectRelation.None
            ? CombatPacketRule.DirectValue
            : CombatPacketRule.PeriodicValue;
    }

    private static bool CreateContribution(
        in CombatContributionCandidate candidate,
        in CombatPacketEvidence packet,
        in CombatSemanticEvidence semantic,
        CombatResolutionAuthority authority,
        CombatMaterializationKind materialization,
        CombatAssociationKind association,
        out CombatContribution contribution)
    {
        var resolution = new CombatResolutionTrace(
            packet.Rule,
            semantic.Match,
            authority,
            materialization,
            association,
            semantic.Resource.DirectSemantics,
            semantic.Resource.Semantics,
            semantic.Resource.RawId == 0 ? default : ResourceEffectRef.FromRaw(semantic.Resource.RawId),
            semantic.Resource.NodeKind,
            semantic.Resource.NodeId,
            semantic.Resource.Slot?.SkillId ?? 0,
            semantic.Resource.Slot?.Slot ?? -1,
            semantic.Resource.CandidateSlotCount);
        contribution = new CombatContribution(candidate.Metric, candidate.Delivery, Math.Max(0, candidate.Amount), resolution);
        return candidate.Amount > 0;
    }
}
