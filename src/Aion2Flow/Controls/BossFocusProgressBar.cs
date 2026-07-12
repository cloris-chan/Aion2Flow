using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Cloris.Aion2Flow.Presentation;
using SkiaSharp;

namespace Cloris.Aion2Flow.Controls;

public sealed class BossFocusProgressBar : Control
{
    public static readonly DirectProperty<BossFocusProgressBar, ProgressSegment?> SegmentProperty =
        AvaloniaProperty.RegisterDirect<BossFocusProgressBar, ProgressSegment?>(
            nameof(Segment),
            control => control.Segment,
            (control, value) => control.Segment = value);

    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<BossFocusProgressBar, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> OuterShadowBrushProperty = AvaloniaProperty.Register<BossFocusProgressBar, IBrush?>(nameof(OuterShadowBrush));

    public static readonly StyledProperty<IBrush?> FrameBrushProperty = AvaloniaProperty.Register<BossFocusProgressBar, IBrush?>(nameof(FrameBrush));

    public static readonly StyledProperty<IBrush?> InnerShadowBrushProperty = AvaloniaProperty.Register<BossFocusProgressBar, IBrush?>(nameof(InnerShadowBrush));

    public static readonly StyledProperty<double> ChamferWidthProperty = AvaloniaProperty.Register<BossFocusProgressBar, double>(nameof(ChamferWidth), 8d);

    public static readonly StyledProperty<double> FrameThicknessProperty = AvaloniaProperty.Register<BossFocusProgressBar, double>(nameof(FrameThickness), 2d);

    public static readonly StyledProperty<double> FlowSpeedProperty = AvaloniaProperty.Register<BossFocusProgressBar, double>(nameof(FlowSpeed), 1d);

    public static readonly StyledProperty<double> FlowStrengthProperty = AvaloniaProperty.Register<BossFocusProgressBar, double>(nameof(FlowStrength), 1d);

    public static readonly DirectProperty<BossFocusProgressBar, bool> IsAnimationEnabledProperty =
        AvaloniaProperty.RegisterDirect<BossFocusProgressBar, bool>(
            nameof(IsAnimationEnabled),
            control => control.IsAnimationEnabled,
            (control, value) => control.IsAnimationEnabled = value);

    private CompositionCustomVisual? _compositionVisual;
    private BossFocusProgressBarVisualState? _publishedState;

    static BossFocusProgressBar()
    {
        AffectsRender<BossFocusProgressBar>(
            BackgroundProperty,
            OuterShadowBrushProperty,
            FrameBrushProperty,
            InnerShadowBrushProperty,
            ChamferWidthProperty,
            FrameThicknessProperty);
    }

    public ProgressSegment? Segment
    {
        get;
        set => SetAndRaise(SegmentProperty, ref field, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? OuterShadowBrush
    {
        get => GetValue(OuterShadowBrushProperty);
        set => SetValue(OuterShadowBrushProperty, value);
    }

    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    public IBrush? InnerShadowBrush
    {
        get => GetValue(InnerShadowBrushProperty);
        set => SetValue(InnerShadowBrushProperty, value);
    }

    public double ChamferWidth
    {
        get => GetValue(ChamferWidthProperty);
        set => SetValue(ChamferWidthProperty, value);
    }

    public double FrameThickness
    {
        get => GetValue(FrameThicknessProperty);
        set => SetValue(FrameThicknessProperty, value);
    }

    public double FlowSpeed
    {
        get => GetValue(FlowSpeedProperty);
        set => SetValue(FlowSpeedProperty, value);
    }

    public double FlowStrength
    {
        get => GetValue(FlowStrengthProperty);
        set => SetValue(FlowStrengthProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get;
        set => SetAndRaise(IsAnimationEnabledProperty, ref field, value);
    } = true;

    public override void Render(DrawingContext context)
    {
        if (_compositionVisual is not null)
            return;

        BossFocusProgressBarVisualHandler.RenderStatic(context, new Rect(Bounds.Size), CreateVisualState());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
            return;

        _ = BossFocusMaterialTextureData.Shared;
        _compositionVisual = compositor.CreateCustomVisual(new BossFocusProgressBarVisualHandler());
        _compositionVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _compositionVisual);
        PublishVisualState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_compositionVisual is not null)
        {
            _compositionVisual.SendHandlerMessage(BossFocusProgressBarVisualHandler.StopMessage);
            ElementComposition.SetElementChildVisual(this, null);
            _compositionVisual = null;
        }

        _publishedState = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && _compositionVisual is not null)
            _compositionVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);

