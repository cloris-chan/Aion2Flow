namespace Cloris.Aion2Flow.Protocol.Combat;

[Flags]
public enum DamageModifiers : ushort
{
    None = 0,
    Back = 1 << 0,
    Front = 1 << 1,
    Block = 1 << 2,
    Parry = 1 << 3,
    Perfect = 1 << 4,
    Smite = 1 << 5,
    Endurance = 1 << 6,
    Regeneration = 1 << 7,
    DefensivePerfect = 1 << 8,
    MultiHit = 1 << 9,
    Critical = 1 << 10,
    Evade = 1 << 11,
    Invincible = 1 << 12
}
