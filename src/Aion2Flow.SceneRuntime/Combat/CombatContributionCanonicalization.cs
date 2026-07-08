namespace Cloris.Aion2Flow.SceneRuntime.Combat;

[Flags]
public enum CombatContributionCanonicalization : ushort
{
    None = 0,
    CompactDirectValue = 1 << 0,
    CompactRecoveryByOpener = 1 << 1,
    CompactRecoveryByInlineGroup = 1 << 2,
    CompactAvoidance = 1 << 3,
    OwnerTargetSummonResource = 1 << 4,
    SystemPeriodicRecoverySeed = 1 << 5,
    SystemPeriodicRecoveryHealing = 1 << 6,
    PeriodicStandaloneDamage = 1 << 7,
    PeriodicStandaloneContinuation = 1 << 8,
    PeriodicContinuationHealing = 1 << 9,
    PeriodicShieldGrant = 1 << 10,
    PeriodicShieldAbsorbed = 1 << 11
}