        if (change.Property == SegmentProperty && _compositionVisual is null)
            InvalidateVisual();

        if (change.Property == SegmentProperty ||
            change.Property == BackgroundProperty ||
            change.Property == OuterShadowBrushProperty ||
            change.Property == FrameBrushProperty ||
            change.Property == InnerShadowBrushProperty ||
            change.Property == ChamferWidthProperty ||
            change.Property == FrameThicknessProperty ||
            change.Property == FlowSpeedProperty ||
            change.Property == FlowStrengthProperty ||
            change.Property == IsAnimationEnabledProperty)
        {
            PublishVisualState();
        }
    }

    internal BossFocusProgressBarVisualState CreateVisualStateForDiagnostics() => CreateVisualState();

    private void PublishVisualState()
    {
        if (_compositionVisual is null)
            return;

        var state = CreateVisualState();
        if (_publishedState == state)
            return;

        _publishedState = state;
        _compositionVisual.SendHandlerMessage(state);
    }

    private BossFocusProgressBarVisualState CreateVisualState()
    {
        var segment = Segment;
        return new BossFocusProgressBarVisualState(
            segment.HasValue ? (float)Math.Clamp(segment.Value.Ratio, 0d, 1d) : 0f,
            segment.HasValue
                ? HudBrushColor.Resolve(segment.Value.Brush, Colors.Transparent)
                : Colors.Transparent,
            HudBrushColor.Resolve(Background, Colors.Transparent),
            HudBrushColor.Resolve(OuterShadowBrush, Colors.Transparent),
            HudBrushColor.Resolve(FrameBrush, Colors.Transparent),
            HudBrushColor.Resolve(InnerShadowBrush, Colors.Transparent),
            (float)Math.Max(0d, ChamferWidth),
            (float)Math.Max(0d, FrameThickness),
            (float)Math.Clamp(FlowSpeed, 0d, 4d),
            (float)Math.Clamp(FlowStrength, 0d, 1d),
            IsAnimationEnabled);
    }
}

internal readonly record struct BossFocusProgressBarVisualState(
    float Ratio,
    Color FillColor,
    Color TrackColor,
    Color OuterShadowColor,
    Color FrameColor,
    Color InnerShadowColor,
    float ChamferWidth,
    float FrameThickness,
    float FlowSpeed,
    float FlowStrength,
    bool IsAnimationEnabled)
{
    internal bool ShouldAnimate =>
        IsAnimationEnabled && Ratio > 0f && FillColor.A > 0 && FlowSpeed > 0f && FlowStrength > 0f;
}

internal readonly record struct BossFocusHexagonVertices(
    Vector2 LeftPoint,
    Vector2 TopLeft,
    Vector2 TopRight,
    Vector2 RightPoint,
    Vector2 BottomRight,
    Vector2 BottomLeft)
{
    internal static BossFocusHexagonVertices Create(float x, float y, float width, float height, float chamferWidth)
    {
        var resolvedWidth = Math.Max(0f, width);
        var resolvedHeight = Math.Max(0f, height);
        var resolvedChamfer = Math.Clamp(chamferWidth, 0f, resolvedWidth * 0.5f);
        var right = x + resolvedWidth;
        var bottom = y + resolvedHeight;
        var middle = y + resolvedHeight * 0.5f;
        return new BossFocusHexagonVertices(
            new Vector2(x, middle),
            new Vector2(x + resolvedChamfer, y),
            new Vector2(right - resolvedChamfer, y),
            new Vector2(right, middle),
            new Vector2(right - resolvedChamfer, bottom),
            new Vector2(x + resolvedChamfer, bottom));
    }
}

