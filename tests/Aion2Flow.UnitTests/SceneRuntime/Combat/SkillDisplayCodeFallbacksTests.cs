namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SkillDisplayCodeFallbacksTests
{
    [Theory]
    [InlineData(1227237, new[] { 1227230 })]
    [InlineData(1227265, new[] { 1227260 })]
    [InlineData(1607415, new[] { 1607410 })]
    [InlineData(1607400, new int[] { })]
    public void Writes_Display_Fallbacks_For_Seven_Digit_Combat_Codes(int skillCode, int[] expected)
    {
        Span<int> fallbackCodes = stackalloc int[SkillDisplayCodeFallbacks.MaxFallbackCount];

        var count = SkillDisplayCodeFallbacks.WriteFallbackCodes(skillCode, fallbackCodes);

        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, fallbackCodes[..count].ToArray());
    }

    [Theory]
    [InlineData(16300243, new[] { 16300240, 16300000, 16300003 })]
    [InlineData(17440047, new[] { 17440040, 17440000, 17440007 })]
    public void Keeps_Standard_Player_Display_Fallbacks(int skillCode, int[] expected)
    {
        Span<int> fallbackCodes = stackalloc int[SkillDisplayCodeFallbacks.MaxFallbackCount];

        var count = SkillDisplayCodeFallbacks.WriteFallbackCodes(skillCode, fallbackCodes);

        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, fallbackCodes[..count].ToArray());
    }
}
