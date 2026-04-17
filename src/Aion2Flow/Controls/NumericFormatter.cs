using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.Controls;

internal readonly record struct NumericFormatOptions(
    int FractionDigits,
    bool TrimTrailingZeros,
    bool UseGrouping,
    bool UseCompactNotation,
    bool UsePercentageNotation,
    double CompactThreshold,
    int CompactSignificantDigits,
    string? Prefix,
    string? Suffix)
{
    public int NormalizedFractionDigits => Math.Clamp(FractionDigits, 0, NumericFormatter.MaxFractionDigits);
    public int NormalizedCompactSignificantDigits => Math.Clamp(CompactSignificantDigits, 1, NumericFormatter.MaxFractionDigits);
    public double NormalizedCompactThreshold => CompactThreshold > 0 ? CompactThreshold : 1000d;
}

internal static class NumericFormatter
{
    internal const int MaxFractionDigits = 6;

    private static readonly ulong[] Pow10 =
    [
        1UL,
        10UL,
        100UL,
        1_000UL,
        10_000UL,
        100_000UL,
        1_000_000UL
    ];

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
        var formatValue = options.UsePercentageNotation ? value * 100d : value;

        if (double.IsNaN(formatValue))
        {
            if (!TryCopyLiteral("NaN".AsSpan(), destination, out charsWritten))
            {
                return false;
            }

            return TryAppendPercentageSuffix(destination, ref charsWritten, options.UsePercentageNotation);
        }

        if (double.IsPositiveInfinity(formatValue))
        {
            if (!TryCopyLiteral("\u221e".AsSpan(), destination, out charsWritten))
            {
                return false;
            }

            return TryAppendPercentageSuffix(destination, ref charsWritten, options.UsePercentageNotation);
        }

        if (double.IsNegativeInfinity(formatValue))
        {
            if (!TryCopyLiteral("-\u221e".AsSpan(), destination, out charsWritten))
            {
                return false;
            }

            return TryAppendPercentageSuffix(destination, ref charsWritten, options.UsePercentageNotation);
        }

        char compactSuffix = '\0';
        var fractionDigits = options.NormalizedFractionDigits;
        var useGrouping = options.UseGrouping;

        if (options.UseCompactNotation && Math.Abs(formatValue) >= options.NormalizedCompactThreshold)
        {
            ApplyCompactNotation(ref formatValue, ref fractionDigits, out compactSuffix, options.NormalizedCompactSignificantDigits);
            useGrouping = false;
        }

        if (!TryWriteFixedPoint(formatValue, fractionDigits, useGrouping, options.TrimTrailingZeros, destination, out charsWritten))
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

        return TryAppendPercentageSuffix(destination, ref charsWritten, options.UsePercentageNotation);
    }

    private static bool TryWriteFixedPoint(
        double value,
        int fractionDigits,
        bool useGrouping,
        bool trimTrailingZeros,
        Span<char> destination,
        out int charsWritten)
    {
        charsWritten = 0;
        var isNegative = value < 0;
        var absoluteValue = Math.Abs(value);
        var scale = Pow10[fractionDigits];
        var scaledValue = Math.Round(absoluteValue * scale, MidpointRounding.AwayFromZero);

        if (scaledValue > ulong.MaxValue)
        {
            return TryCopyLiteral(isNegative ? "-OVF".AsSpan() : "OVF".AsSpan(), destination, out charsWritten);
        }

        var scaledInteger = (ulong)scaledValue;
        if (scaledInteger == 0)
        {
            isNegative = false;
        }

        var integerPart = scaledInteger / scale;
        var fractionalPart = scaledInteger % scale;

        if (isNegative)
        {
            if ((uint)charsWritten >= (uint)destination.Length)
            {
                return false;
            }

            destination[charsWritten++] = '-';
        }

        if (!TryWriteUnsignedInteger(integerPart, useGrouping, destination[charsWritten..], out var integerCharsWritten))
        {
            return false;
        }

        charsWritten += integerCharsWritten;

        if (fractionDigits <= 0)
        {
            return true;
        }

        var fractionalDigitsToWrite = fractionDigits;
        if (trimTrailingZeros)
        {
            while (fractionalDigitsToWrite > 0 && fractionalPart % 10 == 0)
            {
                fractionalPart /= 10;
                fractionalDigitsToWrite--;
            }
        }

        if (fractionalDigitsToWrite == 0)
        {
            return true;
        }

        if ((uint)charsWritten >= (uint)destination.Length)
        {
            return false;
        }

        destination[charsWritten++] = '.';

        if (!TryWriteFractionalPart(fractionalPart, fractionalDigitsToWrite, destination[charsWritten..], out var fractionCharsWritten))
        {
            return false;
        }

        charsWritten += fractionCharsWritten;
        return true;
    }

    private static bool TryWriteUnsignedInteger(ulong value, bool useGrouping, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        Span<char> reversedDigits = stackalloc char[32];
        var digitCount = 0;

        do
        {
            reversedDigits[digitCount++] = (char)('0' + (value % 10));
            value /= 10;
        }
        while (value > 0);

        var requiredLength = digitCount;
        if (useGrouping)
        {
            requiredLength += (digitCount - 1) / 3;
        }

        if (requiredLength > destination.Length)
        {
            return false;
        }

        var destinationIndex = 0;
        for (var sourceIndex = digitCount - 1; sourceIndex >= 0; sourceIndex--)
        {
            destination[destinationIndex++] = reversedDigits[sourceIndex];

            if (useGrouping && sourceIndex > 0 && sourceIndex % 3 == 0)
            {
                destination[destinationIndex++] = ',';
            }
        }

        charsWritten = destinationIndex;
        return true;
    }

    private static bool TryWriteFractionalPart(ulong value, int digits, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (digits > destination.Length)
        {
            return false;
        }

        for (var index = digits - 1; index >= 0; index--)
        {
            destination[index] = (char)('0' + (value % 10));
            value /= 10;
        }

        charsWritten = digits;
        return true;
    }

    private static bool TryAppendPercentageSuffix(Span<char> destination, ref int charsWritten, bool usePercentageNotation)
    {
        if (!usePercentageNotation)
        {
            return true;
        }

        if ((uint)charsWritten >= (uint)destination.Length)
        {
            return false;
        }

        destination[charsWritten++] = '%';
        return true;
    }

    private static void ApplyCompactNotation(ref double value, ref int fractionDigits, out char compactSuffix, int significantDigits)
    {
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

    private static bool TryCopyLiteral(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (source.Length > destination.Length)
        {
            return false;
        }

        source.CopyTo(destination);
        charsWritten = source.Length;
        return true;
    }

    private readonly record struct CompactUnit(double Scale, char Suffix);
}
