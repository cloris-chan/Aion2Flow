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

public sealed class PlaybackLaneView : Control
{
    public static readonly StyledProperty<IReadOnlyList<PlaybackTimelineMarker>?> MarkersProperty = AvaloniaProperty.Register<PlaybackLaneView, IReadOnlyList<PlaybackTimelineMarker>?>(nameof(Markers));

    public static readonly StyledProperty<double> DurationMillisecondsProperty = AvaloniaProperty.Register<PlaybackLaneView, double>(nameof(DurationMilliseconds));

    public static readonly StyledProperty<double> PositionMillisecondsProperty = AvaloniaProperty.Register<PlaybackLaneView, double>(nameof(PositionMilliseconds));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<PlaybackLaneView, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> LaneBackgroundProperty = AvaloniaProperty.Register<PlaybackLaneView, IBrush?>(nameof(LaneBackground));

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<PlaybackLaneView, IBrush?>(nameof(PlayheadBrush));

    static PlaybackLaneView()
    {
        AffectsRender<PlaybackLaneView>(MarkersProperty, DurationMillisecondsProperty, PositionMillisecondsProperty, AccentBrushProperty, LaneBackgroundProperty, PlayheadBrushProperty);
    }

    public event EventHandler<PlaybackSeekRequestedEventArgs>? SeekRequested;

    public IReadOnlyList<PlaybackTimelineMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public double DurationMilliseconds
    {
        get => GetValue(DurationMillisecondsProperty);
        set => SetValue(DurationMillisecondsProperty, value);
    }

    public double PositionMilliseconds
    {
        get => GetValue(PositionMillisecondsProperty);
        set => SetValue(PositionMillisecondsProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? LaneBackground
    {
        get => GetValue(LaneBackgroundProperty);
        set => SetValue(LaneBackgroundProperty, value);
    }

    public IBrush? PlayheadBrush
    {
        get => GetValue(PlayheadBrushProperty);
        set => SetValue(PlayheadBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var background = LaneBackground ?? Brushes.Transparent;
        context.FillRectangle(background, bounds);

        var accent = AccentBrush;
        if (accent is not null)
            context.FillRectangle(accent, new Rect(0, 0, 3, bounds.Height));

        var duration = DurationMilliseconds;
        if (duration <= 0)
            return;

        var markers = Markers;
        if (markers is not null)
        {
            for (var i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                var ratio = Math.Clamp(marker.PositionMilliseconds / duration, 0d, 1d);
                var markerWidth = Math.Clamp(marker.Weight, 3d, 12d);
                var x = Math.Clamp(ratio * bounds.Width - markerWidth * 0.5d, 3d, Math.Max(3d, bounds.Width - markerWidth));
                var height = Math.Max(8d, bounds.Height - 8d);
                var y = (bounds.Height - height) * 0.5d;
                context.FillRectangle(marker.Brush, new Rect(x, y, markerWidth, height));
            }
        }

        var playheadRatio = Math.Clamp(PositionMilliseconds / duration, 0d, 1d);
        var playheadX = Math.Clamp(playheadRatio * bounds.Width, 0d, bounds.Width);
        var playhead = PlayheadBrush ?? Brushes.White;
        context.DrawLine(new Pen(playhead, 2), new Point(playheadX, 0), new Point(playheadX, bounds.Height));
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

    private void RequestSeek(double x)
    {
        var width = Bounds.Width;
        var duration = DurationMilliseconds;
        if (width <= 0 || duration <= 0)
            return;

        var ratio = Math.Clamp(x / width, 0d, 1d);
        SeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(ratio * duration));
    }
}
