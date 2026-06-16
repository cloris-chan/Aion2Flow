using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class PlaybackTimelineStripView : Control
{
    public static readonly DirectProperty<PlaybackTimelineStripView, IReadOnlyList<PlaybackTimelineBand>?> BandsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, IReadOnlyList<PlaybackTimelineBand>?>(nameof(Bands), view => view.Bands, (view, value) => view.Bands = value);

    public static readonly DirectProperty<PlaybackTimelineStripView, double> DurationMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, double>(nameof(DurationMilliseconds), view => view.DurationMilliseconds, (view, value) => view.DurationMilliseconds = value);

    public static readonly DirectProperty<PlaybackTimelineStripView, double> PositionMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, double>(nameof(PositionMilliseconds), view => view.PositionMilliseconds, (view, value) => view.PositionMilliseconds = value);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, IBrush?>(nameof(PlayheadBrush));

    public static readonly StyledProperty<double> PlayheadThicknessProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, double>(nameof(PlayheadThickness), 2d);

    static PlaybackTimelineStripView()
    {
        AffectsRender<PlaybackTimelineStripView>(BandsProperty, DurationMillisecondsProperty, PositionMillisecondsProperty, TrackBrushProperty, PlayheadBrushProperty, PlayheadThicknessProperty);
    }

    private IReadOnlyList<PlaybackTimelineBand>? _bands;
    private double _durationMilliseconds;
    private double _positionMilliseconds;

    public event EventHandler<PlaybackSeekRequestedEventArgs>? SeekRequested;

    public IReadOnlyList<PlaybackTimelineBand>? Bands
    {
        get => _bands;
        set => SetAndRaise(BandsProperty, ref _bands, value);
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

        var bands = Bands;
        if (bands is not null && bands.Count > 0)
            DrawBands(context, bands, duration, bounds);

        var playheadX = PlaybackTimelineGeometry.PositionToX(PositionMilliseconds, duration, bounds.Width);
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

    private static void DrawBands(DrawingContext context, IReadOnlyList<PlaybackTimelineBand> bands, double duration, Rect bounds)
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
            var pen = new Pen(band.Brush, 1d);
            for (var markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                DrawMarker(context, markers[markerIndex], duration, bounds.Width, y, pen);
        }
    }

    private static void DrawMarker(DrawingContext context, PlaybackTimelineMarker marker, double duration, double width, double y, Pen pen)
    {
        var x = PlaybackTimelineGeometry.PositionToX(marker.PositionMilliseconds, duration, width);
        var halfWidth = Math.Clamp(marker.Weight * 1.8d, 6d, 42d) * 0.5d;
        var start = Math.Max(0d, x - halfWidth);
        var end = Math.Min(width, x + halfWidth);
        context.DrawLine(pen, new Point(start, y), new Point(Math.Max(start + 1d, end), y));
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
