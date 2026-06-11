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
}
