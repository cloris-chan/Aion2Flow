namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SkillVariantInfoTests
{
    [Theory]
    [InlineData(16300130, 16300000, 13, 0b00101, 0)]
    [InlineData(16300243, 16300000, 24, 0b01010, 3)]
    [InlineData(16301450, 16300000, 145, 0b11001, 0)]
    [InlineData(17060003, 17060000, 0, 0, 3)]
    [InlineData(17440047, 17440000, 4, 0b01000, 7)]
    public void Parses_Packet_Skill_Variant_Without_Resource_Data(int skillCode, int baseSkillCode, int specializationDigits, int specializationMask, int variantState)
    {
        var variant = SkillVariantInfo.Parse(skillCode);

        Assert.Equal(skillCode, variant.SkillCode);
        Assert.Equal(baseSkillCode, variant.BaseSkillCode);
        Assert.Equal(specializationDigits, variant.SpecializationDigits);
        Assert.Equal(specializationMask, variant.SpecializationMask);
        Assert.Equal(variantState, variant.VariantState);
    }

    [Theory]
    [InlineData(17040257, 17040250, 17040000, 17040007)]
    [InlineData(17730001, 17730000, 17730000, 17730001)]
    public void Parse_Exposes_Derived_Variant_Codes(
        int skillCode,
        int expectedSpecializationSkillCode,
        int expectedBaseSkillCode,
        int expectedBaseVariantSkillCode)
    {
        var variant = SkillVariantInfo.Parse(skillCode);

        Assert.Equal(expectedSpecializationSkillCode, variant.SpecializationSkillCode);
        Assert.Equal(expectedBaseSkillCode, variant.BaseSkillCode);
        Assert.Equal(expectedBaseVariantSkillCode, variant.BaseVariantSkillCode);
    }

    [Theory]
    [InlineData(1218810)]
    [InlineData(11000001)]
    [InlineData(19010047)]
    public void Parse_Preserves_NonStandard_Skill_Code_As_Opaque(int skillCode)
    {
        var variant = SkillVariantInfo.Parse(skillCode);

        Assert.False(variant.IsStandardPlayerSkill);
        Assert.Equal(skillCode, variant.SkillCode);
        Assert.Equal(skillCode, variant.BaseSkillCode);
        Assert.Equal(0, variant.SpecializationDigits);
        Assert.Equal(0, variant.SpecializationMask);
        Assert.Equal(0, variant.VariantState);
    }
}
