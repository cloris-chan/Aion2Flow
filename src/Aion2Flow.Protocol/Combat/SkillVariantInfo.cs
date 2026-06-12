namespace Cloris.Aion2Flow.Protocol.Combat;

public readonly record struct SkillVariantInfo(int SkillCode, int BaseSkillCode, int SpecializationDigits, int SpecializationMask, int VariantState)
{
    public bool HasSpecialization => SpecializationMask != 0;
    public bool HasVariantState => VariantState != 0;
    public int SpecializationSkillCode => BaseSkillCode + EncodeVariantSuffix(SpecializationMask, 0);
    public int BaseVariantSkillCode => BaseSkillCode + VariantState;

    public void WriteDisplayFallbackCodes(Span<int> destination)
    {
        if (destination.Length < 3)
            throw new ArgumentException("Destination must contain at least three elements.", nameof(destination));

        destination[0] = SpecializationSkillCode;
        destination[1] = BaseSkillCode;
        destination[2] = BaseVariantSkillCode;
    }

    public static SkillVariantInfo Parse(int skillCode)
    {
        if (skillCode <= 0)
            return default;

        var variantState = skillCode % 10;
        var specializationDigits = (skillCode / 10) % 1000;
        var specializationMask = 0;
        var remainingDigits = specializationDigits;

        while (remainingDigits > 0)
        {
            var digit = remainingDigits % 10;
            remainingDigits /= 10;
            if (digit is >= 1 and <= 5)
                specializationMask |= 1 << (digit - 1);
        }

        var baseSkillCode = skillCode - (skillCode % 10000);
        return new SkillVariantInfo(skillCode, baseSkillCode, specializationDigits, specializationMask, variantState);
    }

    private static int EncodeVariantSuffix(int specializationMask, int variantState)
    {
        var suffix = 0;
        for (var specialization = 1; specialization <= 5; specialization++)
        {
            var bit = 1 << (specialization - 1);
            if ((specializationMask & bit) != 0)
                suffix = (suffix * 10) + specialization;
        }

        return (suffix * 10) + variantState;
    }
}
