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

    public static readonly StyledProperty<bool> IsPlayheadVisibleProperty = AvaloniaProperty.Register<PlaybackTimelineView, bool>(nameof(IsPlayheadVisible), true);

    static PlaybackTimelineView()
    {
        AffectsRender<PlaybackTimelineView>(MarkersProperty, SpansProperty, DurationMillisecondsProperty, PositionMillisecondsProperty, TrackBrushProperty, ProgressBrushProperty, PlayheadBrushProperty, PlayheadThicknessProperty, IsPlayheadVisibleProperty);
    }

    private IReadOnlyList<PlaybackTimelineMarker>? _markers;
    private IReadOnlyList<PlaybackTimelineSpan>? _spans;
    private double _durationMilliseconds;
    private double _positionMilliseconds;
    private IBrush? _cachedPlayheadBrush;
    private double _cachedPlayheadThickness;
    private Pen? _playheadPen;

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

    public bool IsPlayheadVisible
    {
        get => GetValue(IsPlayheadVisibleProperty);
        set => SetValue(IsPlayheadVisibleProperty, value);
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

        if (IsPlayheadVisible)
            context.DrawLine(GetPlayheadPen(), new Point(playheadX, 0), new Point(playheadX, bounds.Height));
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
        var centerX = PlaybackTimelineGeometry.PositionToX(marker.PositionMilliseconds, duration, bounds.Width);
        if (marker.IsApplication)
        {
            var radius = Math.Clamp(bounds.Height * 0.26d, 1.6d, 3d);
            var x = Math.Clamp(centerX, radius, Math.Max(radius, bounds.Width - radius));
            context.DrawEllipse(marker.Brush, null, new Point(x, bounds.Height * 0.5d), radius, radius);
            return;
        }

        var markerWidth = Math.Clamp(marker.Weight * 1.35d, 3d, 16d);
        var start = Math.Max(0d, centerX - markerWidth * 0.5d);
        var end = Math.Min(bounds.Width, centerX + markerWidth * 0.5d);
        var y = Math.Round(bounds.Height * 0.5d) + 0.5d;
        context.FillRectangle(marker.Brush, new Rect(start, y - 0.5d, Math.Max(1d, end - start), 1d));
    }

    private static void DrawSpan(DrawingContext context, PlaybackTimelineSpan span, double duration, Rect bounds)
    {
        var start = PlaybackTimelineGeometry.PositionToX(span.StartMilliseconds, duration, bounds.Width);
        var end = PlaybackTimelineGeometry.PositionToX(span.EndMilliseconds, duration, bounds.Width);
        var width = Math.Max(1d, end - start);
        var thickness = Math.Clamp(bounds.Height * 0.28d, 1d, 2d);
        var y = Math.Round((bounds.Height - thickness) * 0.5d) + 0.5d;
        var rect = new Rect(start, y, width, thickness);
        context.FillRectangle(span.FillBrush, rect);
        context.FillRectangle(span.BorderBrush, new Rect(start, y + thickness * 0.5d - 0.5d, width, 1d));
    }

    private void RequestSeek(double x)
    {
        var width = Bounds.Width;
        var duration = DurationMilliseconds;
        if (width <= 0 || duration <= 0)
            return;

        SeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(PlaybackTimelineGeometry.XToPosition(x, duration, width)));
    }

    private Pen GetPlayheadPen()
    {
        var brush = PlayheadBrush ?? Brushes.White;
        var thickness = Math.Max(1d, PlayheadThickness);
        if (_playheadPen is null || !ReferenceEquals(_cachedPlayheadBrush, brush) || Math.Abs(_cachedPlayheadThickness - thickness) > double.Epsilon)
        {
            _cachedPlayheadBrush = brush;
            _cachedPlayheadThickness = thickness;
            _playheadPen = new Pen(brush, thickness);
        }

        return _playheadPen;
    }
}
