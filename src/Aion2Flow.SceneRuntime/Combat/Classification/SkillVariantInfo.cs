namespace Cloris.Aion2Flow.Combat.Classification;

public readonly record struct SkillVariantInfo(
    int OriginalSkillCode,
    int NormalizedSkillCode,
    int BaseSkillCode,
    int ChargeStage,
    int SpecializationMask)
{
    public bool HasCharge => ChargeStage > 0;
    public bool HasSpecialization => SpecializationMask != 0;
}
