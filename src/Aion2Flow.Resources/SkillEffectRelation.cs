namespace Cloris.Aion2Flow.Resources;

public readonly record struct SkillEffectRelation(int SkillId, int Slot, int EffectId, int EffectDataId, int AuxEffectId)
{
    public bool References(int code) => code > 0 && (EffectId == code || EffectDataId == code || AuxEffectId == code);
}
