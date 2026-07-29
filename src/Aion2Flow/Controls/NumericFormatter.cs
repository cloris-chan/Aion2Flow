using System.Globalization;
using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.Controls;

internal readonly record struct NumericFormatOptions(
    bool UseCompactNotation,
    double CompactThreshold,
    int CompactSignificantDigits,
    string? Formatter,
    string? Prefix,
    string? Suffix)
{
    public int NormalizedCompactSignificantDigits =>
        Math.Clamp(
            CompactSignificantDigits,
            1,
            NumericFormatter.MaxCompactSignificantDigits);

    public double NormalizedCompactThreshold => CompactThreshold >= 1000d ? CompactThreshold : 1000d;
}

internal static class NumericFormatter
{
    internal const int MaxCompactSignificantDigits = 6;

    private const string DefaultFormatter = "N0";
    private static readonly NumberFormatInfo FormatProvider = CreateFormatProvider();

    private static readonly CompactUnit[] CompactUnits =
    [
        new(1_000d, 'k'),
        new(1_000_000d, 'm'),
        new(1_000_000_000d, 'b'),
        new(1_000_000_000_000d, 't'),
        new(1_000_000_000_000_000d, 'q')
    ];

    public static bool TryFormat(double value, Span<char> destination, in NumericFormatOptions options, out int charsWritten)
    {
        charsWritten = 0;

        if (!TryAppendText(options.Prefix, destination, ref charsWritten))
        {
            return false;
        }

        if (!TryFormatCore(value, destination[charsWritten..], options, out var valueCharsWritten))
        {
            return false;
        }

        charsWritten += valueCharsWritten;

        if (!TryAppendText(options.Suffix, destination, ref charsWritten))
        {
            return false;
        }

        return true;
    }

    private static bool TryFormatCore(double value, Span<char> destination, in NumericFormatOptions options, out int charsWritten)
    {
        charsWritten = 0;
        if (!double.IsFinite(value) ||
            !options.UseCompactNotation ||
            Math.Abs(value) < options.NormalizedCompactThreshold)
        {
            var formatter = options.Formatter ?? DefaultFormatter;
            return value.TryFormat(
                destination,
                out charsWritten,
                formatter,
                FormatProvider);
        }

        var compactValue = value;
        ApplyCompactNotation(
            ref compactValue,
            out var fractionDigits,
            out var compactSuffix,
            options.NormalizedCompactSignificantDigits);

        Span<char> formatterBuffer =
            stackalloc char[2 + MaxCompactSignificantDigits];
        var formatterLength = BuildCompactFormatter(
            fractionDigits,
            formatterBuffer);
        if (!compactValue.TryFormat(
                destination,
                out charsWritten,
                formatterBuffer[..formatterLength],
                FormatProvider))
        {
            return false;
        }

        if (compactSuffix != '\0')
        {
            if ((uint)charsWritten >= (uint)destination.Length)
            {
                return false;
            }

            destination[charsWritten++] = compactSuffix;
        }

        return true;
    }

    private static int BuildCompactFormatter(
        int fractionDigits,
        Span<char> destination)
    {
        destination[0] = '0';
        if (fractionDigits <= 0)
        {
            return 1;
        }

        destination[1] = '.';
        destination.Slice(2, fractionDigits).Fill('#');
        return fractionDigits + 2;
    }

    private static void ApplyCompactNotation(
        ref double value,
        out int fractionDigits,
        out char compactSuffix,
        int significantDigits)
    {
        fractionDigits = 0;
        compactSuffix = '\0';
        var absoluteValue = Math.Abs(value);
        var unitIndex = SelectCompactUnitIndex(absoluteValue);

        if (unitIndex < 0)
        {
            return;
        }

        while (true)
        {
            var unit = CompactUnits[unitIndex];
            var scaledValue = value / unit.Scale;
            var scaledAbs = Math.Abs(scaledValue);
            var integerDigits = CountIntegerDigits(scaledAbs);
            fractionDigits = Math.Max(0, significantDigits - integerDigits);
            var roundedScaledValue = Math.Round(scaledValue, fractionDigits, MidpointRounding.AwayFromZero);

            if (Math.Abs(roundedScaledValue) >= 1000d && unitIndex < CompactUnits.Length - 1)
            {
                unitIndex++;
                continue;
            }

            value = roundedScaledValue;
            compactSuffix = unit.Suffix;
            return;
        }
    }

    private static int SelectCompactUnitIndex(double absoluteValue)
    {
        for (var index = CompactUnits.Length - 1; index >= 0; index--)
        {
            if (absoluteValue >= CompactUnits[index].Scale)
            {
                return index;
            }
        }

        return -1;
    }

    private static NumberFormatInfo CreateFormatProvider()
    {
        var numberFormat =
            (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numberFormat.PositiveInfinitySymbol = "\u221e";
        numberFormat.NegativeInfinitySymbol = "-\u221e";
        numberFormat.PercentPositivePattern = 1;
        numberFormat.PercentNegativePattern = 1;
        return NumberFormatInfo.ReadOnly(numberFormat);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountIntegerDigits(double value)
    {
        if (value < 1d)
        {
            return 1;
        }

        if (value < 10d)
        {
            return 1;
        }

        if (value < 100d)
        {
            return 2;
        }

        if (value < 1000d)
        {
            return 3;
        }

        return (int)Math.Floor(Math.Log10(value)) + 1;
    }

    private static bool TryAppendText(string? text, Span<char> destination, ref int charsWritten)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var span = text.AsSpan();
        if (span.Length > destination.Length - charsWritten)
        {
            return false;
        }

        span.CopyTo(destination[charsWritten..]);
        charsWritten += span.Length;
        return true;
    }

    private readonly record struct CompactUnit(double Scale, char Suffix);
}
