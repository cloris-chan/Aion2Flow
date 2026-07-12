using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace Cloris.Aion2Flow.Controls;

public sealed class QuestSelectionHighlight : Control
{
    public static readonly DirectProperty<QuestSelectionHighlight, bool> IsActiveProperty =
        AvaloniaProperty.RegisterDirect<QuestSelectionHighlight, bool>(
            nameof(IsActive),
            control => control.IsActive,
            (control, value) => control.IsActive = value);

    public static readonly StyledProperty<IBrush?> SelectionBackgroundProperty =
        AvaloniaProperty.Register<QuestSelectionHighlight, IBrush?>(nameof(SelectionBackground));

    private CompositionCustomVisual? _compositionVisual;
    private ImmutableSolidColorBrush _fallbackBackgroundBrush = new(Colors.Transparent);
    private Color _fallbackBackgroundColor = Colors.Transparent;
    private QuestSelectionHighlightVisualState _publishedState;
    private bool _hasPublishedState;

    static QuestSelectionHighlight()
    {
        AffectsRender<QuestSelectionHighlight>(SelectionBackgroundProperty);
    }

    public bool IsActive
    {
        get;
        set => SetAndRaise(IsActiveProperty, ref field, value);
    }

    public IBrush? SelectionBackground
    {
        get => GetValue(SelectionBackgroundProperty);
        set => SetValue(SelectionBackgroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (_compositionVisual is not null)
            return;

        var state = CreateVisualState();
        if (!state.IsActive)
            return;

        if (state.BackgroundColor != _fallbackBackgroundColor)
        {
            _fallbackBackgroundColor = state.BackgroundColor;
            _fallbackBackgroundBrush = new ImmutableSolidColorBrush(state.BackgroundColor);
        }

        QuestSelectionHighlightVisualHandler.RenderStatic(context, new Rect(Bounds.Size), _fallbackBackgroundBrush);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
            return;

        _ = QuestFlowMaterialTextureData.Shared;
        _ = QuestSelectionParticleTextureData.Shared;
        _ = QuestCompletionEdgeTextureData.Shared;
        _compositionVisual = compositor.CreateCustomVisual(new QuestSelectionHighlightVisualHandler());
        _compositionVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        _compositionVisual.Visible = IsActive;
        ElementComposition.SetElementChildVisual(this, _compositionVisual);
        PublishVisualState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_compositionVisual is not null)
        {
            _compositionVisual.SendHandlerMessage(QuestSelectionHighlightVisualHandler.StopMessage);
            ElementComposition.SetElementChildVisual(this, null);
            _compositionVisual = null;
        }

        _hasPublishedState = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && _compositionVisual is not null)
            _compositionVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        if (change.Property == IsActiveProperty && _compositionVisual is not null)
            _compositionVisual.Visible = IsActive;

        if (change.Property == IsActiveProperty ||
            change.Property == SelectionBackgroundProperty)
        {
            if (_compositionVisual is null)
                InvalidateVisual();
            PublishVisualState();
        }
    }

    internal QuestSelectionHighlightVisualState CreateVisualStateForDiagnostics() => CreateVisualState();

    private void PublishVisualState()
    {
        if (_compositionVisual is null)
            return;

        var state = CreateVisualState();
        if (_hasPublishedState && state == _publishedState)
            return;

        _publishedState = state;
        _hasPublishedState = true;
        _compositionVisual.SendHandlerMessage(state);
    }

    private QuestSelectionHighlightVisualState CreateVisualState()
    {
        return new QuestSelectionHighlightVisualState(
            IsActive,
            HudBrushColor.Resolve(SelectionBackground, Colors.Transparent));
    }
}

internal readonly record struct QuestSelectionHighlightVisualState(
    bool IsActive,
    Color BackgroundColor)
{
    internal bool ShouldAnimate => IsActive;
}

internal sealed class QuestSelectionHighlightVisualHandler : CompositionCustomVisualHandler
{
    internal const float FlowLayerScale = 1.5f;
    internal static readonly object StopMessage = new();
    private static readonly ImmutablePen EdgeBaselinePen = new(new ImmutableSolidColorBrush(QuestCompletionEdgeSkiaProgram.BaselineColor), 1d);

