namespace Cloris.Aion2Flow.SceneRuntime.Combat;

internal static class CombatContributionAdmission
{
    public static bool IsCandidateAdmissible(
        int sourceId,
        int targetId,
        in CombatContributionCandidate candidate) =>
        candidate.IsPositive &&
        (candidate.Metric != CombatMetricKind.Damage || sourceId <= 0 || sourceId != targetId);

    public static bool IsSuppressedBeforeResolution(in CombatOccurrenceResolution occurrence) =>
        occurrence.Suppression == CombatSuppressionReason.SystemPeriodicRecoverySeed;

    public static bool IsOccurrenceAdmissible(
        in CombatOccurrenceResolution occurrence,
        bool hasContribution,
        in CombatContribution contribution) =>
        occurrence.Suppression switch
        {
            CombatSuppressionReason.SystemPeriodicRecoverySeed => false,
            CombatSuppressionReason.OwnerTargetSummonResource =>
                IsAuthoritativeSemanticContribution(hasContribution, in contribution),
            CombatSuppressionReason.PeriodicPoolSemanticCandidate =>
                IsAuthoritativePeriodicPoolGrant(hasContribution, in contribution),
            CombatSuppressionReason.PeriodicPoolClosed => false,
            _ => true
        };

    private static bool IsAuthoritativeSemanticContribution(
        bool hasContribution,
        in CombatContribution contribution) =>
        hasContribution &&
        contribution.Resolution.Authority == CombatResolutionAuthority.SkillSemantic &&
        contribution.Resolution.SemanticMatch is CombatSemanticMatchKind.ExactNode or CombatSemanticMatchKind.UnambiguousSlot;

    private static bool IsAuthoritativePeriodicPoolGrant(
        bool hasContribution,
        in CombatContribution contribution) =>
        IsAuthoritativeSemanticContribution(hasContribution, in contribution) &&
        contribution.Metric == CombatMetricKind.ShieldGranted &&
        contribution.Delivery == CombatDeliveryKind.Pool;
}
