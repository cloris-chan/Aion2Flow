using Avalonia;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class PlaybackTimelineView : PlaybackTimelineControl
{
    public static readonly DirectProperty<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineMarker>?> MarkersProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineMarker>?>(nameof(Markers), view => view.Markers, (view, value) => view.Markers = value);

    public static readonly DirectProperty<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineSpan>?> SpansProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineSpan>?>(nameof(Spans), view => view.Spans, (view, value) => view.Spans = value);

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty = AvaloniaProperty.Register<PlaybackTimelineView, IBrush?>(nameof(ProgressBrush));

    static PlaybackTimelineView()
    {
        AffectsRender<PlaybackTimelineView>(MarkersProperty, SpansProperty, ProgressBrushProperty);
    }

    private IReadOnlyList<PlaybackTimelineMarker>? _markers;
    private IReadOnlyList<PlaybackTimelineSpan>? _spans;

    public IReadOnlyList<PlaybackTimelineMarker>? Markers
    {
        get => _markers;
        set => SetAndRaise(MarkersProperty, ref _markers, value);
    }

    public IReadOnlyList<PlaybackTimelineSpan>? Spans
    {
        get => _spans;
        set => SetAndRaise(SpansProperty, ref _spans, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    protected override void RenderTimeline(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds)
    {
        var positionMilliseconds = PositionMilliseconds;
        var playheadX = PlaybackTimelineGeometry.PositionToX(positionMilliseconds, viewport, bounds.Width);
        var progress = ProgressBrush;
        if (progress is not null && positionMilliseconds > viewport.StartMilliseconds && playheadX > 0d)
            context.FillRectangle(progress, new Rect(0, 0, playheadX, bounds.Height));

        var spans = Spans;
        if (spans is not null)
        {
            for (var i = 0; i < spans.Count; i++)
                DrawSpan(context, spans[i], viewport, bounds);
        }

        var markers = Markers;
        if (markers is not null)
        {
            for (var i = 0; i < markers.Count; i++)
                DrawMarker(context, markers[i], viewport, bounds);
        }
    }

    private static void DrawMarker(DrawingContext context, PlaybackTimelineMarker marker, PlaybackTimelineViewport viewport, Rect bounds)
    {
        if (!viewport.Contains(marker.PositionMilliseconds))
            return;

        var centerX = PlaybackTimelineGeometry.PositionToX(marker.PositionMilliseconds, viewport, bounds.Width);
        if (marker.IsApplication)
        {
            var radius = Math.Clamp(bounds.Height * 0.26d, 1.6d, 3d);
            context.DrawEllipse(marker.Brush, null, new Point(centerX, bounds.Height * 0.5d), radius, radius);
            return;
        }

        var markerWidth = Math.Clamp(marker.Weight * 1.35d, 3d, 16d);
        var start = Math.Max(0d, centerX - markerWidth * 0.5d);
        var end = Math.Min(bounds.Width, centerX + markerWidth * 0.5d);
        var y = Math.Round(bounds.Height * 0.5d) + 0.5d;
        context.FillRectangle(marker.Brush, new Rect(start, y - 0.5d, Math.Max(1d, end - start), 1d));
    }

    private static void DrawSpan(DrawingContext context, PlaybackTimelineSpan span, PlaybackTimelineViewport viewport, Rect bounds)
    {
        if (!PlaybackTimelineGeometry.TryClipSpan(span.StartMilliseconds, span.EndMilliseconds, viewport, out var clippedStartMilliseconds, out var clippedEndMilliseconds))
            return;

        var start = PlaybackTimelineGeometry.PositionToX(clippedStartMilliseconds, viewport, bounds.Width);
        var end = PlaybackTimelineGeometry.PositionToX(clippedEndMilliseconds, viewport, bounds.Width);
        var width = Math.Max(1d, end - start);
        var thickness = Math.Clamp(bounds.Height * 0.28d, 1d, 2d);
        var y = Math.Round((bounds.Height - thickness) * 0.5d) + 0.5d;
        var rect = new Rect(start, y, width, thickness);
        context.FillRectangle(span.FillBrush, rect);
        context.FillRectangle(span.BorderBrush, new Rect(start, y + thickness * 0.5d - 0.5d, width, 1d));
    }

}