    private QuestSelectionHighlightVisualState _state;
    private ImmutableSolidColorBrush _backgroundBrush = new(Colors.Transparent);
    private HudShaderInstance? _waveShaderInstance;
    private HudShaderInstance? _particleShaderInstance;
    private HudShaderInstance? _topEdgeShaderInstance;
    private HudShaderInstance? _bottomEdgeShaderInstance;
    private HudShaderInstance? _edgeBaselineShaderInstance;
    private SKPaint? _backgroundPaint;
    private SKPaint? _wavePaint;
    private SKPaint? _particlePaint;
    private SKPaint? _edgePaint;
    private SKPaint? _edgeBaselinePaint;
    private bool _hasState;
    private bool _stopped;

    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, StopMessage))
        {
            _stopped = true;
            DisposeSkiaResources();
            return;
        }

        if (message is not QuestSelectionHighlightVisualState state || _stopped)
            return;

        if (!_hasState || state.BackgroundColor != _state.BackgroundColor)
            _backgroundBrush = new ImmutableSolidColorBrush(state.BackgroundColor);

        _state = state;
        _hasState = true;
        Invalidate();
        if (state.ShouldAnimate)
            RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_stopped || !_state.ShouldAnimate)
            return;

        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext drawingContext)
    {
        if (!_hasState || _stopped || !_state.IsActive)
            return;

        var size = EffectiveSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var bounds = new Rect(0d, 0d, size.X, size.Y);
        var skiaFeature = drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (skiaFeature is null)
        {
            RenderStatic(drawingContext, bounds, _backgroundBrush);
            return;
        }

        using var lease = skiaFeature.Lease();
        RenderSkia(lease.SkCanvas, (float)size.X, (float)size.Y);
    }

    internal static void RenderStatic(DrawingContext context, Rect bounds, IImmutableBrush background)
    {
        if (bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        context.FillRectangle(background, bounds);
        const double inset = 0.5d;
        context.DrawLine(EdgeBaselinePen, new Point(bounds.Left, bounds.Top + inset), new Point(bounds.Right, bounds.Top + inset));
        context.DrawLine(EdgeBaselinePen, new Point(bounds.Left, bounds.Bottom - inset), new Point(bounds.Right, bounds.Bottom - inset));
    }

    internal static SKRuntimeEffect CompileWaveShaderForDiagnostics() => QuestFlowMaterialSkiaProgram.CompileEffect();

    internal static SKRuntimeEffect CompileParticleShaderForDiagnostics() => QuestSelectionParticleSkiaProgram.CompileEffect();

    internal static SKRuntimeEffect CompileEdgeShaderForDiagnostics() => QuestCompletionEdgeSkiaProgram.CompileEffect();

    internal static SKRuntimeEffect CompileEdgeBaselineShaderForDiagnostics() => QuestCompletionEdgeSkiaProgram.CompileBaselineEffect();

    internal static SKRuntimeEffect CompileBlenderForDiagnostics() => QuestFlowMaterialSkiaProgram.CompileBlenderEffect();

    private static void RenderStatic(
        ImmediateDrawingContext context,
        Rect bounds,
        IImmutableBrush background)
    {
        context.FillRectangle(background, bounds);
        const double inset = 0.5d;
        context.DrawLine(EdgeBaselinePen, new Point(bounds.Left, bounds.Top + inset), new Point(bounds.Right, bounds.Top + inset));
        context.DrawLine(EdgeBaselinePen, new Point(bounds.Left, bounds.Bottom - inset), new Point(bounds.Right, bounds.Bottom - inset));
    }

    private void RenderSkia(SKCanvas canvas, float width, float height)
    {
        EnsureSkiaResources();

        _backgroundPaint!.Color = ToSkColor(_state.BackgroundColor);
        canvas.DrawRect(0f, 0f, width, height, _backgroundPaint);

        var time = (float)(CompositionNow.TotalSeconds % QuestFlowMaterialSkiaProgram.TimePeriodSeconds);
        var flowBounds = ResolveFlowLayerBounds(width, height);
        DrawParticleLayer(canvas, flowBounds, time);
        DrawWaveLayer(canvas, flowBounds, time);
        DrawEdges(canvas, width, height, time);
    }

    internal static SKRect ResolveFlowLayerBounds(float width, float height)
    {
        var scaledWidth = Math.Max(0f, width) * FlowLayerScale;
        var scaledHeight = Math.Max(0f, height) * FlowLayerScale;
        var top = (Math.Max(0f, height) - scaledHeight) * 0.5f;
        return new SKRect(0f, top, scaledWidth, top + scaledHeight);
    }

    private void DrawParticleLayer(SKCanvas canvas, SKRect bounds, float time)
    {
        var instance = _particleShaderInstance!;
        instance.Uniforms["origin"] = new SKPoint(bounds.Left, bounds.Top);
        instance.Uniforms["size"] = new SKPoint(bounds.Width, bounds.Height);
        instance.Uniforms["time"] = time;
        instance.Uniforms["flowColor"] = QuestSelectionParticleSkiaProgram.ClientTintColor;

        using var shader = instance.Build();
        _particlePaint!.Shader = shader;
        try
        {
            canvas.DrawRect(bounds, _particlePaint);
        }
        finally
        {
            _particlePaint.Shader = null;
        }
    }

    private void DrawWaveLayer(SKCanvas canvas, SKRect bounds, float time)
    {
        var instance = _waveShaderInstance!;
        instance.Uniforms["origin"] = new SKPoint(bounds.Left, bounds.Top);
        instance.Uniforms["size"] = new SKPoint(bounds.Width, bounds.Height);
        instance.Uniforms["time"] = time;
        instance.Uniforms["flowColor"] = QuestFlowMaterialSkiaProgram.ClientTintColor;

        using var shader = instance.Build();
        _wavePaint!.Shader = shader;
        try
        {
            canvas.DrawRect(bounds, _wavePaint);
        }
        finally
        {
            _wavePaint.Shader = null;
        }
    }

    private void DrawEdges(SKCanvas canvas, float width, float height, float time)
    {
        var baseline = _edgeBaselineShaderInstance!;
        baseline.Uniforms["originX"] = 0f;
        baseline.Uniforms["width"] = width;
        baseline.Uniforms["edgeColor"] = QuestCompletionEdgeSkiaProgram.BaselineSkColor;
        using var baselineShader = baseline.Build();
        _edgeBaselinePaint!.Shader = baselineShader;
        try
        {
            canvas.DrawRect(0f, 0f, width, QuestCompletionEdgeSkiaProgram.BaselineHeight, _edgeBaselinePaint);
            canvas.DrawRect(0f, height - QuestCompletionEdgeSkiaProgram.BaselineHeight, width, QuestCompletionEdgeSkiaProgram.BaselineHeight, _edgeBaselinePaint);
        }
        finally
        {
            _edgeBaselinePaint.Shader = null;
        }

        var halfBandHeight = QuestCompletionEdgeSkiaProgram.BandHeight * 0.5f;
        DrawEdgeBand(canvas, _topEdgeShaderInstance!, width, -halfBandHeight, time, true);
        DrawEdgeBand(canvas, _bottomEdgeShaderInstance!, width, height - halfBandHeight, time, false);
    }

    private void DrawEdgeBand(
        SKCanvas canvas,
        HudShaderInstance instance,
        float width,
        float top,
        float time,
        bool mirrorX)
    {
        instance.Uniforms["origin"] = new SKPoint(0f, top);
        instance.Uniforms["size"] = new SKPoint(width, QuestCompletionEdgeSkiaProgram.BandHeight);
        instance.Uniforms["time"] = time;
        instance.Uniforms["edgeColor"] = QuestCompletionEdgeSkiaProgram.FlareSkColor;
        instance.Uniforms["mirrorX"] = mirrorX ? 1f : 0f;

        using var shader = instance.Build();
        _edgePaint!.Shader = shader;
        try
        {
            canvas.DrawRect(0f, top, width, QuestCompletionEdgeSkiaProgram.BandHeight, _edgePaint);
        }
        finally
        {
            _edgePaint.Shader = null;
        }
    }

    private void EnsureSkiaResources()
    {
        if (_waveShaderInstance is not null)
            return;

        var waveProgram = QuestFlowMaterialSkiaProgram.GetForCurrentThread();
        _waveShaderInstance = waveProgram.CreateInstance();
        _particleShaderInstance = QuestSelectionParticleSkiaProgram.GetForCurrentThread().CreateInstance();
        var edgeProgram = QuestCompletionEdgeSkiaProgram.GetForCurrentThread();
        _topEdgeShaderInstance = edgeProgram.CreateInstance();
        _bottomEdgeShaderInstance = edgeProgram.CreateInstance();
        _edgeBaselineShaderInstance = edgeProgram.CreateBaselineInstance();
        _backgroundPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _wavePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Blender = waveProgram.Blender };
        _particlePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Blender = waveProgram.Blender };
        _edgePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Blender = waveProgram.Blender };
        _edgeBaselinePaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
    }

    private void DisposeSkiaResources()
    {
        _edgeBaselinePaint?.Dispose();
        _edgeBaselinePaint = null;
        _edgePaint?.Dispose();
        _edgePaint = null;
        _particlePaint?.Dispose();
        _particlePaint = null;
        _wavePaint?.Dispose();
        _wavePaint = null;
        _backgroundPaint?.Dispose();
        _backgroundPaint = null;
        _particleShaderInstance?.Dispose();
        _particleShaderInstance = null;
        _waveShaderInstance?.Dispose();
        _waveShaderInstance = null;
        _edgeBaselineShaderInstance?.Dispose();
        _edgeBaselineShaderInstance = null;
        _bottomEdgeShaderInstance?.Dispose();
        _bottomEdgeShaderInstance = null;
        _topEdgeShaderInstance?.Dispose();
        _topEdgeShaderInstance = null;
    }

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
}