internal readonly record struct BossFocusProgressBarGeometry(
    BossFocusHexagonVertices Shadow,
    BossFocusHexagonVertices Outer,
    BossFocusHexagonVertices Inner,
    float OuterInset,
    float InnerLeft,
    float InnerTop,
    float InnerWidth,
    float InnerHeight)
{
    private const float OuterShadowInset = 1.5f;

    internal static BossFocusProgressBarGeometry Create(float width, float height, float chamferWidth, float frameThickness)
    {
        var resolvedWidth = Math.Max(0f, width);
        var resolvedHeight = Math.Max(0f, height);
        var outerInset = Math.Min(OuterShadowInset, Math.Min(resolvedWidth, resolvedHeight) * 0.5f);
        var outerWidth = Math.Max(0f, resolvedWidth - outerInset * 2f);
        var outerHeight = Math.Max(0f, resolvedHeight - outerInset * 2f);
        var frameInset = Math.Clamp(frameThickness, 0f, Math.Min(outerWidth, outerHeight) * 0.5f);
        var innerLeft = outerInset + frameInset;
        var innerTop = outerInset + frameInset;
        var innerWidth = Math.Max(0f, outerWidth - frameInset * 2f);
        var innerHeight = Math.Max(0f, outerHeight - frameInset * 2f);
        var outerChamfer = ScaleChamfer(chamferWidth, outerHeight, resolvedHeight);
        var innerChamfer = ScaleChamfer(chamferWidth, innerHeight, resolvedHeight);
        return new BossFocusProgressBarGeometry(
            BossFocusHexagonVertices.Create(0f, 0f, resolvedWidth, resolvedHeight, chamferWidth),
            BossFocusHexagonVertices.Create(outerInset, outerInset, outerWidth, outerHeight, outerChamfer),
            BossFocusHexagonVertices.Create(innerLeft, innerTop, innerWidth, innerHeight, innerChamfer),
            outerInset,
            innerLeft,
            innerTop,
            innerWidth,
            innerHeight);
    }

    private static float ScaleChamfer(float chamferWidth, float layerHeight, float totalHeight) =>
        totalHeight > 0f ? Math.Max(0f, chamferWidth) * layerHeight / totalHeight : 0f;
}

internal sealed class BossFocusProgressBarVisualHandler : CompositionCustomVisualHandler
{
    internal static readonly object StopMessage = new();

