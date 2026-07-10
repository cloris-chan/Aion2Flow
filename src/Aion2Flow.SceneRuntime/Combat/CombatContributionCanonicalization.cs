namespace Cloris.Aion2Flow.SceneRuntime.Combat;

[Flags]
public enum CombatContributionCanonicalization : ushort
{
    None = 0,
    CompactDirectValue = 1 << 0,
    CompactRecoveryByOpener = 1 << 1,
    CompactRecoveryByInlineGroup = 1 << 2,
    CompactRecoveryBySelfValueGroup = 1 << 3,
    CompactAvoidance = 1 << 4,
    OwnerTargetSummonResource = 1 << 5,
    SystemPeriodicRecoverySeed = 1 << 6,
    SystemPeriodicRecoveryHealing = 1 << 7,
    PeriodicStandaloneDamage = 1 << 8,
    PeriodicStandaloneContinuation = 1 << 9,
    PeriodicContinuationHealing = 1 << 10,
    PeriodicShieldGrant = 1 << 11,
    PeriodicShieldAbsorbed = 1 << 12
}
