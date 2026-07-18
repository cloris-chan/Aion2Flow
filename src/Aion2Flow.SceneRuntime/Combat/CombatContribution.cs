using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum CombatMetricKind : byte
{
    Damage = 1,
    Healing = 2,
    ShieldGranted = 3,
    ShieldAbsorbed = 4
}

public enum CombatDeliveryKind : byte
{
    Direct = 1,
    Periodic = 2,
    Drain = 3,
    Regeneration = 4,
    Reflect = 5,
    Pool = 6
}

public enum CombatPacketRule : byte
{
    None = 0,
    DirectValue = 1,
    DirectHealthResource = 2,
    DirectManaResource = 3,
    DirectSemantic = 4,
    DirectFallbackDamage = 5,
    CompactDirectValue = 6,
    CompactRecovery = 7,
    CompactAvoidance = 8,
    PeriodicValue = 9,
    PeriodicHealthResource = 10,
    PeriodicManaResource = 11,
    PeriodicSemantic = 12,
    PeriodicFallbackDamage = 13,
    PeriodicRecovery = 14,
    PeriodicShieldGrant = 15,
    PeriodicShieldAbsorbed = 16,
    ActiveSkillInvincible = 17,
    PeriodicLinkInvincible = 18,
    DrainSecondary = 19,
    RegenerationSecondary = 20
}

public enum CombatSemanticMatchKind : byte
{
    None = 0,
    ExactNode = 1,
    UnambiguousSlot = 2
}

public enum CombatResolutionAuthority : byte
{
    Packet = 1,
    SkillSemantic = 2,
    PacketDefault = 3
}

public enum CombatMaterializationKind : byte
{
    Primary = 0,
    CompactAssociated = 1,
    DrainSecondary = 2,
    RegenerationSecondary = 3,
    PeriodicRecovery = 4,
    PeriodicPoolGrant = 5,
    PeriodicPoolAbsorb = 6
}

public enum CombatAssociationKind : byte
{
    None = 0,
    CompactOpener = 1,
    CompactInlineRecoveryGroup = 2,
    CompactSelfValueGroup = 3
}

public enum CombatSuppressionReason : byte
{
    None = 0,
    OwnerTargetSummonResource = 1,
    SystemPeriodicRecoverySeed = 2,
    PeriodicPoolSemanticCandidate = 3
}

public readonly record struct CombatResolutionTrace(
    CombatPacketRule PacketRule,
    CombatSemanticMatchKind SemanticMatch,
    CombatResolutionAuthority Authority,
    CombatMaterializationKind Materialization,
    CombatAssociationKind Association,
    SkillSemanticValue DirectSemantics,
    SkillSemanticValue Semantics,
    ResourceEffectRef ResourceEffectRef,
    SkillSemanticResourceNodeKind ResourceNodeKind,
    int ResourceNodeId,
    int ResourceSkillId,
    int EffectSlot,
    int ResourceCandidateSlotCount)
{
    public bool HasResourceEvidence => ResourceNodeId > 0;

    public static CombatResolutionTrace FromPacket(
        CombatPacketRule packetRule,
        CombatMaterializationKind materialization,
        CombatAssociationKind association) =>
        new(
            packetRule,
            CombatSemanticMatchKind.None,
            CombatResolutionAuthority.Packet,
            materialization,
            association,
            default,
            default,
            default,
            default,
            0,
            0,
            -1,
            0);
}

public readonly record struct CombatContribution(
    CombatMetricKind Metric,
    CombatDeliveryKind Delivery,
    long Amount,
    CombatResolutionTrace Resolution);

public readonly record struct CombatOccurrenceResolution(
    CombatPacketRule PacketRule,
    CombatMaterializationKind Materialization,
    CombatAssociationKind Association,
    CombatSuppressionReason Suppression)
{
    public static CombatOccurrenceResolution Primary => default;

    public CombatOccurrenceResolution Inherit(in CombatOccurrenceResolution previous) => new(
        PacketRule == CombatPacketRule.None ? previous.PacketRule : PacketRule,
        Materialization == CombatMaterializationKind.Primary ? previous.Materialization : Materialization,
        Association == CombatAssociationKind.None ? previous.Association : Association,
        Suppression == CombatSuppressionReason.None ? previous.Suppression : Suppression);
}