    private BossFocusProgressBarVisualState? _state;
    private ImmutableSolidColorBrush _outerShadowBrush = new(Colors.Transparent);
    private ImmutableSolidColorBrush _frameBrush = new(Colors.Transparent);
    private ImmutableSolidColorBrush _innerShadowBrush = new(Colors.Transparent);
    private ImmutableSolidColorBrush _trackBrush = new(Colors.Transparent);
    private ImmutableSolidColorBrush _fillBrush = new(Colors.Transparent);
    private HudShaderInstance? _shaderInstance;
    private SKPaint? _outerShadowPaint;
    private SKPaint? _framePaint;
    private SKPaint? _innerShadowPaint;
    private SKPaint? _trackPaint;
    private SKPaint? _fillPaint;
    private SKPaint? _flowPaint;
    private SKPaint? _edgePaint;
    private SKPath? _outerPath;
    private SKPath? _innerPath;
    private BossFocusProgressBarGeometry _geometry;
    private float _geometryWidth = -1f;
    private float _geometryHeight = -1f;
    private float _geometryChamferWidth = -1f;
    private float _geometryFrameThickness = -1f;
    private bool _stopped;

    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, StopMessage))
        {
            _stopped = true;
            DisposeSkiaResources();
            return;
        }

        if (message is not BossFocusProgressBarVisualState state || _stopped)
            return;

        _state = state;
        _outerShadowBrush = new ImmutableSolidColorBrush(state.OuterShadowColor);
        _frameBrush = new ImmutableSolidColorBrush(state.FrameColor);
        _innerShadowBrush = new ImmutableSolidColorBrush(state.InnerShadowColor);
        _trackBrush = new ImmutableSolidColorBrush(state.TrackColor);
        _fillBrush = new ImmutableSolidColorBrush(state.FillColor);

        Invalidate();
        if (state.ShouldAnimate)
            RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_stopped || _state?.ShouldAnimate != true)
            return;

        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext drawingContext)
    {
        if (_state is not { } state || _stopped)
            return;

        var size = EffectiveSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var skiaFeature = drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (skiaFeature is null)
        {
            RenderStatic(drawingContext, new Rect(0d, 0d, size.X, size.Y), state, _outerShadowBrush, _frameBrush, _innerShadowBrush, _trackBrush, _fillBrush);
            return;
        }

        using var lease = skiaFeature.Lease();
        RenderSkia(lease.SkCanvas, (float)size.X, (float)size.Y, state);
    }

    internal static void RenderStatic(DrawingContext context, Rect bounds, BossFocusProgressBarVisualState state)
    {
        if (bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        var geometry = BossFocusProgressBarGeometry.Create((float)bounds.Width, (float)bounds.Height, state.ChamferWidth, state.FrameThickness);
        var shadow = CreateGeometry(geometry.Shadow);
        var outer = CreateGeometry(geometry.Outer);
        var inner = CreateGeometry(geometry.Inner);
        context.DrawGeometry(new ImmutableSolidColorBrush(state.OuterShadowColor), null, shadow);
        context.DrawGeometry(new ImmutableSolidColorBrush(state.FrameColor), null, outer);
        context.DrawGeometry(new ImmutableSolidColorBrush(state.TrackColor), null, inner);

        using var clip = context.PushGeometryClip(inner);
        var fillWidth = geometry.InnerWidth * state.Ratio;
        if (fillWidth > 0f)
            context.FillRectangle(new ImmutableSolidColorBrush(state.FillColor), new Rect(geometry.InnerLeft, geometry.InnerTop, fillWidth, geometry.InnerHeight));

        context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(state.InnerShadowColor), 1.25d), inner);
    }

    internal static SKRuntimeEffect CompileShaderForDiagnostics() => BossFocusProgressBarSkiaProgram.CompileShaderEffect();

    internal static SKRuntimeEffect CompileBlenderForDiagnostics() => BossFocusProgressBarSkiaProgram.CompileBlenderEffect();

    private static void RenderStatic(
        ImmediateDrawingContext context,
        Rect bounds,
        BossFocusProgressBarVisualState state,
        IImmutableBrush outerShadow,
        IImmutableBrush frame,
        IImmutableBrush innerShadow,
        IImmutableBrush track,
        IImmutableBrush fill)
    {
        var geometry = BossFocusProgressBarGeometry.Create((float)bounds.Width, (float)bounds.Height, state.ChamferWidth, state.FrameThickness);
        context.FillRectangle(outerShadow, bounds);
        var outerBounds = new Rect(
            geometry.OuterInset,
            geometry.OuterInset,
            Math.Max(0d, bounds.Width - geometry.OuterInset * 2d),
            Math.Max(0d, bounds.Height - geometry.OuterInset * 2d));
        context.FillRectangle(frame, outerBounds);
        var innerBounds = new Rect(geometry.InnerLeft, geometry.InnerTop, geometry.InnerWidth, geometry.InnerHeight);
        context.FillRectangle(innerShadow, innerBounds);
        var contentBounds = innerBounds.Deflate(0.75d);
        context.FillRectangle(track, contentBounds);
        using var clip = context.PushClip(contentBounds);

        var fillWidth = contentBounds.Width * state.Ratio;
        if (fillWidth > 0d)
            context.FillRectangle(fill, new Rect(contentBounds.Left, contentBounds.Top, fillWidth, contentBounds.Height));
    }

    private void RenderSkia(SKCanvas canvas, float width, float height, BossFocusProgressBarVisualState state)
    {
        EnsureSkiaResources();
        EnsureGeometry(width, height, state.ChamferWidth, state.FrameThickness);

        _outerShadowPaint!.Color = ToSkColor(state.OuterShadowColor);
        canvas.DrawPath(_outerPath!, _outerShadowPaint);
        _framePaint!.Color = ToSkColor(state.FrameColor);
        canvas.DrawPath(_outerPath!, _framePaint);
        _trackPaint!.Color = ToSkColor(state.TrackColor);
        canvas.DrawPath(_innerPath!, _trackPaint);

        canvas.Save();
        canvas.ClipPath(_innerPath!, SKClipOperation.Intersect, antialias: true);

        var filledRight = _geometry.InnerLeft + _geometry.InnerWidth * state.Ratio;
        if (filledRight > _geometry.InnerLeft)
        {
            _fillPaint!.Color = ToSkColor(state.FillColor);
            canvas.DrawRect(
                _geometry.InnerLeft,
                _geometry.InnerTop,
                filledRight,
                _geometry.InnerTop + _geometry.InnerHeight,
                _fillPaint);
        }

        if (state.ShouldAnimate)
            DrawFlowOverlay(canvas, filledRight, state);

        canvas.Restore();

        canvas.Save();
        canvas.ClipPath(_innerPath!, SKClipOperation.Intersect, antialias: true);
        _innerShadowPaint!.Color = ToSkColor(state.InnerShadowColor);
        canvas.DrawPath(_innerPath!, _innerShadowPaint);
        canvas.Restore();

        _edgePaint!.Color = WithOpacity(state.OuterShadowColor, 0.78f);
        canvas.DrawPath(_outerPath!, _edgePaint);
    }

    private void DrawFlowOverlay(SKCanvas canvas, float filledRight, BossFocusProgressBarVisualState state)
    {
        var instance = _shaderInstance!;
        instance.Uniforms["origin"] = new SKPoint(_geometry.InnerLeft, _geometry.InnerTop);
        instance.Uniforms["size"] = new SKPoint(_geometry.InnerWidth, _geometry.InnerHeight);
        instance.Uniforms["time"] = (float)(CompositionNow.TotalSeconds % BossFocusProgressBarSkiaProgram.TimePeriodSeconds);
        instance.Uniforms["speed"] = state.FlowSpeed;
        instance.Uniforms["strength"] = state.FlowStrength;

        using var shader = instance.Build();
        _flowPaint!.Shader = shader;
        try
        {
            canvas.DrawRect(_geometry.InnerLeft, _geometry.InnerTop, filledRight, _geometry.InnerTop + _geometry.InnerHeight, _flowPaint);
        }
        finally
        {
            _flowPaint.Shader = null;
        }
    }

    private void EnsureSkiaResources()
    {
        if (_shaderInstance is not null)
            return;

        var program = BossFocusProgressBarSkiaProgram.GetForCurrentThread();
        _shaderInstance = program.CreateInstance();
        _outerShadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.35f)
        };
        _framePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _innerShadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.25f,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.55f)
        };
        _trackPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _fillPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        _flowPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill, Blender = program.Blender };
        _edgePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };
        _outerPath = new SKPath();
        _innerPath = new SKPath();
    }

    private void EnsureGeometry(float width, float height, float chamferWidth, float frameThickness)
    {
        if (width == _geometryWidth &&
            height == _geometryHeight &&
            chamferWidth == _geometryChamferWidth &&
            frameThickness == _geometryFrameThickness)
        {
            return;
        }

        _geometryWidth = width;
        _geometryHeight = height;
        _geometryChamferWidth = chamferWidth;
        _geometryFrameThickness = frameThickness;
        _geometry = BossFocusProgressBarGeometry.Create(width, height, chamferWidth, frameThickness);
        SetPath(_outerPath!, _geometry.Outer);
        SetPath(_innerPath!, _geometry.Inner);
    }

    private void DisposeSkiaResources()
    {
        _outerPath?.Dispose();
        _outerPath = null;
        _innerPath?.Dispose();
        _innerPath = null;
        _edgePaint?.Dispose();
        _edgePaint = null;
        _innerShadowPaint?.Dispose();
        _innerShadowPaint = null;
        _flowPaint?.Dispose();
        _flowPaint = null;
        _fillPaint?.Dispose();
        _fillPaint = null;
        _trackPaint?.Dispose();
        _trackPaint = null;
        _framePaint?.Dispose();
        _framePaint = null;
        _outerShadowPaint?.Dispose();
        _outerShadowPaint = null;
        _shaderInstance?.Dispose();
        _shaderInstance = null;
    }

    private static StreamGeometry CreateGeometry(BossFocusHexagonVertices vertices)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(ToPoint(vertices.LeftPoint), isFilled: true);
        context.LineTo(ToPoint(vertices.TopLeft));
        context.LineTo(ToPoint(vertices.TopRight));
        context.LineTo(ToPoint(vertices.RightPoint));
        context.LineTo(ToPoint(vertices.BottomRight));
        context.LineTo(ToPoint(vertices.BottomLeft));
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static void SetPath(SKPath path, BossFocusHexagonVertices vertices)
    {
        path.Reset();
        path.MoveTo(vertices.LeftPoint.X, vertices.LeftPoint.Y);
        path.LineTo(vertices.TopLeft.X, vertices.TopLeft.Y);
        path.LineTo(vertices.TopRight.X, vertices.TopRight.Y);
        path.LineTo(vertices.RightPoint.X, vertices.RightPoint.Y);
        path.LineTo(vertices.BottomRight.X, vertices.BottomRight.Y);
        path.LineTo(vertices.BottomLeft.X, vertices.BottomLeft.Y);
        path.Close();
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

    private static SKColor WithOpacity(Color color, float opacity) => new(color.R, color.G, color.B, (byte)Math.Clamp(Math.Round(color.A * opacity), 0d, byte.MaxValue));
}

