using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class NumericBlockTests
{
    private static readonly Size InfiniteSize = new(double.PositiveInfinity, double.PositiveInfinity);

    public NumericBlockTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Theory]
    [InlineData(12345d, false, null, null, null, "12,345")]
    [InlineData(12345d, false, "0", null, null, "12345")]
    [InlineData(12345d, false, "N0", null, null, "12,345")]
    [InlineData(0.8888d, false, "P2", null, null, "88.88%")]
    [InlineData(1120d, true, "N0", null, null, "1.12k")]
    [InlineData(12.34d, false, "0.0", "[", "]", "[12.3]")]
    [InlineData(1d, false, "00", null, null, "01")]
    [InlineData(double.NaN, false, "P1", null, null, "NaN")]
    [InlineData(double.PositiveInfinity, false, null, null, null, "\u221e")]
    [InlineData(double.NegativeInfinity, false, null, null, null, "-\u221e")]
    [InlineData(1e30d, false, "0", null, null, "1000000000000000000000000000000")]
    public void FormatsCommonValuesThroughNumericBlock(
        double value,
        bool useCompactNotation,
        string? formatter,
        string? prefix,
        string? suffix,
        string expected)
    {
        var block = new NumericBlock
        {
            Value = value,
            Formatter = formatter,
            UseCompactNotation = useCompactNotation,
            Prefix = prefix,
            Suffix = suffix
        };

        Assert.Equal(expected, block.GetFormattedTextForDiagnostics());
    }

    [Fact]
    public void FormatterChangeReformatsAndInvalidatesTheGlyphRun()
    {
        var block = new NumericBlock
        {
            Value = 1d,
            Formatter = "0"
        };
        block.MeasureForDiagnostics(InfiniteSize);
        block.ResetDiagnostics();

        block.Formatter = "00";
        block.MeasureForDiagnostics(InfiniteSize);

        var diagnostics = block.GetDiagnostics();
        Assert.Equal("01", block.GetFormattedTextForDiagnostics());
        Assert.Equal(1, diagnostics.GlyphRebuildCount);
        Assert.True(diagnostics.MeasureInvalidationCount > 0);
        Assert.Equal(1, diagnostics.VisualInvalidationCount);
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

    [Fact]
    public void ApplicationTextRenderingPolicy_UsesUnhintedGrayscaleRendering()
    {
        var options = AppTextRenderingPolicy.ApplicationOptions;

        Assert.Equal(TextRenderingMode.Antialias, options.TextRenderingMode);
        Assert.Equal(TextHintingMode.None, options.TextHintingMode);
        Assert.Equal(BaselinePixelAlignment.Unaligned, options.BaselinePixelAlignment);
    }

    [Fact]
    public void ApplicationTextRenderingPolicy_CreatesTopLevelStyleWithApplicationOptions()
    {
        var style = AppTextRenderingPolicy.CreateTopLevelStyle();
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));

        Assert.Equal(AppTextRenderingPolicy.OptionsProperty, setter.Property);
        Assert.Equal(AppTextRenderingPolicy.ApplicationOptions, setter.Value);
    }

}
