namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillEffectReference(int SkillId, int Slot, int EffectId, int EffectDataId, int AuxEffectId)
{
    public bool References(int code) => code > 0 && (EffectId == code || EffectDataId == code || AuxEffectId == code);
}