internal sealed class BossFocusProgressBarSkiaProgram
{
    internal const float TimePeriodSeconds = 999f;

    private const string ShaderSource = """
        uniform float2 origin;
        uniform float2 size;
        uniform float2 fineTextureSize;
        uniform float2 broadTextureSize;
        uniform float2 distortionTextureSize;
        uniform float time;
        uniform float speed;
        uniform float strength;
        uniform shader fineTrail;
        uniform shader broadTrail;
        uniform shader distortionMap;

        half srgbToLinear(half value) {
            return value <= 0.04045
                ? value / 12.92
                : pow((value + 0.055) / 1.055, 2.4);
        }

        half4 main(float2 position) {
            float2 uv = clamp((position - origin) / max(size, float2(1.0)), 0.0, 1.0);
            float2 fitDivisor = clamp(
                float2(size.y / max(size.x, 1.0), size.x / max(size.y, 1.0)),
                float2(0.0001),
                float2(1.0));
            float2 fittedUv = uv / fitDivisor;
            float materialTime = mod(max(time, 0.0), 999.0) * speed;

            float2 distortionUv =
                (fittedUv + materialTime * float2(-1.3, 0.0) + float2(0.5)) *
                float2(0.068, 0.1);
            half2 distortionSample = distortionMap.eval(distortionUv * distortionTextureSize).rg;
            half2 distortion = half2(
                srgbToLinear(distortionSample.r),
                srgbToLinear(distortionSample.g));
            float2 centeredDistortion = float2(distortion) + float2(-0.5);

            const float2 fineTiling = float2(24.0, 15.0);
            float2 fineUv =
                (fittedUv + materialTime * float2(0.389589, 0.0) + float2(-4.0, 1.0)) / fineTiling +
                float2(0.5) - float2(0.5) / fineTiling +
                centeredDistortion * -0.073333;
            half fine = srgbToLinear(fineTrail.eval(fineUv * fineTextureSize).b);

            const float2 broadTiling = float2(24.0, 10.0);
            float2 broadUv =
                (fittedUv + materialTime * float2(0.3, 0.0)) / broadTiling +
                float2(0.5) - float2(0.5) / broadTiling +
                centeredDistortion * -0.101333;
            half broad = broadTrail.eval(broadUv * broadTextureSize).b;

            half materialFactor = max((fine + 0.6) * (broad * 5.0 + 0.7), 0.0);
            return half4(materialFactor * 0.1, half(strength), 0.0, 1.0);
        }
        """;

