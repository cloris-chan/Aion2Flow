using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class PlaybackTimelineRulerView : PlaybackTimelineControl
{
    private const double TargetMajorTickSpacing = 96d;
    private const double MinimumLabelGap = 8d;
    private const int MaximumTickCount = 2048;
    private const int MaximumLabelCacheEntries = 128;

    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<PlaybackTimelineRulerView, IBrush?>(nameof(TickBrush));

    public static readonly StyledProperty<IBrush?> MinorTickBrushProperty = AvaloniaProperty.Register<PlaybackTimelineRulerView, IBrush?>(nameof(MinorTickBrush));

    public static readonly StyledProperty<double> TickThicknessProperty = AvaloniaProperty.Register<PlaybackTimelineRulerView, double>(nameof(TickThickness), 1d);

    public static readonly StyledProperty<double> MajorTickLengthProperty = AvaloniaProperty.Register<PlaybackTimelineRulerView, double>(nameof(MajorTickLength), 9d);

    public static readonly StyledProperty<double> MinorTickLengthProperty = AvaloniaProperty.Register<PlaybackTimelineRulerView, double>(nameof(MinorTickLength), 5d);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextBlock.FontFamilyProperty.AddOwner<PlaybackTimelineRulerView>();

    public static readonly StyledProperty<double> FontSizeProperty = TextBlock.FontSizeProperty.AddOwner<PlaybackTimelineRulerView>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty = TextBlock.FontStyleProperty.AddOwner<PlaybackTimelineRulerView>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextBlock.FontWeightProperty.AddOwner<PlaybackTimelineRulerView>();

    public static readonly StyledProperty<FontStretch> FontStretchProperty = TextBlock.FontStretchProperty.AddOwner<PlaybackTimelineRulerView>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<PlaybackTimelineRulerView>();

    static PlaybackTimelineRulerView()
    {
        AffectsRender<PlaybackTimelineRulerView>(
            TickBrushProperty,
            MinorTickBrushProperty,
            TickThicknessProperty,
            MajorTickLengthProperty,
            MinorTickLengthProperty,
            FontFamilyProperty,
            FontSizeProperty,
            FontStyleProperty,
            FontWeightProperty,
            FontStretchProperty,
            ForegroundProperty);
    }

    private readonly Dictionary<LabelCacheKey, FormattedText> _labelCache = [];
    private LabelStyleKey _labelStyleKey;
    private Typeface? _labelTypeface;
    private IBrush? _cachedTickBrush;
    private IBrush? _cachedMinorTickBrush;
    private double _cachedTickThickness;
    private Pen? _tickPen;
    private Pen? _minorTickPen;
    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public IBrush? MinorTickBrush
    {
        get => GetValue(MinorTickBrushProperty);
        set => SetValue(MinorTickBrushProperty, value);
    }

    public double TickThickness
    {
        get => GetValue(TickThicknessProperty);
        set => SetValue(TickThicknessProperty, value);
    }

    public double MajorTickLength
    {
        get => GetValue(MajorTickLengthProperty);
        set => SetValue(MajorTickLengthProperty, value);
    }

    public double MinorTickLength
    {
        get => GetValue(MinorTickLengthProperty);
        set => SetValue(MinorTickLengthProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void RenderTimeline(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds)
    {
        var tickScale = ResolveTickScale(viewport.DurationMilliseconds, bounds.Width);
        DrawTicks(context, viewport, bounds, tickScale);
    }

    private void DrawTicks(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds, TickScale tickScale)
    {
        var majorTickLength = Math.Clamp(MajorTickLength, 0d, bounds.Height);
        var minorTickLength = Math.Clamp(MinorTickLength, 0d, majorTickLength);
        var tickPen = GetTickPen();
        var minorTickPen = GetMinorTickPen();
        var baselineY = Math.Max(0d, bounds.Height - tickPen.Thickness * 0.5d);

        context.DrawLine(tickPen, new Point(0d, baselineY), new Point(bounds.Width, baselineY));
        DrawMinorTicks(context, viewport, bounds.Width, baselineY, minorTickLength, tickScale.MinorIntervalMilliseconds, minorTickPen);
        DrawMajorTicksAndLabels(context, viewport, bounds, baselineY, majorTickLength, tickScale.MajorIntervalMilliseconds, tickPen);
    }

    private static void DrawMinorTicks(DrawingContext context, PlaybackTimelineViewport viewport, double width, double baselineY, double tickLength, double intervalMilliseconds, Pen pen)
    {
        var tickMilliseconds = FirstAlignedTick(viewport.StartMilliseconds, intervalMilliseconds);
        var tolerance = intervalMilliseconds * 1e-9d;
        for (var count = 0; count < MaximumTickCount && tickMilliseconds <= viewport.EndMilliseconds + tolerance; count++, tickMilliseconds += intervalMilliseconds)
        {
            var x = PlaybackTimelineGeometry.PositionToX(tickMilliseconds, viewport, width);
            context.DrawLine(pen, new Point(x, baselineY - tickLength), new Point(x, baselineY));
        }
    }

    private void DrawMajorTicksAndLabels(DrawingContext context, PlaybackTimelineViewport viewport, Rect bounds, double baselineY, double tickLength, double intervalMilliseconds, Pen pen)
    {
        var labelTypeface = EnsureLabelStyle();

        var labelAreaHeight = Math.Max(0d, baselineY - tickLength);
        var tickMilliseconds = FirstAlignedTick(viewport.StartMilliseconds, intervalMilliseconds);
        var tolerance = intervalMilliseconds * 1e-9d;
        var lastLabelRight = double.NegativeInfinity;
        for (var count = 0; count < MaximumTickCount && tickMilliseconds <= viewport.EndMilliseconds + tolerance; count++, tickMilliseconds += intervalMilliseconds)
        {
            var x = PlaybackTimelineGeometry.PositionToX(tickMilliseconds, viewport, bounds.Width);
            context.DrawLine(pen, new Point(x, baselineY - tickLength), new Point(x, baselineY));

            if (labelAreaHeight <= 0d)
                continue;

            var label = GetLabel(tickMilliseconds, intervalMilliseconds, labelTypeface);
            var labelX = Math.Clamp(x - label.Width * 0.5d, 0d, Math.Max(0d, bounds.Width - label.Width));
            if (labelX < lastLabelRight + MinimumLabelGap)
                continue;

            var labelY = Math.Max(0d, (labelAreaHeight - label.Height) * 0.5d);
            context.DrawText(label, new Point(labelX, labelY));
            lastLabelRight = labelX + label.Width;
        }
    }

    private FormattedText GetLabel(double positionMilliseconds, double majorIntervalMilliseconds, Typeface typeface)
    {
        var fractionDigits = ResolveFractionDigits(majorIntervalMilliseconds);
        var roundedPositionMilliseconds = (long)Math.Round(positionMilliseconds, MidpointRounding.AwayFromZero);
        var key = new LabelCacheKey(roundedPositionMilliseconds, fractionDigits);
        if (_labelCache.TryGetValue(key, out var cached))
            return cached;

        if (_labelCache.Count >= MaximumLabelCacheEntries)
            _labelCache.Clear();

        var label = new FormattedText(
            FormatTime(roundedPositionMilliseconds, fractionDigits),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(1d, FontSize),
            Foreground ?? Brushes.White);
        _labelCache.Add(key, label);
        return label;
    }

    private Typeface EnsureLabelStyle()
    {
        var foreground = Foreground ?? Brushes.White;
        var styleKey = new LabelStyleKey(FontFamily, Math.Max(1d, FontSize), FontStyle, FontWeight, FontStretch, foreground);
        if (_labelTypeface is { } typeface && styleKey.Equals(_labelStyleKey))
            return typeface;

        _labelStyleKey = styleKey;
        typeface = new Typeface(styleKey.FontFamily, styleKey.FontStyle, styleKey.FontWeight, styleKey.FontStretch);
        _labelTypeface = typeface;
        _labelCache.Clear();
        return typeface;
    }

    private Pen GetTickPen()
    {
        var brush = TickBrush ?? Brushes.Gray;
        var thickness = Math.Max(0.5d, TickThickness);
        if (_tickPen is null || !ReferenceEquals(_cachedTickBrush, brush) || Math.Abs(_cachedTickThickness - thickness) > double.Epsilon)
        {
            _cachedTickBrush = brush;
            _cachedTickThickness = thickness;
            _tickPen = new Pen(brush, thickness);
        }

        return _tickPen;
    }

    private Pen GetMinorTickPen()
    {
        var brush = MinorTickBrush ?? TickBrush ?? Brushes.Gray;
        var thickness = Math.Max(0.5d, TickThickness);
        if (_minorTickPen is null || !ReferenceEquals(_cachedMinorTickBrush, brush) || Math.Abs(_minorTickPen.Thickness - thickness) > double.Epsilon)
        {
            _cachedMinorTickBrush = brush;
            _minorTickPen = new Pen(brush, thickness);
        }

        return _minorTickPen;
    }

    private static TickScale ResolveTickScale(double durationMilliseconds, double width)
    {
        var requestedInterval = Math.Max(1d, durationMilliseconds * TargetMajorTickSpacing / width);
        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(requestedInterval)));
        var normalizedInterval = requestedInterval / magnitude;

        double majorMultiplier;
        int minorDivisions;
        if (normalizedInterval <= 1d)
        {
            majorMultiplier = 1d;
            minorDivisions = 5;
        }
        else if (normalizedInterval <= 2d)
        {
            majorMultiplier = 2d;
            minorDivisions = 4;
        }
        else if (normalizedInterval <= 5d)
        {
            majorMultiplier = 5d;
            minorDivisions = 5;
        }
        else
        {
            majorMultiplier = 10d;
            minorDivisions = 5;
        }

        var majorIntervalMilliseconds = majorMultiplier * magnitude;
        if (!double.IsFinite(majorIntervalMilliseconds))
            majorIntervalMilliseconds = durationMilliseconds;

        return new TickScale(majorIntervalMilliseconds, majorIntervalMilliseconds / minorDivisions);
    }

    private static double FirstAlignedTick(double startMilliseconds, double intervalMilliseconds)
    {
        var intervalIndex = Math.Ceiling(startMilliseconds / intervalMilliseconds - 1e-9d);
        return intervalIndex * intervalMilliseconds;
    }

    private static int ResolveFractionDigits(double intervalMilliseconds)
    {
        if (intervalMilliseconds >= 1_000d)
            return 0;
        if (intervalMilliseconds >= 100d)
            return 1;
        if (intervalMilliseconds >= 10d)
            return 2;
        return 3;
    }

    private static string FormatTime(long totalMilliseconds, int fractionDigits)
    {
        totalMilliseconds = Math.Max(0L, totalMilliseconds);
        var hours = totalMilliseconds / 3_600_000L;
        var minutes = totalMilliseconds / 60_000L;
        var displayMinutes = hours > 0L ? minutes % 60L : minutes;
        var seconds = totalMilliseconds / 1_000L % 60L;
        var milliseconds = totalMilliseconds % 1_000L;

        return (hours > 0L, fractionDigits) switch
        {
            (true, 0) => string.Create(CultureInfo.InvariantCulture, $"{hours}:{displayMinutes:00}:{seconds:00}"),
            (true, 1) => string.Create(CultureInfo.InvariantCulture, $"{hours}:{displayMinutes:00}:{seconds:00}.{milliseconds / 100L}"),
            (true, 2) => string.Create(CultureInfo.InvariantCulture, $"{hours}:{displayMinutes:00}:{seconds:00}.{milliseconds / 10L:00}"),
            (true, _) => string.Create(CultureInfo.InvariantCulture, $"{hours}:{displayMinutes:00}:{seconds:00}.{milliseconds:000}"),
            (false, 0) => string.Create(CultureInfo.InvariantCulture, $"{displayMinutes}:{seconds:00}"),
            (false, 1) => string.Create(CultureInfo.InvariantCulture, $"{displayMinutes}:{seconds:00}.{milliseconds / 100L}"),
            (false, 2) => string.Create(CultureInfo.InvariantCulture, $"{displayMinutes}:{seconds:00}.{milliseconds / 10L:00}"),
            (false, _) => string.Create(CultureInfo.InvariantCulture, $"{displayMinutes}:{seconds:00}.{milliseconds:000}")
        };
    }

    private readonly record struct TickScale(double MajorIntervalMilliseconds, double MinorIntervalMilliseconds);

    private readonly record struct LabelCacheKey(long PositionMilliseconds, int FractionDigits);

    private readonly record struct LabelStyleKey(FontFamily FontFamily, double FontSize, FontStyle FontStyle, FontWeight FontWeight, FontStretch FontStretch, IBrush Foreground);
}
