namespace Cloris.Aion2Flow.Combat.Classification;

[Flags]
public enum DamageModifiers : ushort
{
    None = 0,
    Back = 1 << 0,
    Block = 1 << 1,
    Parry = 1 << 2,
    Perfect = 1 << 3,
    Smite = 1 << 4,
    Endurance = 1 << 5,
    Regeneration = 1 << 6,
    DefensivePerfect = 1 << 7,
    MultiHit = 1 << 8,
    Critical = 1 << 9,
    Evade = 1 << 10,
    Invincible = 1 << 11
}
