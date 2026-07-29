using Avalonia;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class DurationBlockTests
{
    private static readonly Size InfiniteSize =
        new(double.PositiveInfinity, double.PositiveInfinity);

    public DurationBlockTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Theory]
    [InlineData(0d, "0.0s")]
    [InlineData(68.34d, "68.3s")]
    [InlineData(68.36d, "68.4s")]
    [InlineData(-1d, "0.0s")]
    public void DecimalSeconds_PreservesTheExistingTimerFormat(
        double seconds,
        string expected)
    {
        var block = new DurationBlock
        {
            Duration = TimeSpan.FromSeconds(seconds)
        };

        Assert.Equal(
            expected,
            block.DecimalSecondsBlockForDiagnostics.GetFormattedTextForDiagnostics());
    }

    [Theory]
    [InlineData(0d, "00", ":00")]
    [InlineData(59.999d, "00", ":59")]
    [InlineData(60d, "01", ":00")]
    [InlineData(3601d, "60", ":01")]
    [InlineData(6000d, "100", ":00")]
    [InlineData(-1d, "00", ":00")]
    public void MinutesSeconds_UsesElapsedWholeSecondsAndTotalMinutes(
        double seconds,
        string expectedMinutes,
        string expectedSeconds)
    {
        var block = new DurationBlock
        {
            DisplayFormat = EncounterTimeDisplayFormat.MinutesSeconds,
            Duration = TimeSpan.FromSeconds(seconds)
        };

        Assert.Equal(
            expectedMinutes,
            block.MinutesBlockForDiagnostics.GetFormattedTextForDiagnostics());
        Assert.Equal(
            expectedSeconds,
            block.SecondsBlockForDiagnostics.GetFormattedTextForDiagnostics());
    }

    [Fact]
    public void DurationBlock_RefreshesWhenTheDisplayFormatChanges()
    {
        var block = new DurationBlock
        {
            Duration = TimeSpan.FromSeconds(68.3d)
        };

        Assert.True(block.DecimalSecondsBlockForDiagnostics.IsVisible);
        Assert.Equal(
            "68.3s",
            block.DecimalSecondsBlockForDiagnostics.GetFormattedTextForDiagnostics());

        block.DisplayFormat = EncounterTimeDisplayFormat.MinutesSeconds;

        Assert.False(block.DecimalSecondsBlockForDiagnostics.IsVisible);
        Assert.True(block.MinutesBlockForDiagnostics.IsVisible);
        Assert.True(block.SecondsBlockForDiagnostics.IsVisible);
        Assert.Equal(
            "01",
            block.MinutesBlockForDiagnostics.GetFormattedTextForDiagnostics());
        Assert.Equal(
            ":08",
            block.SecondsBlockForDiagnostics.GetFormattedTextForDiagnostics());
    }

    [Fact]
    public void MinutesSeconds_UsesFormattersInsteadOfPrefixesForZeroPadding()
    {
        var block = new DurationBlock
        {
            DisplayFormat = EncounterTimeDisplayFormat.MinutesSeconds,
            Duration = TimeSpan.FromSeconds(68d)
        };

        Assert.Equal("00", block.MinutesBlockForDiagnostics.Formatter);
        Assert.Null(block.MinutesBlockForDiagnostics.Prefix);
        Assert.Equal("00", block.SecondsBlockForDiagnostics.Formatter);
        Assert.Equal(":", block.SecondsBlockForDiagnostics.Prefix);
    }

    [Fact]
    public void DurationBlock_KeepsItsWidthStableUntilTheEncounterScopeChanges()
    {
        var block = new DurationBlock
        {
            Duration = TimeSpan.FromSeconds(9999.9d),
            StableWidthScopeKey = Guid.NewGuid()
        };
        block.Measure(InfiniteSize);
        var wideSize = block.DesiredSize;

        block.Duration = TimeSpan.FromSeconds(1d);
        block.Measure(InfiniteSize);

        Assert.Equal(wideSize.Width, block.DesiredSize.Width);

        block.StableWidthScopeKey = Guid.NewGuid();
        block.Measure(InfiniteSize);

        Assert.True(block.DesiredSize.Width < wideSize.Width);
    }
}