    private const string BlenderSource = """
        half srgbToLinear(half value) {
            return value <= 0.04045
                ? value / 12.92
                : pow((value + 0.055) / 1.055, 2.4);
        }

        half linearToSrgb(half value) {
            return value <= 0.0031308
                ? value * 12.92
                : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
        }

        half3 srgbToLinear(half3 value) {
            return half3(
                srgbToLinear(value.r),
                srgbToLinear(value.g),
                srgbToLinear(value.b));
        }

        half3 linearToSrgb(half3 value) {
            return half3(
                linearToSrgb(value.r),
                linearToSrgb(value.g),
                linearToSrgb(value.b));
        }

        half4 main(half4 source, half4 destination) {
            half strength = source.g;
            half destinationAlpha = destination.a;
            half3 baseSrgb = destinationAlpha > 0.0
                ? destination.rgb / destinationAlpha
                : half3(0.0);
            half3 baseLinear = srgbToLinear(clamp(baseSrgb, 0.0, 1.0));
            half materialFactor = source.r * 10.0;
            half3 materialLinear = clamp(baseLinear * baseLinear * materialFactor, 0.0, 1.0);
            half materialAlpha = destinationAlpha * destinationAlpha;
            half4 materialColor = half4(linearToSrgb(materialLinear) * materialAlpha, materialAlpha);
            return mix(destination, materialColor, strength);
        }
        """;

