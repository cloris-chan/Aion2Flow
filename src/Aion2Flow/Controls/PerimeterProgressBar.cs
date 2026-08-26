using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Cloris.Aion2Flow.Controls;

public sealed class PerimeterProgressBar : Control
{
    public static readonly DirectProperty<PerimeterProgressBar, double> ValueProperty =
        AvaloniaProperty.RegisterDirect<PerimeterProgressBar, double>(
            nameof(Value),
            control => control.Value,
            (control, value) => control.Value = value);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PerimeterProgressBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<PerimeterProgressBar, IBrush?>(nameof(Background));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<PerimeterProgressBar, double>(nameof(Thickness), 2d);

    static PerimeterProgressBar()
    {
        AffectsRender<PerimeterProgressBar>(ValueProperty, FillProperty, BackgroundProperty, ThicknessProperty);
    }

    public double Value
    {
        get;
        set => SetAndRaise(ValueProperty, ref field, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var thickness = double.IsFinite(Thickness) ? Math.Max(0d, Thickness) : 0d;
        if (width <= thickness || height <= thickness || thickness <= 0d)
            return;

        var rectangle = new Rect(Bounds.Size).Deflate(thickness / 2d);
        var background = Background;
        if (background is not null)
            DrawPerimeter(context, CreatePen(background, thickness), rectangle, 1d);

        var fill = Fill;
        var ratio = double.IsFinite(Value) ? Math.Clamp(Value, 0d, 1d) : 0d;
        if (fill is not null && ratio > 0d)
            DrawPerimeter(context, CreatePen(fill, thickness), rectangle, ratio);
    }

    private static Pen CreatePen(IBrush brush, double thickness)
        => new(
            brush,
            thickness,
            lineCap: PenLineCap.Square,
            lineJoin: PenLineJoin.Miter);

    private static void DrawPerimeter(DrawingContext context, Pen pen, Rect rectangle, double ratio)
    {
        var width = rectangle.Width;
        var height = rectangle.Height;
        var remaining = 2d * (width + height) * ratio;
        if (remaining <= 0d)
            return;

        var topLeft = new Point(rectangle.Left, rectangle.Top);
        var topRight = new Point(rectangle.Right, rectangle.Top);
        var bottomRight = new Point(rectangle.Right, rectangle.Bottom);
        var bottomLeft = new Point(rectangle.Left, rectangle.Bottom);

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(topLeft, isFilled: false);
            remaining = AppendSide(geometryContext, topLeft, topRight, width, remaining);
            remaining = AppendSide(geometryContext, topRight, bottomRight, height, remaining);
            remaining = AppendSide(geometryContext, bottomRight, bottomLeft, width, remaining);
            AppendSide(geometryContext, bottomLeft, topLeft, height, remaining);
            geometryContext.EndFigure(isClosed: ratio >= 1d);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static double AppendSide(
        StreamGeometryContext context,
        Point start,
        Point end,
        double length,
        double remaining)
    {
        if (remaining <= 0d)
            return 0d;

        var segmentLength = Math.Min(length, remaining);
        var ratio = length > 0d ? segmentLength / length : 0d;
        var point = new Point(
            start.X + (end.X - start.X) * ratio,
            start.Y + (end.Y - start.Y) * ratio);
        context.LineTo(point);
        return remaining - segmentLength;
    }
}