internal sealed class QuestCompletionEdgeSkiaProgram
{
    internal static readonly Color BaselineColor = Color.FromRgb(0xBA, 0xDC, 0xFF);
    internal static readonly SKColorF BaselineSkColor = new(0xBA / 255f, 0xDC / 255f, 1f, 1f);
    internal static readonly SKColorF FlareSkColor = new(0xCC / 255f, 0xF0 / 255f, 1f, 1f);
    internal const float BandHeight = 20f;
    internal const float BaselineHeight = 1f;
    internal const float MainPanningU = 1f;
    internal const float MainOffsetU = 1f;
    internal const float MainTilingU = 2f;
    internal const float MainTilingV = 2f;
    internal const float MaskTilingU = 1.133581f;
    internal const float MaskTilingV = 1f;
    internal const float MaskRotationTurns = 0.25f;
    internal const float MainColorMultiplier = 3f;

    private const string ShaderSource = """
        uniform float2 origin;
        uniform float2 size;
        uniform float2 flareTextureSize;
        uniform float2 maskTextureSize;
        uniform float2 mainPanning;
        uniform float2 mainOffset;
        uniform float2 mainTiling;
        uniform float2 maskTiling;
        uniform float maskRotationRadians;
        uniform float mainColorMultiplier;
        uniform float time;
        uniform float mirrorX;
        uniform half4 edgeColor;
        uniform shader flareTexture;
        uniform shader maskTexture;

        half srgbToLinear(half value) {
            return value <= 0.04045
                ? value / 12.92
                : pow((value + 0.055) / 1.055, 2.4);
        }

        half3 srgbToLinear3(half3 value) {
            return half3(
                srgbToLinear(value.r),
                srgbToLinear(value.g),
                srgbToLinear(value.b));
        }

        float2 scaleAroundCenter(float2 uv, float2 tiling) {
            return uv / tiling + float2(0.5) - float2(0.5) / tiling;
        }

        float2 rotateAroundCenter(float2 uv, float angle) {
            float sine = sin(angle);
            float cosine = cos(angle);
            float2 centered = uv - float2(0.5);
            return float2(
                centered.x * cosine - centered.y * sine,
                centered.x * sine + centered.y * cosine) + float2(0.5);
        }

        half4 main(float2 position) {
            float2 uv = clamp((position - origin) / max(size, float2(1.0)), 0.0, 1.0);
            if (mirrorX > 0.5)
                uv.x = 1.0 - uv.x;

            float materialTime = mod(max(time, 0.0), 999.0);
            float2 flareUv = scaleAroundCenter(
                uv + mainOffset + materialTime * mainPanning,
                mainTiling);
            float2 maskUv = rotateAroundCenter(
                scaleAroundCenter(uv, maskTiling),
                maskRotationRadians);

            half4 flare = flareTexture.eval(flareUv * flareTextureSize);
            half mask = srgbToLinear(maskTexture.eval(maskUv * maskTextureSize).r);
            half opacity = clamp(srgbToLinear(flare.r) * mask * edgeColor.a, 0.0, 1.0);
            half intensity = max(flare.a * mainColorMultiplier * opacity, 0.0);
            half3 additiveLinear = srgbToLinear3(edgeColor.rgb) * intensity;
            return half4(additiveLinear, opacity);
        }
        """;

