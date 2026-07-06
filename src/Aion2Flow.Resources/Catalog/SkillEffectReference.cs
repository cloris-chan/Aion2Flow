namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillEffectReference(int SkillId, int Slot, SkillEffectReferenceKind Kind, int EffectCode)
{
    public bool References(int code) => code > 0 && EffectCode == code;
}

public enum SkillEffectReferenceKind : byte
{
    EffectId = 1,
    EffectDataId = 2,
    AuxEffectId = 3,
    AutoLoadEffectDataId = 4
}
