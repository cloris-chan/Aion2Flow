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

    public static readonly DirectProperty<PlaybackTimelineStripView, PlaybackTimelineViewport> ViewportProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, PlaybackTimelineViewport>(nameof(Viewport), view => view.Viewport, (view, value) => view.Viewport = value);

    public static readonly DirectProperty<PlaybackTimelineStripView, double> PositionMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<PlaybackTimelineStripView, double>(nameof(PositionMilliseconds), view => view.PositionMilliseconds, (view, value) => view.PositionMilliseconds = value);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, IBrush?>(nameof(PlayheadBrush));

    public static readonly StyledProperty<double> PlayheadThicknessProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, double>(nameof(PlayheadThickness), 2d);

    public static readonly StyledProperty<bool> IsPlayheadVisibleProperty = AvaloniaProperty.Register<PlaybackTimelineStripView, bool>(nameof(IsPlayheadVisible), true);

    static PlaybackTimelineStripView()
    {
        AffectsRender<PlaybackTimelineStripView>(BandsProperty, ViewportProperty, PositionMillisecondsProperty, TrackBrushProperty, PlayheadBrushProperty, PlayheadThicknessProperty, IsPlayheadVisibleProperty);
    }

    private IReadOnlyList<PlaybackTimelineBand>? _bands;
    private PlaybackTimelineViewport _viewport;
    private double _positionMilliseconds;
    private IBrush? _cachedPlayheadBrush;
    private double _cachedPlayheadThickness;
    private Pen? _playheadPen;

    public event EventHandler<PlaybackSeekRequestedEventArgs>? SeekRequested;

    public IReadOnlyList<PlaybackTimelineBand>? Bands
    {
        get => _bands;
        set => SetAndRaise(BandsProperty, ref _bands, value);
    }

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

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        using var clip = context.PushClip(bounds);
        context.FillRectangle(TrackBrush ?? Brushes.Transparent, bounds);

        var viewport = Viewport;
        if (viewport.IsEmpty)
            return;

        var bands = Bands;
        if (bands is not null && bands.Count > 0)
            DrawBands(context, bands, viewport, bounds);

        var positionMilliseconds = PositionMilliseconds;
        if (!IsPlayheadVisible || !viewport.Contains(positionMilliseconds))
            return;

        var playheadX = PlaybackTimelineGeometry.PositionToX(positionMilliseconds, viewport, bounds.Width);
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

    private void RequestSeek(double x)
    {
        var width = Bounds.Width;
        var viewport = Viewport;
        if (width <= 0d || viewport.IsEmpty)
            return;

        SeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(PlaybackTimelineGeometry.XToPosition(x, viewport, width)));
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