    private const string BaselineShaderSource = """
        uniform float originX;
        uniform float width;
        uniform float2 baselineTextureSize;
        uniform half4 edgeColor;
        uniform shader baselineTexture;

        half4 main(float2 position) {
            float u = clamp((position.x - originX) / max(width, 1.0), 0.0, 1.0);
            half alpha = baselineTexture.eval(float2(u, 0.5) * baselineTextureSize).a * edgeColor.a;
            return half4(edgeColor.rgb * alpha, alpha);
        }
        """;

    [ThreadStatic]
    // Native shader resources stay on the compositor thread that created them.
    private static QuestCompletionEdgeSkiaProgram? s_current;

    private readonly SKRuntimeEffect _effect;
    private readonly SKRuntimeEffect _baselineEffect;
    private readonly QuestCompletionEdgeSkiaTextures _textures;

    private QuestCompletionEdgeSkiaProgram()
    {
        _effect = CompileEffect();
        _baselineEffect = CompileBaselineEffect();
        _textures = QuestCompletionEdgeSkiaTextures.GetForCurrentThread();
    }

    internal static QuestCompletionEdgeSkiaProgram GetForCurrentThread() =>
        s_current ??= new QuestCompletionEdgeSkiaProgram();

    internal static SKRuntimeEffect CompileEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(ShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile quest completion edge shader: {errors}");
    }

