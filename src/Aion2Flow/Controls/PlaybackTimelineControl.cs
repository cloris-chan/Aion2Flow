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

public abstract class PlaybackTimelineControl : Control
{
    public static readonly DirectProperty<PlaybackTimelineControl, PlaybackTimelineViewport> ViewportProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineControl, PlaybackTimelineViewport>(nameof(Viewport), view => view.Viewport, (view, value) => view.Viewport = value);

    public static readonly DirectProperty<PlaybackTimelineControl, double> PositionMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineControl, double>(nameof(PositionMilliseconds), view => view.PositionMilliseconds, (view, value) => view.PositionMilliseconds = value);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<PlaybackTimelineControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<PlaybackTimelineControl, IBrush?>(nameof(PlayheadBrush));

    public static readonly StyledProperty<double> PlayheadThicknessProperty = AvaloniaProperty.Register<PlaybackTimelineControl, double>(nameof(PlayheadThickness), 2d);

    public static readonly StyledProperty<bool> IsPlayheadVisibleProperty = AvaloniaProperty.Register<PlaybackTimelineControl, bool>(nameof(IsPlayheadVisible), true);

    static PlaybackTimelineControl()
    {
        AffectsRender<PlaybackTimelineControl>(ViewportProperty, PositionMillisecondsProperty, TrackBrushProperty, PlayheadBrushProperty, PlayheadThicknessProperty, IsPlayheadVisibleProperty);
    }

    private PlaybackTimelineViewport _viewport;
    private double _positionMilliseconds;
    private Pen? _playheadPen;

    public event EventHandler<PlaybackSeekRequestedEventArgs>? SeekRequested;

    public PlaybackTimelineViewport Viewport
    {
        get => _viewport;
        set => SetAndRaise(ViewportProperty, ref _viewport, value);
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

    public bool IsPlayheadVisible
    {
        get => GetValue(IsPlayheadVisibleProperty);
        set => SetValue(IsPlayheadVisibleProperty, value);
    }

    public sealed override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        using var clip = context.PushClip(bounds);
        context.FillRectangle(TrackBrush ?? Brushes.Transparent, bounds);
        var viewport = Viewport;
        if (viewport.IsEmpty)
            return;

        RenderTimeline(context, viewport, bounds);
        DrawPlayhead(context, bounds);
    }

    protected abstract void RenderTimeline(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds);

    private void DrawPlayhead(DrawingContext context, Rect bounds)
    {
        if (!IsPlayheadVisible || !Viewport.Contains(PositionMilliseconds))
            return;

        var brush = PlayheadBrush ?? Brushes.White;
        var thickness = Math.Max(1d, PlayheadThickness);
        if (_playheadPen is null || !ReferenceEquals(_playheadPen.Brush, brush) || _playheadPen.Thickness != thickness)
            _playheadPen = new Pen(brush, thickness);

        var x = PlaybackTimelineGeometry.PositionToX(PositionMilliseconds, Viewport, bounds.Width);
        context.DrawLine(_playheadPen, new Point(x, 0d), new Point(x, bounds.Height));
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
        if (Bounds.Width <= 0d || Viewport.IsEmpty)
            return;

        SeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(PlaybackTimelineGeometry.XToPosition(x, Viewport, Bounds.Width)));
    }
}
