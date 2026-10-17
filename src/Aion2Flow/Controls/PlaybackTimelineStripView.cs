using Avalonia;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class PlaybackTimelineStripView : PlaybackTimelineControl
{
    public static readonly DirectProperty<PlaybackTimelineStripView, IReadOnlyList<PlaybackTimelineBand>?> BandsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, IReadOnlyList<PlaybackTimelineBand>?>(nameof(Bands), view => view.Bands, (view, value) => view.Bands = value);

    static PlaybackTimelineStripView()
    {
        AffectsRender<PlaybackTimelineStripView>(BandsProperty);
    }

    private IReadOnlyList<PlaybackTimelineBand>? _bands;

    public IReadOnlyList<PlaybackTimelineBand>? Bands
    {
        get => _bands;
        set => SetAndRaise(BandsProperty, ref _bands, value);
    }

    protected override void RenderTimeline(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds)
    {
        var bands = Bands;
        if (bands is not null && bands.Count > 0)
            DrawBands(context, bands, viewport, bounds);
    }

    private static void DrawBands(DrawingContext context, IReadOnlyList<PlaybackTimelineBand> bands, PlaybackTimelineViewport viewport, Rect bounds)
    {
        var bandCount = Math.Max(1, bands.Count);
        var step = bounds.Height / (bandCount + 1);
        for (var bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            var band = bands[bandIndex];
            var markers = band.Markers;
            if (markers.Count == 0)
                continue;

            var y = Math.Round(step * (bandIndex + 1)) + 0.5d;
            for (var markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                DrawMarker(context, markers[markerIndex], viewport, bounds.Width, y, band.Brush);
        }
    }

    private static void DrawMarker(DrawingContext context, PlaybackTimelineMarker marker, PlaybackTimelineViewport viewport, double width, double y, IBrush brush)
    {
        if (!viewport.Contains(marker.PositionMilliseconds))
            return;

        var x = PlaybackTimelineGeometry.PositionToX(marker.PositionMilliseconds, viewport, width);
        var halfWidth = Math.Clamp(marker.Weight * 1.8d, 6d, 42d) * 0.5d;
        var start = Math.Max(0d, x - halfWidth);
        var end = Math.Min(width, x + halfWidth);
        context.FillRectangle(brush, new Rect(start, y - 0.5d, Math.Max(1d, end - start), 1d));
    }

}
