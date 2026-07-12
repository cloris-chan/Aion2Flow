using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Cloris.Aion2Flow.Controls;

public sealed class SegmentedTableSeparator : Control
{
    public static readonly DirectProperty<SegmentedTableSeparator, IBrush?> LineBrushProperty = AvaloniaProperty.RegisterDirect<SegmentedTableSeparator, IBrush?>(nameof(LineBrush), control => control.LineBrush, (control, value) => control.LineBrush = value);

    public static readonly DirectProperty<SegmentedTableSeparator, double> ThicknessProperty = AvaloniaProperty.RegisterDirect<SegmentedTableSeparator, double>(nameof(Thickness), control => control.Thickness, (control, value) => control.Thickness = value);

    public static readonly DirectProperty<SegmentedTableSeparator, double> GapWidthProperty = AvaloniaProperty.RegisterDirect<SegmentedTableSeparator, double>(nameof(GapWidth), control => control.GapWidth, (control, value) => control.GapWidth = value);

    private IBrush? _lineBrush;
    private double _thickness = 1d;
    private double _gapWidth = 10d;

    static SegmentedTableSeparator()
    {
        AffectsRender<SegmentedTableSeparator>(LineBrushProperty, ThicknessProperty, GapWidthProperty);
    }

    public IBrush? LineBrush
    {
        get => _lineBrush;
        set => SetAndRaise(LineBrushProperty, ref _lineBrush, value);
    }

    public double Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    public double GapWidth
    {
        get => _gapWidth;
        set => SetAndRaise(GapWidthProperty, ref _gapWidth, value);
    }

    public override void Render(DrawingContext context)
    {
        var brush = LineBrush;
        var thickness = Thickness;
        if (brush is null || thickness <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var top = Math.Max(0d, Bounds.Height - thickness);
        if (Parent is not Grid grid || grid.ColumnDefinitions.Count <= 1)
        {
            context.FillRectangle(brush, new Rect(0d, top, Bounds.Width, thickness));
            return;
        }

        var halfGap = Math.Max(0d, GapWidth) / 2d;
        var spacing = grid.ColumnSpacing;
        var x = 0d;
        var segmentStart = 0d;

        for (var i = 0; i < grid.ColumnDefinitions.Count - 1; i++)
        {
            x += grid.ColumnDefinitions[i].ActualWidth;
            var gapCenter = x + spacing / 2d;
            var gapStart = Math.Clamp(gapCenter - halfGap, segmentStart, Bounds.Width);
            if (gapStart > segmentStart)
                context.FillRectangle(brush, new Rect(segmentStart, top, gapStart - segmentStart, thickness));

            x += spacing;
            segmentStart = Math.Clamp(gapCenter + halfGap, 0d, Bounds.Width);
        }

        if (Bounds.Width > segmentStart)
            context.FillRectangle(brush, new Rect(segmentStart, top, Bounds.Width - segmentStart, thickness));
    }
}
