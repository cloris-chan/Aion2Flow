using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class SlantedProgressBar : Control
{
    public static readonly DirectProperty<SlantedProgressBar, ProgressSegment?> SegmentProperty =
        AvaloniaProperty.RegisterDirect<SlantedProgressBar, ProgressSegment?>(
            nameof(Segment),
            control => control.Segment,
            (control, value) => control.Segment = value);

    public static readonly StyledProperty<double> SlantWidthProperty =
        AvaloniaProperty.Register<SlantedProgressBar, double>(nameof(SlantWidth), 7d);

    private ImmutableSolidColorBrush _fillBrush = new(Colors.Transparent);
    private StreamGeometry? _fillGeometry;
    private SlantedProgressBarVisualState _cachedState;
    private Size _geometrySize;
    private float _geometrySlant = float.NaN;
    private float _geometryRatio = float.NaN;
    private bool _hasCachedState;

    static SlantedProgressBar()
    {
        AffectsRender<SlantedProgressBar>(SlantWidthProperty);
    }

    public ProgressSegment? Segment
    {
        get;
        set => SetAndRaise(SegmentProperty, ref field, value);
    }

    public double SlantWidth
    {
        get => GetValue(SlantWidthProperty);
        set => SetValue(SlantWidthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var state = CreateVisualState();
        EnsureDrawingResources(state);
        if (state.Ratio > 0f)
            context.DrawGeometry(_fillBrush, null, _fillGeometry!);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentProperty)
            InvalidateVisual();
    }

    internal SlantedProgressBarVisualState CreateVisualStateForDiagnostics() => CreateVisualState();

    private SlantedProgressBarVisualState CreateVisualState()
    {
        var segment = Segment;
        var ratio = segment.HasValue ? Math.Clamp(segment.Value.Ratio, 0d, 1d) : 0d;
        var fillColor = segment.HasValue
            ? HudBrushColor.Resolve(segment.Value.Brush, Colors.Transparent)
            : Colors.Transparent;
        return new SlantedProgressBarVisualState(
            (float)ratio,
            fillColor,
            (float)Math.Max(0d, SlantWidth));
    }

    private void EnsureDrawingResources(SlantedProgressBarVisualState state)
    {
        if (!_hasCachedState || state.FillColor != _cachedState.FillColor)
            _fillBrush = new ImmutableSolidColorBrush(state.FillColor);

        var size = Bounds.Size;
        if (size != _geometrySize || state.SlantWidth != _geometrySlant || state.Ratio != _geometryRatio)
        {
            var bounds = SlantedProgressBarVertices.Create(
                0f,
                0f,
                (float)size.Width,
                (float)size.Height,
                state.SlantWidth);
            _fillGeometry = CreateGeometry(SlantedProgressBarVertices.CreateFill(bounds, state.Ratio));
            _geometrySize = size;
            _geometrySlant = state.SlantWidth;
            _geometryRatio = state.Ratio;
        }

        _cachedState = state;
        _hasCachedState = true;
    }

    private static StreamGeometry CreateGeometry(SlantedProgressBarVertices vertices)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(ToPoint(vertices.TopLeft), isFilled: true);
        context.LineTo(ToPoint(vertices.TopRight));
        context.LineTo(ToPoint(vertices.BottomRight));
        context.LineTo(ToPoint(vertices.BottomLeft));
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);
}

internal readonly record struct SlantedProgressBarVisualState(
    float Ratio,
    Color FillColor,
    float SlantWidth);

internal readonly record struct SlantedProgressBarVertices(
    Vector2 TopLeft,
    Vector2 TopRight,
    Vector2 BottomRight,
    Vector2 BottomLeft)
{
    internal static SlantedProgressBarVertices Create(float x, float y, float width, float height, float slantWidth)
    {
        var resolvedWidth = Math.Max(0f, width);
        var resolvedHeight = Math.Max(0f, height);
        var resolvedSlant = Math.Clamp(slantWidth, 0f, resolvedWidth);
        return new SlantedProgressBarVertices(
            new Vector2(x + resolvedSlant, y),
            new Vector2(x + resolvedWidth, y),
            new Vector2(x + resolvedWidth - resolvedSlant, y + resolvedHeight),
            new Vector2(x, y + resolvedHeight));
    }

    internal static SlantedProgressBarVertices CreateFill(SlantedProgressBarVertices bounds, float ratio)
    {
        var resolvedRatio = Math.Clamp(ratio, 0f, 1f);
        var topWidth = Math.Max(0f, bounds.TopRight.X - bounds.TopLeft.X) * resolvedRatio;
        var bottomWidth = Math.Max(0f, bounds.BottomRight.X - bounds.BottomLeft.X) * resolvedRatio;
        return new SlantedProgressBarVertices(
            bounds.TopLeft,
            new Vector2(bounds.TopLeft.X + topWidth, bounds.TopLeft.Y),
            new Vector2(bounds.BottomLeft.X + bottomWidth, bounds.BottomLeft.Y),
            bounds.BottomLeft);
    }
}
