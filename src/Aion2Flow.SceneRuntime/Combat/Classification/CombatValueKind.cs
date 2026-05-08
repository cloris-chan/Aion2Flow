namespace Cloris.Aion2Flow.Combat.Classification;

public enum CombatValueKind : byte
{
    Unknown = 0,
    Damage = 1,
    PeriodicDamage = 2,
    Healing = 3,
    PeriodicHealing = 4,
    DrainDamage = 5,
    DrainHealing = 6,
    Shield = 7,
    Support = 8
}