    internal static SKRuntimeEffect CompileBaselineEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(BaselineShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile quest completion baseline shader: {errors}");
    }

    internal HudShaderInstance CreateInstance()
    {
        var instance = new HudShaderInstance(_effect);
        instance.Children["flareTexture"] = _textures.FlareShader;
        instance.Children["maskTexture"] = _textures.MaskShader;
        instance.Uniforms["flareTextureSize"] = _textures.FlareTextureSize;
        instance.Uniforms["maskTextureSize"] = _textures.MaskTextureSize;
        instance.Uniforms["mainPanning"] = new SKPoint(MainPanningU, 0f);
        instance.Uniforms["mainOffset"] = new SKPoint(MainOffsetU, 0f);
        instance.Uniforms["mainTiling"] = new SKPoint(MainTilingU, MainTilingV);
        instance.Uniforms["maskTiling"] = new SKPoint(MaskTilingU, MaskTilingV);
        instance.Uniforms["maskRotationRadians"] = MaskRotationTurns * MathF.Tau;
        instance.Uniforms["mainColorMultiplier"] = MainColorMultiplier;
        return instance;
    }

    internal HudShaderInstance CreateBaselineInstance()
    {
        var instance = new HudShaderInstance(_baselineEffect);
        instance.Children["baselineTexture"] = _textures.BaselineShader;
        instance.Uniforms["baselineTextureSize"] = _textures.BaselineTextureSize;
        return instance;
    }
}

internal sealed class QuestFlowMaterialSkiaProgram
{
    internal static readonly SKColorF ClientTintColor = new(0x46 / 255f, 0x64 / 255f, 0x66 / 255f, 1f);
    internal const float TimePeriodSeconds = 999f;
    internal const float WidgetBrushHeight = 60f;
    internal const float WidgetRenderScaleY = 4f;
    internal const float WidgetClipHeight = 44f;
    internal const float WidgetUvCompressionY = WidgetBrushHeight * WidgetRenderScaleY / WidgetClipHeight;

