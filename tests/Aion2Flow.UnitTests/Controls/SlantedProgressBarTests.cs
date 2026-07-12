using Avalonia;
using Avalonia.Media;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SlantedProgressBarTests
{
    public SlantedProgressBarTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Fact]
    public void VisualState_ClampsSegmentAndPreservesBrushOpacity()
    {
        var bar = new SlantedProgressBar
        {
            Segment = new ProgressSegment(2d, new SolidColorBrush(Color.FromArgb(200, 20, 40, 60), 0.5d)),
            SlantWidth = -4d
        };

        var state = bar.CreateVisualStateForDiagnostics();

        Assert.Equal(1f, state.Ratio);
        Assert.Equal(Color.FromArgb(100, 20, 40, 60), state.FillColor);
        Assert.Equal(0f, state.SlantWidth);
    }

    [Fact]
    public void Geometry_CreatesFullHeightParallelSlantedRatioFill()
    {
        var fill = SlantedProgressBarFill.Create(new Size(200d, 10d), 7f, 0.6f);
        var full = SlantedProgressBarFill.Create(new Size(200d, 10d), 7f, 1f);
        var topLeft = fill.Transform.Transform(new Point(fill.LocalBounds.Left, fill.LocalBounds.Top));
        var topRight = fill.Transform.Transform(new Point(fill.LocalBounds.Right, fill.LocalBounds.Top));
        var bottomLeft = fill.Transform.Transform(new Point(fill.LocalBounds.Left, fill.LocalBounds.Bottom));
        var bottomRight = fill.Transform.Transform(new Point(fill.LocalBounds.Right, fill.LocalBounds.Bottom));
        var fullTopLeft = full.Transform.Transform(new Point(full.LocalBounds.Left, full.LocalBounds.Top));
        var fullTopRight = full.Transform.Transform(new Point(full.LocalBounds.Right, full.LocalBounds.Top));
        var fillRatio = (topRight.X - topLeft.X) / (fullTopRight.X - fullTopLeft.X);

        Assert.Equal(-7d, bottomLeft.X - topLeft.X, 4);
        Assert.Equal(10d, bottomLeft.Y - topLeft.Y, 4);
        Assert.Equal(bottomLeft.X - topLeft.X, bottomRight.X - topRight.X, 4);
        Assert.Equal(bottomLeft.Y - topLeft.Y, bottomRight.Y - topRight.Y, 4);
        Assert.Equal(0.6d, fillRatio, 3);
    }

    [Fact]
    public void Geometry_RatioUpdatesDoNotAllocate()
    {
        _ = SlantedProgressBarFill.Create(new Size(200d, 10d), 7f, 0.5f);

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var totalWidth = 0d;
        for (var i = 0; i < 10_000; i++)
            totalWidth += SlantedProgressBarFill.Create(new Size(200d, 10d), 7f, i / 10_000f).LocalBounds.Width;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.True(totalWidth > 0d);
        Assert.Equal(0, allocated);
    }
}
