using System.Numerics;
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
        var bounds = SlantedProgressBarVertices.Create(0f, 0f, 200f, 10f, 7f);
        var fill = SlantedProgressBarVertices.CreateFill(bounds, 0.6f);
        var full = SlantedProgressBarVertices.CreateFill(bounds, 1f);
        var boundsLeftEdge = bounds.BottomLeft - bounds.TopLeft;
        var boundsRightEdge = bounds.BottomRight - bounds.TopRight;
        var fillLeftEdge = fill.BottomLeft - fill.TopLeft;
        var fillRightEdge = fill.BottomRight - fill.TopRight;
        var fillRatio = (fill.TopRight.X - fill.TopLeft.X) /
                        (full.TopRight.X - full.TopLeft.X);

        Assert.Equal(new Vector2(-7f, 10f), boundsLeftEdge);
        Assert.Equal(boundsLeftEdge, boundsRightEdge);
        Assert.Equal(bounds.TopLeft, fill.TopLeft);
        Assert.Equal(bounds.BottomLeft, fill.BottomLeft);
        Assert.Equal(fillLeftEdge.X, fillRightEdge.X, 4);
        Assert.Equal(fillLeftEdge.Y, fillRightEdge.Y, 4);
        Assert.Equal(0.6f, fillRatio, 3);
    }
}