    private const string ShaderSource = """
        uniform float2 origin;
        uniform float2 size;
        uniform float2 trailTextureSize;
        uniform float2 maskTextureSize;
        uniform float2 mixTextureSize;
        uniform float2 normalTextureSize;
        uniform float2 distortionMaskTextureSize;
        uniform float widgetUvCompressionY;
        uniform float time;
        uniform half4 flowColor;
        uniform shader trailTexture;
        uniform shader maskTexture;
        uniform shader mixTexture;
        uniform shader normalTexture;
        uniform shader distortionMaskTexture;

        half srgbToLinear(half value) {
            return value <= 0.04045
                ? value / 12.92
                : pow((value + 0.055) / 1.055, 2.4);
        }

        half3 srgbToLinear3(half3 value) {
            return half3(
                srgbToLinear(value.r),
                srgbToLinear(value.g),
                srgbToLinear(value.b));
        }

        float2 scaleAroundCenter(float2 uv, float2 tiling) {
            return uv / tiling + float2(0.5) - float2(0.5) / tiling;
        }

        half4 main(float2 position) {
            float2 uv = clamp((position - origin) / max(size, float2(1.0)), 0.0, 1.0);
            float2 widgetUv = float2(uv.x, (uv.y - 0.5) / max(widgetUvCompressionY, 1.0) + 0.5);
            float materialTime = mod(max(time, 0.0), 999.0);

            float2 normalUv =
                (widgetUv + materialTime * float2(-0.2, -0.224)) * float2(0.6, 0.5);
            half normalValue = normalTexture.eval(normalUv * normalTextureSize).r;

            float2 distortionMaskUv = clamp(
                (widgetUv + float2(0.341331988573074, 0.0)) * float2(0.4, 1.0),
                0.0,
                1.0);
            half distortionMask = srgbToLinear(
                distortionMaskTexture.eval(distortionMaskUv * distortionMaskTextureSize).r);
            float distortion = (float(normalValue) - 0.5) * float(distortionMask);

            float2 trailUv = scaleAroundCenter(
                widgetUv + materialTime * float2(-0.2, 0.0),
                float2(0.6, 0.25));
            trailUv.y = clamp(trailUv.y + distortion * 1.5, 0.0, 1.0);
            half trail = trailTexture.eval(trailUv * trailTextureSize).r;

            float2 mixUv = scaleAroundCenter(
                widgetUv + materialTime * float2(-0.2, 0.0),
                float2(1.0, 0.488000005483627));
            mixUv.y = clamp(mixUv.y - distortion, 0.0, 1.0);
            half mixValue = srgbToLinear(mixTexture.eval(mixUv * mixTextureSize).r);

            half maskValue = srgbToLinear(
                maskTexture.eval(clamp(widgetUv, 0.0, 1.0) * maskTextureSize).b);
            half opacity = clamp(maskValue * trail, 0.0, 1.0);
            half materialFactor = max(trail + mixValue, 0.0) * opacity;
            half highlightAlpha = clamp(materialFactor, 0.0, 1.0);
            half3 additiveLinear = srgbToLinear3(flowColor.rgb) * highlightAlpha;
            return half4(additiveLinear, highlightAlpha);
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

        half3 srgbToLinear3(half3 value) {
            return half3(
                srgbToLinear(value.r),
                srgbToLinear(value.g),
                srgbToLinear(value.b));
        }

        half3 linearToSrgb3(half3 value) {
            return half3(
                linearToSrgb(value.r),
                linearToSrgb(value.g),
                linearToSrgb(value.b));
        }

        half4 main(half4 source, half4 destination) {
            half highlightAlpha = source.a;
            if (highlightAlpha <= 0.0)
                return destination;

            half destinationAlpha = destination.a;
            half3 destinationSrgb = destinationAlpha > 0.0
                ? clamp(destination.rgb / destinationAlpha, 0.0, 1.0)
                : half3(0.0);
            half3 destinationLinearPremultiplied = srgbToLinear3(destinationSrgb) * destinationAlpha;
            half resultAlpha = highlightAlpha + destinationAlpha * (1.0 - highlightAlpha);
            half3 resultLinear = clamp(
                (destinationLinearPremultiplied + source.rgb) / max(resultAlpha, 0.0001),
                0.0,
                1.0);
            return half4(linearToSrgb3(resultLinear) * resultAlpha, resultAlpha);
        }
        """;

