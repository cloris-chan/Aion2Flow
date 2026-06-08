using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class SegmentedProgressBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProgressSegment>?> SegmentsProperty = AvaloniaProperty.Register<SegmentedProgressBar, IReadOnlyList<ProgressSegment>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(nameof(Background));

    static SegmentedProgressBar()
    {
        AffectsRender<SegmentedProgressBar>(SegmentsProperty, BackgroundProperty);
    }

    public IReadOnlyList<ProgressSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var background = Background;
        if (background is not null)
            context.FillRectangle(background, bounds);

        var segments = Segments;
        if (segments is null || segments.Count == 0)
            return;

        var left = 0d;
        var width = bounds.Width;
        var height = bounds.Height;
        for (var i = 0; i < segments.Count && left < width; i++)
        {
            var segment = segments[i];
            var ratio = Math.Clamp(segment.Ratio, 0d, 1d);
            if (ratio <= 0)
                continue;

            var segmentWidth = Math.Min(width - left, width * ratio);
            if (segmentWidth <= 0)
                continue;

            context.FillRectangle(segment.Brush, new Rect(left, 0, segmentWidth, height));
            left += segmentWidth;
        }
    }
}
