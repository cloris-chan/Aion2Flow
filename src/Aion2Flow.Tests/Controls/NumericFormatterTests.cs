using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class NumericFormatterTests
{
    [Theory]
    [InlineData(12345d, false, false, false, 0, null, null, "12345")]
    [InlineData(12345d, true, false, false, 0, null, null, "12,345")]
    [InlineData(1234.5d, true, false, false, 2, null, null, "1,234.5")]
    [InlineData(-9876.54d, true, false, false, 1, null, null, "-9,876.5")]
    [InlineData(12.34d, false, false, false, 1, null, "%", "12.3%")]
    [InlineData(0.8888d, false, false, true, 2, null, null, "88.88%")]
    [InlineData(0.125d, false, false, true, 1, null, " of total", "12.5% of total")]
    public void FormatsFixedPointValues(
        double value,
        bool useGrouping,
        bool useCompactNotation,
        bool usePercentageNotation,
        int fractionDigits,
        string? prefix,
        string? suffix,
        string expected)
    {
        Span<char> buffer = stackalloc char[64];
        var options = new NumericFormatOptions(
            FractionDigits: fractionDigits,
            TrimTrailingZeros: true,
            UseGrouping: useGrouping,
            UseCompactNotation: useCompactNotation,
            UsePercentageNotation: usePercentageNotation,
            CompactThreshold: 1000d,
            CompactSignificantDigits: 3,
            Prefix: prefix,
            Suffix: suffix);

        var result = NumericFormatter.TryFormat(value, buffer, options, out var charsWritten);

        Assert.True(result);
        Assert.Equal(expected, buffer[..charsWritten].ToString());
    }

    [Theory]
    [InlineData(1120d, "1.12k")]
    [InlineData(23_700_000d, "23.7m")]
    [InlineData(999_950d, "1m")]
    [InlineData(-1_250_000d, "-1.25m")]
    public void FormatsCompactValues(double value, string expected)
    {
        Span<char> buffer = stackalloc char[64];
        var options = new NumericFormatOptions(
            FractionDigits: 0,
            TrimTrailingZeros: true,
            UseGrouping: false,
            UseCompactNotation: true,
            UsePercentageNotation: false,
            CompactThreshold: 1000d,
            CompactSignificantDigits: 3,
            Prefix: null,
            Suffix: null);

        var result = NumericFormatter.TryFormat(value, buffer, options, out var charsWritten);

        Assert.True(result);
        Assert.Equal(expected, buffer[..charsWritten].ToString());
    }
}