    [ThreadStatic]
    // Native shader resources stay on the compositor thread that created them.
    private static QuestFlowMaterialSkiaProgram? s_current;

    private readonly SKRuntimeEffect _effect;
    private readonly SKRuntimeEffect _blenderEffect;
    private readonly QuestFlowMaterialSkiaTextures _textures;

    private QuestFlowMaterialSkiaProgram()
    {
        _effect = CompileEffect();
        _blenderEffect = CompileBlenderEffect();
        Blender = _blenderEffect.ToBlender();
        _textures = QuestFlowMaterialSkiaTextures.GetForCurrentThread();
    }

    internal static QuestFlowMaterialSkiaProgram GetForCurrentThread() =>
        s_current ??= new QuestFlowMaterialSkiaProgram();

    internal SKBlender Blender { get; }

    internal static SKRuntimeEffect CompileEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(ShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile quest flow material shader: {errors}");
    }

    internal static SKRuntimeEffect CompileBlenderEffect()
    {
        var effect = SKRuntimeEffect.CreateBlender(BlenderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile quest flow material blender: {errors}");
    }

    internal HudShaderInstance CreateInstance()
    {
        var instance = new HudShaderInstance(_effect);
        instance.Children["trailTexture"] = _textures.TrailShader;
        instance.Children["maskTexture"] = _textures.MaskShader;
        instance.Children["mixTexture"] = _textures.MixShader;
        instance.Children["normalTexture"] = _textures.NormalShader;
        instance.Children["distortionMaskTexture"] = _textures.DistortionMaskShader;
        instance.Uniforms["trailTextureSize"] = _textures.TrailTextureSize;
        instance.Uniforms["maskTextureSize"] = _textures.MaskTextureSize;
        instance.Uniforms["mixTextureSize"] = _textures.MixTextureSize;
        instance.Uniforms["normalTextureSize"] = _textures.NormalTextureSize;
        instance.Uniforms["distortionMaskTextureSize"] = _textures.DistortionMaskTextureSize;
        instance.Uniforms["widgetUvCompressionY"] = WidgetUvCompressionY;
        return instance;
    }
}

internal sealed class QuestSelectionParticleSkiaProgram
{
    internal static readonly SKColorF ClientTintColor = new(0xBE / 255f, 0xF7 / 255f, 0xFA / 255f, 1f);
    internal const float MainPanningU = -0.1f;
    internal const float MainTiling = 4f;
    internal const float MaskTiling = 1.3f;
    internal const float NoisePanningU = -0.05f;
    internal const float NoisePanningV = 0.04f;
    internal const float NoiseTiling = 5f;
    internal const float DistortionPanningU = 0.068f;
    internal const float DistortionPanningV = 0.05f;
    internal const float DistortionTilingU = 1.076974f;
    internal const float DistortionTilingV = 1.276276f;
    internal const float DistortionRotation = 0.117333f;
    internal const float DistortionIntensity = -0.443259f;
    internal const float NoiseMultiply = 2f;

