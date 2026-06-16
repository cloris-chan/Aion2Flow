using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class PlaybackSeekRequestedEventArgs(double positionMilliseconds) : EventArgs
{
    public double PositionMilliseconds { get; } = positionMilliseconds;
}

public sealed class PlaybackTimelineView : Control
{
    public static readonly DirectProperty<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineMarker>?> MarkersProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineMarker>?>(nameof(Markers), view => view.Markers, (view, value) => view.Markers = value);

    public static readonly DirectProperty<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineSpan>?> SpansProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, IReadOnlyList<PlaybackTimelineSpan>?>(nameof(Spans), view => view.Spans, (view, value) => view.Spans = value);

    public static readonly DirectProperty<PlaybackTimelineView, double> DurationMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, double>(nameof(DurationMilliseconds), view => view.DurationMilliseconds, (view, value) => view.DurationMilliseconds = value);

    public static readonly DirectProperty<PlaybackTimelineView, double> PositionMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineView, double>(nameof(PositionMilliseconds), view => view.PositionMilliseconds, (view, value) => view.PositionMilliseconds = value);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<PlaybackTimelineView, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty = AvaloniaProperty.Register<PlaybackTimelineView, IBrush?>(nameof(ProgressBrush));

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<PlaybackTimelineView, IBrush?>(nameof(PlayheadBrush));

    public static readonly StyledProperty<double> PlayheadThicknessProperty = AvaloniaProperty.Register<PlaybackTimelineView, double>(nameof(PlayheadThickness), 2d);

    static PlaybackTimelineView()
    {
        AffectsRender<PlaybackTimelineView>(MarkersProperty, SpansProperty, DurationMillisecondsProperty, PositionMillisecondsProperty, TrackBrushProperty, ProgressBrushProperty, PlayheadBrushProperty, PlayheadThicknessProperty);
    }

    private IReadOnlyList<PlaybackTimelineMarker>? _markers;
    private IReadOnlyList<PlaybackTimelineSpan>? _spans;
    private double _durationMilliseconds;
    private double _positionMilliseconds;

    public event EventHandler<PlaybackSeekRequestedEventArgs>? SeekRequested;

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

    public double DurationMilliseconds
    {
        get => _durationMilliseconds;
        set => SetAndRaise(DurationMillisecondsProperty, ref _durationMilliseconds, value);
    }

    public double PositionMilliseconds
    {
        get => _positionMilliseconds;
        set => SetAndRaise(PositionMillisecondsProperty, ref _positionMilliseconds, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public IBrush? PlayheadBrush
    {
        get => GetValue(PlayheadBrushProperty);
        set => SetValue(PlayheadBrushProperty, value);
    }

    public double PlayheadThickness
    {
        get => GetValue(PlayheadThicknessProperty);
        set => SetValue(PlayheadThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        using var clip = context.PushClip(bounds);
        context.FillRectangle(TrackBrush ?? Brushes.Transparent, bounds);

        var duration = DurationMilliseconds;
        if (duration <= 0)
            return;

        var playheadX = PlaybackTimelineGeometry.PositionToX(PositionMilliseconds, duration, bounds.Width);
        var progress = ProgressBrush;
        if (progress is not null && playheadX > 0)
            context.FillRectangle(progress, new Rect(0, 0, playheadX, bounds.Height));

        var spans = Spans;
        if (spans is not null)
        {
            for (var i = 0; i < spans.Count; i++)
                DrawSpan(context, spans[i], duration, bounds);
        }

        var markers = Markers;
        if (markers is not null)
        {
            for (var i = 0; i < markers.Count; i++)
                DrawMarker(context, markers[i], duration, bounds);
        }

        var playhead = PlayheadBrush ?? Brushes.White;
        var thickness = Math.Max(1d, PlayheadThickness);
        context.DrawLine(new Pen(playhead, thickness), new Point(playheadX, 0), new Point(playheadX, bounds.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);
            RequestSeek(e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Captured == this)
        {
            RequestSeek(e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    private static void DrawMarker(DrawingContext context, PlaybackTimelineMarker marker, double duration, Rect bounds)
    {
        var markerWidth = marker.IsApplication ? Math.Clamp(bounds.Height - 6d, 10d, 16d) : Math.Clamp(marker.Weight, 3d, 12d);
        var x = Math.Clamp(PlaybackTimelineGeometry.PositionToX(marker.PositionMilliseconds, duration, bounds.Width) - markerWidth * 0.5d, 0d, Math.Max(0d, bounds.Width - markerWidth));
        var height = marker.IsApplication ? markerWidth : Math.Max(8d, bounds.Height - 8d);
        var y = (bounds.Height - height) * 0.5d;
        context.FillRectangle(marker.Brush, new Rect(x, y, markerWidth, height));
    }

    private static void DrawSpan(DrawingContext context, PlaybackTimelineSpan span, double duration, Rect bounds)
    {
        var start = PlaybackTimelineGeometry.PositionToX(span.StartMilliseconds, duration, bounds.Width);
        var end = PlaybackTimelineGeometry.PositionToX(span.EndMilliseconds, duration, bounds.Width);
        var width = Math.Max(1d, end - start);
        var rect = new Rect(start, 3d, width, Math.Max(1d, bounds.Height - 6d));
        context.FillRectangle(span.FillBrush, rect);
        context.DrawRectangle(null, new Pen(span.BorderBrush, 1d), rect);
    }

    private void RequestSeek(double x)
    {
        var width = Bounds.Width;
        var duration = DurationMilliseconds;
        if (width <= 0 || duration <= 0)
            return;

        SeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(PlaybackTimelineGeometry.XToPosition(x, duration, width)));
    }
}
