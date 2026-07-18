using Cloris.Aion2Flow.Protocol.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum CombatResourceFlowKind : byte
{
    Unknown = 0,
    Restore = 1,
    Spend = 2
}

public enum CombatResourceDeliveryKind : byte
{
    Direct = 1,
    Periodic = 2
}

public enum CombatOccurrenceDisposition : byte
{
    Suppressed = 0,
    Admitted = 1
}

public readonly record struct CombatMechanicOccurrence(
    DamageModifiers Modifiers,
    int HitCount,
    int AttemptCount,
    int EvadeCount,
    int InvincibleCount,
    int MultiHitCount,
    int MultiHitSubCount,
    CombatResolutionTrace Resolution)
{
    public bool HasFacts =>
        Modifiers != DamageModifiers.None ||
        HitCount > 0 ||
        AttemptCount > 0 ||
        EvadeCount > 0 ||
        InvincibleCount > 0 ||
        MultiHitCount > 0 ||
        MultiHitSubCount > 0;
}

public readonly record struct CombatResourceOccurrence(
    CombatResourceKind Resource,
    CombatResourceFlowKind Flow,
    CombatResourceDeliveryKind Delivery,
    long Amount,
    CombatResolutionTrace Resolution);

public readonly record struct CombatOccurrenceMaterialization(
    CombatOccurrenceDisposition Disposition,
    CombatContribution? Contribution,
    CombatMechanicOccurrence? Mechanic,
    CombatResourceOccurrence? Resource)
{
    public bool IsAdmitted => Disposition == CombatOccurrenceDisposition.Admitted;
    public bool HasAny => Contribution.HasValue || Mechanic.HasValue || Resource.HasValue;
}
