using System;

namespace Cloris.Aion2Flow.Resources;

[Flags]
public enum SkillSemantics : ushort
{
    None = 0,
    Damage = 1 << 0,
    PeriodicDamage = 1 << 1,
    Healing = 1 << 2,
    PeriodicHealing = 1 << 3,
    DrainOrAbsorb = 1 << 4,
    ShieldOrBarrier = 1 << 5,
    Support = 1 << 6,
    NonHealthResourceRestore = 1 << 7
}
