using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class NumericFormatterTests
{
    [Theory]
    [InlineData(12345d, null, null, null, "12,345")]
    [InlineData(12345d, "0", null, null, "12345")]
    [InlineData(12345d, "N0", null, null, "12,345")]
    [InlineData(1234.5d, "#,##0.#", null, null, "1,234.5")]
    [InlineData(-9876.54d, "N1", null, null, "-9,876.5")]
    [InlineData(12.34d, "0.0", null, "%", "12.3%")]
    [InlineData(0.8888d, "P2", null, null, "88.88%")]
    [InlineData(0.125d, "P1", null, " of total", "12.5% of total")]
    [InlineData(1d, "00", null, null, "01")]
    public void FormatsValuesWithTheSuppliedFormatter(
        double value,
        string? formatter,
        string? prefix,
        string? suffix,
        string expected)
    {
        Span<char> buffer = stackalloc char[64];
        var options = new NumericFormatOptions(
            UseCompactNotation: false,
            CompactThreshold: 1000d,
            CompactSignificantDigits: 3,
            Formatter: formatter,
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
            UseCompactNotation: true,
            CompactThreshold: 1000d,
            CompactSignificantDigits: 3,
            Formatter: "N0",
            Prefix: null,
            Suffix: null);

        var result = NumericFormatter.TryFormat(value, buffer, options, out var charsWritten);

        Assert.True(result);
        Assert.Equal(expected, buffer[..charsWritten].ToString());
    }

    [Fact]
    public void CompactThresholdBelowFirstUnitPreservesFormatterBelowOneThousand()
    {
        Span<char> buffer = stackalloc char[64];
        var options = new NumericFormatOptions(
            UseCompactNotation: true,
            CompactThreshold: 100d,
            CompactSignificantDigits: 3,
            Formatter: "N1",
            Prefix: null,
            Suffix: null);

        var result = NumericFormatter.TryFormat(123.4d, buffer, options, out var charsWritten);

        Assert.True(result);
        Assert.Equal("123.4", buffer[..charsWritten].ToString());
    }
}
