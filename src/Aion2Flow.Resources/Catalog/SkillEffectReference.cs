namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillEffectReference(int SkillId, int Slot, SkillEffectReferenceKind Kind, int EffectCode)
{
    public bool References(int code) => code > 0 && EffectCode == code;
}

public enum SkillEffectReferenceKind : byte
{
    SkillEffectFilterId = 1,
    SkillEffectGroupId = 2,
    ProjectileId = 3,
    ToggleOnAbnormalId = 4
}