    [ThreadStatic]
    // Native shader resources stay on the compositor thread that created them.
    private static BossFocusProgressBarSkiaProgram? s_current;

    private readonly SKRuntimeEffect _effect;
    private readonly SKRuntimeEffect _blenderEffect;
    private readonly BossFocusMaterialSkiaTextures _textures;

    private BossFocusProgressBarSkiaProgram()
    {
        _effect = CompileShaderEffect();
        _blenderEffect = CompileBlenderEffect();
        Blender = _blenderEffect.ToBlender();
        _textures = BossFocusMaterialSkiaTextures.GetForCurrentThread();
    }

    internal static BossFocusProgressBarSkiaProgram GetForCurrentThread() => s_current ??= new BossFocusProgressBarSkiaProgram();

    internal SKBlender Blender { get; }

    internal static SKRuntimeEffect CompileShaderEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(ShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile boss focus progress bar shader: {errors}");
    }

    internal static SKRuntimeEffect CompileBlenderEffect()
    {
        var effect = SKRuntimeEffect.CreateBlender(BlenderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile boss focus progress bar blender: {errors}");
    }

    internal HudShaderInstance CreateInstance()
    {
        var instance = new HudShaderInstance(_effect);
        instance.Children["fineTrail"] = _textures.FineTrailShader;
        instance.Children["broadTrail"] = _textures.BroadTrailShader;
        instance.Children["distortionMap"] = _textures.GradientShader;
        instance.Uniforms["fineTextureSize"] = _textures.FineTextureSize;
        instance.Uniforms["broadTextureSize"] = _textures.BroadTextureSize;
        instance.Uniforms["distortionTextureSize"] = _textures.GradientTextureSize;
        return instance;
    }
}
