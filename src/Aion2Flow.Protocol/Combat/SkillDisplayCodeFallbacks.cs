namespace Cloris.Aion2Flow.Protocol.Combat;

public static class SkillDisplayCodeFallbacks
{
    public const int MaxFallbackCount = 5;

    public static int WriteFallbackCodes(int skillCode, Span<int> destination)
    {
        if (destination.Length < MaxFallbackCount)
            throw new ArgumentException($"Destination must contain at least {MaxFallbackCount} elements.", nameof(destination));

        var count = 0;
        var variant = SkillVariantInfo.Parse(skillCode);
        if (variant.IsStandardPlayerSkill)
        {
            AddFallbackCode(destination, ref count, skillCode, variant.SpecializationSkillCode);
            AddFallbackCode(destination, ref count, skillCode, variant.BaseSkillCode);
            AddFallbackCode(destination, ref count, skillCode, variant.BaseVariantSkillCode);
        }
        else
        {
            if (TryGetTrailingStateBaseCode(skillCode, out var trailingStateBaseCode))
                AddFallbackCode(destination, ref count, skillCode, trailingStateBaseCode);
        }

        destination[count..].Clear();
        return count;
    }

    private static bool TryGetTrailingStateBaseCode(int skillCode, out int baseCode)
    {
        if (skillCode <= 0 || skillCode % 10 == 0)
        {
            baseCode = 0;
            return false;
        }

        baseCode = skillCode / 10 * 10;
        return baseCode > 0;
    }

    private static void AddFallbackCode(Span<int> destination, ref int count, int skillCode, int fallbackCode)
    {
        if (fallbackCode <= 0 || fallbackCode == skillCode)
            return;

        for (var i = 0; i < count; i++)
        {
            if (destination[i] == fallbackCode)
                return;
        }

        destination[count++] = fallbackCode;
    }
}
