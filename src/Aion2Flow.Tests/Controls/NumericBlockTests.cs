using System.Reflection;
using Avalonia;
using Avalonia.Threading;
using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class NumericBlockTests
{
    private static readonly Size InfiniteSize = new(double.PositiveInfinity, double.PositiveInfinity);
    private static readonly Lock s_avaloniaGate = new();
    private static bool s_avaloniaInitialized;

    public NumericBlockTests()
    {
        EnsureAvalonia();
    }

    [Theory]
    [InlineData(12345d, false, false, false, 0, null, null, "12345")]
    [InlineData(12345d, true, false, false, 0, null, null, "12,345")]
    [InlineData(0.8888d, false, false, true, 2, null, null, "88.88%")]
    [InlineData(1120d, false, true, false, 0, null, null, "1.12k")]
    [InlineData(12.34d, false, false, false, 1, "[", "]", "[12.3]")]
    [InlineData(double.NaN, false, false, false, 0, null, null, "NaN")]
    [InlineData(double.PositiveInfinity, false, false, false, 0, null, null, "\u221e")]
    [InlineData(double.NegativeInfinity, false, false, false, 0, null, null, "-\u221e")]
    [InlineData(1e30d, false, false, false, 0, null, null, "OVF")]
    public void FormatsCommonValuesThroughNumericBlock(
        double value,
        bool useGrouping,
        bool useCompactNotation,
        bool usePercentageNotation,
        int fractionDigits,
        string? prefix,
        string? suffix,
        string expected)
    {
        var block = new NumericBlock
        {
            Value = value,
            FractionDigits = fractionDigits,
            UseGrouping = useGrouping,
            UseCompactNotation = useCompactNotation,
            UsePercentageNotation = usePercentageNotation,
            Prefix = prefix,
            Suffix = suffix
        };

        Assert.Equal(expected, block.GetFormattedTextForDiagnostics());
    }

    [Fact]
    public void SameFormattedTextSkipsGlyphRebuildAndInvalidation()
    {
        var block = new NumericBlock
        {
            Value = 123_100d,
            UseCompactNotation = true,
            CompactSignificantDigits = 3
        };

        block.MeasureForDiagnostics(InfiniteSize);
        block.ResetDiagnostics();

        block.Value = 123_200d;
        block.Value = 123_300d;
        block.Value = 123_400d;

        var diagnostics = block.GetDiagnostics();
        Assert.Equal("123k", block.GetFormattedTextForDiagnostics());
        Assert.Equal(0, diagnostics.GlyphRebuildCount);
        Assert.Equal(0, diagnostics.MeasureInvalidationCount);
        Assert.Equal(0, diagnostics.VisualInvalidationCount);
        Assert.True(diagnostics.TextFormatCount >= 3);
    }

    [Fact]
    public void DefaultStableWidthDoesNotShrinkOrInvalidateMeasure()
    {
        var block = new NumericBlock { Value = 999_999d };

        var wideSize = block.MeasureForDiagnostics(InfiniteSize);
        block.ResetDiagnostics();

        block.Value = 1d;

        var diagnostics = block.GetDiagnostics();
        var stableSize = block.MeasureForDiagnostics(InfiniteSize);
        Assert.Equal(0, diagnostics.MeasureInvalidationCount);
        Assert.Equal(1, diagnostics.VisualInvalidationCount);
        Assert.Equal(1, diagnostics.GlyphRebuildCount);
        Assert.True(Math.Abs(wideSize.Width - stableSize.Width) < 0.001d);
    }

    [Fact]
    public void StableWidthScopeKeyResetAllowsShrink()
    {
        var block = new NumericBlock { Value = 999_999d };

        var wideSize = block.MeasureForDiagnostics(InfiniteSize);
        block.Value = 1d;
        block.ResetDiagnostics();

        block.StableWidthScopeKey = "next";

        var diagnostics = block.GetDiagnostics();
        var resetSize = block.MeasureForDiagnostics(InfiniteSize);
        Assert.True(diagnostics.MeasureInvalidationCount > 0);
        Assert.True(resetSize.Width < wideSize.Width);
    }

    [Fact]
    public void FixedStableWidthUsesConfiguredDesiredWidth()
    {
        var block = new NumericBlock
        {
            Value = 1d,
            FixedTextWidth = 80d
        };

        var size = block.MeasureForDiagnostics(InfiniteSize);
        Assert.Equal(80d, size.Width);
    }

    [Fact]
    public void ValueChangesReuseCachedTypeface()
    {
        var block = new NumericBlock { Value = 1d };
        block.MeasureForDiagnostics(InfiniteSize);
        block.ResetDiagnostics();

        block.Value = 2d;

        var diagnostics = block.GetDiagnostics();
        Assert.Equal(0, diagnostics.TypefaceResolveCount);
        Assert.Equal(1, diagnostics.GlyphRebuildCount);
    }

    [Fact]
    public void TypefaceCacheIsSharedAcrossNumericBlockInstances()
    {
        NumericBlock.ClearStaticCachesForDiagnostics();

        var first = new NumericBlock { Value = 1d };
        first.MeasureForDiagnostics(InfiniteSize);
        Assert.Equal(1, first.GetDiagnostics().TypefaceResolveCount);

        var second = new NumericBlock { Value = 2d };
        second.MeasureForDiagnostics(InfiniteSize);
        Assert.Equal(0, second.GetDiagnostics().TypefaceResolveCount);
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null || s_avaloniaInitialized)
        {
            return;
        }

        lock (s_avaloniaGate)
        {
            if (Application.Current is not null || s_avaloniaInitialized)
            {
                return;
            }

            typeof(Dispatcher)
                .GetMethod("ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);

            AppBuilder
                .Configure<TestApplication>()
                .UsePlatformDetect()
                .SetupWithoutStarting();

            s_avaloniaInitialized = true;
        }
    }

    private sealed class TestApplication : Application;
}
