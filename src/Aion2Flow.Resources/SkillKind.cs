namespace Cloris.Aion2Flow.Resources;

public enum SkillKind : byte
{
    Unknown = 0,
    Damage = 1,
    PeriodicDamage = 2,
    Healing = 3,
    PeriodicHealing = 4,
    DrainOrAbsorb = 5,
    ShieldOrBarrier = 6,
    Support = 7
}