    private const string ShaderSource = """
        uniform float2 origin;
        uniform float2 size;
        uniform float2 particleTextureSize;
        uniform float2 maskTextureSize;
        uniform float2 noiseTextureSize;
        uniform float2 normalTextureSize;
        uniform float2 mainPanning;
        uniform float mainTiling;
        uniform float maskTiling;
        uniform float2 noisePanning;
        uniform float noiseTiling;
        uniform float2 distortionPanning;
        uniform float2 distortionTiling;
        uniform float distortionRotation;
        uniform float distortionIntensity;
        uniform float noiseMultiply;
        uniform float time;
        uniform half4 flowColor;
        uniform shader particleTexture;
        uniform shader maskTexture;
        uniform shader noiseTexture;
        uniform shader normalTexture;

        half srgbToLinear(half value) {
            return value <= 0.04045
                ? value / 12.92
                : pow((value + 0.055) / 1.055, 2.4);
        }

        half3 srgbToLinear3(half3 value) {
            return half3(
                srgbToLinear(value.r),
                srgbToLinear(value.g),
                srgbToLinear(value.b));
        }

        float2 scaleAroundCenter(float2 uv, float2 tiling) {
            return uv / tiling + float2(0.5) - float2(0.5) / tiling;
        }

        float2 rotateAroundCenter(float2 uv, float angle) {
            float sine = sin(angle);
            float cosine = cos(angle);
            float2 centered = uv - float2(0.5);
            return float2(
                centered.x * cosine - centered.y * sine,
                centered.x * sine + centered.y * cosine) + float2(0.5);
        }

        half4 main(float2 position) {
            float2 uv = clamp((position - origin) / max(size, float2(1.0)), 0.0, 1.0);
            float materialTime = mod(max(time, 0.0), 999.0);

            float2 normalUv = rotateAroundCenter(
                (uv + materialTime * distortionPanning) * distortionTiling,
                distortionRotation);
            half2 normalValue = normalTexture.eval(normalUv * normalTextureSize).rg;
            float2 distortion = (float2(normalValue) - float2(0.5)) * distortionIntensity;

            float2 particleUv =
                (uv + materialTime * mainPanning) * float2(mainTiling) + distortion;
            half particle = particleTexture.eval(particleUv * particleTextureSize).r;

            float2 maskUv = clamp(scaleAroundCenter(uv, float2(maskTiling)), 0.0, 1.0);
            half mask = srgbToLinear(maskTexture.eval(maskUv * maskTextureSize).r);

            float2 noiseUv =
                (uv + materialTime * noisePanning) * float2(noiseTiling);
            half noise = srgbToLinear(noiseTexture.eval(noiseUv * noiseTextureSize).r);

            half materialFactor = clamp(mask * particle * noise * noiseMultiply, 0.0, 1.0);
            half3 additiveLinear = srgbToLinear3(flowColor.rgb) * materialFactor;
            return half4(additiveLinear, materialFactor);
        }
        """;

    [ThreadStatic]
    // Native shader resources stay on the compositor thread that created them.
    private static QuestSelectionParticleSkiaProgram? s_current;

    private readonly SKRuntimeEffect _effect;
    private readonly QuestSelectionParticleSkiaTextures _textures;

    private QuestSelectionParticleSkiaProgram()
    {
        _effect = CompileEffect();
        _textures = QuestSelectionParticleSkiaTextures.GetForCurrentThread();
    }

    internal static QuestSelectionParticleSkiaProgram GetForCurrentThread() =>
        s_current ??= new QuestSelectionParticleSkiaProgram();

    internal static SKRuntimeEffect CompileEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(ShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile quest selection particle shader: {errors}");
    }

    internal HudShaderInstance CreateInstance()
    {
        var instance = new HudShaderInstance(_effect);
        instance.Children["particleTexture"] = _textures.ParticleShader;
        instance.Children["maskTexture"] = _textures.MaskShader;
        instance.Children["noiseTexture"] = _textures.NoiseShader;
        instance.Children["normalTexture"] = _textures.NormalShader;
        instance.Uniforms["particleTextureSize"] = _textures.ParticleTextureSize;
        instance.Uniforms["maskTextureSize"] = _textures.MaskTextureSize;
        instance.Uniforms["noiseTextureSize"] = _textures.NoiseTextureSize;
        instance.Uniforms["normalTextureSize"] = _textures.NormalTextureSize;
        instance.Uniforms["mainPanning"] = new SKPoint(MainPanningU, 0f);
        instance.Uniforms["mainTiling"] = MainTiling;
        instance.Uniforms["maskTiling"] = MaskTiling;
        instance.Uniforms["noisePanning"] = new SKPoint(NoisePanningU, NoisePanningV);
        instance.Uniforms["noiseTiling"] = NoiseTiling;
        instance.Uniforms["distortionPanning"] = new SKPoint(DistortionPanningU, DistortionPanningV);
        instance.Uniforms["distortionTiling"] = new SKPoint(DistortionTilingU, DistortionTilingV);
        instance.Uniforms["distortionRotation"] = DistortionRotation;
        instance.Uniforms["distortionIntensity"] = DistortionIntensity;
        instance.Uniforms["noiseMultiply"] = NoiseMultiply;
        return instance;
    }
}
