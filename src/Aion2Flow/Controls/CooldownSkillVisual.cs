using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace Cloris.Aion2Flow.Controls;

public sealed class CooldownSkillVisual : Control
{
    public static readonly DirectProperty<CooldownSkillVisual, double> CooldownProgressProperty =
        AvaloniaProperty.RegisterDirect<CooldownSkillVisual, double>(
            nameof(CooldownProgress),
            control => control.CooldownProgress,
            (control, value) => control.CooldownProgress = value);

    public static readonly DirectProperty<CooldownSkillVisual, long> CompletionStartedUtcMillisecondsProperty =
        AvaloniaProperty.RegisterDirect<CooldownSkillVisual, long>(
            nameof(CompletionStartedUtcMilliseconds),
            control => control.CompletionStartedUtcMilliseconds,
            (control, value) => control.CompletionStartedUtcMilliseconds = value);

    private CompositionCustomVisual? _compositionVisual;
    private CooldownSkillVisualState _publishedState;
    private bool _hasPublishedState;

    static CooldownSkillVisual()
    {
        AffectsRender<CooldownSkillVisual>(CooldownProgressProperty, CompletionStartedUtcMillisecondsProperty);
    }

    public double CooldownProgress
    {
        get;
        set => SetAndRaise(CooldownProgressProperty, ref field, value);
    }

    public long CompletionStartedUtcMilliseconds
    {
        get;
        set => SetAndRaise(CompletionStartedUtcMillisecondsProperty, ref field, value);
    }

    public override void Render(DrawingContext context)
    {
        if (_compositionVisual is not null)
            return;

        CooldownSkillVisualHandler.RenderStatic(context, new Rect(Bounds.Size), CreateVisualState());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
            return;

        _ = CooldownSkillMaterialTextureData.Shared;
        _compositionVisual = compositor.CreateCustomVisual(new CooldownSkillVisualHandler());
        _compositionVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _compositionVisual);
        PublishVisualState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_compositionVisual is not null)
        {
            _compositionVisual.SendHandlerMessage(CooldownSkillVisualHandler.StopMessage);
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

        if (change.Property == CooldownProgressProperty ||
            change.Property == CompletionStartedUtcMillisecondsProperty)
        {
            if (_compositionVisual is null)
                InvalidateVisual();
            PublishVisualState();
        }
    }

    internal CooldownSkillVisualState CreateVisualStateForDiagnostics() => CreateVisualState();

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

    private CooldownSkillVisualState CreateVisualState()
    {
        var progress = double.IsFinite(CooldownProgress)
            ? (float)Math.Clamp(CooldownProgress, 0d, 1d)
            : 0f;
        return new CooldownSkillVisualState(progress, CompletionStartedUtcMilliseconds);
    }
}

internal readonly record struct CooldownSkillVisualState(
    float CooldownProgress,
    long CompletionStartedUtcMilliseconds)
{
    internal bool HasCooldown => CooldownProgress > 0f;

    internal bool HasTail => CooldownProgress > 0f && CooldownProgress < 1f;
}

internal static class CooldownSkillVisualClientStyle
{
    internal const float NativeWidgetSize = 70f;
    internal const float CornerRadius = 2f;
    internal const float TailSourceWidth = 70f;
    internal const float TailSourceHeight = 2f;
    internal const float TailGlowScaleY = 12f;
    internal const float TailTranslationY = -1f;
    internal static readonly Color DimmedColor = Color.FromArgb(0x99, 0x00, 0x00, 0x00);
    internal static readonly Color CooldownFillColor = Color.FromArgb(0x7F, 0x00, 0x84, 0x93);
    internal static readonly SKColorF TailGlowAddColor = new(0x73 / 255f, 0xE6 / 255f, 0xE7 / 255f, 1f);
    internal static readonly SKColorF TailGlowColor = new(0x2E / 255f, 0x64 / 255f, 0x65 / 255f, 1f);
}

internal sealed class CooldownSkillVisualHandler : CompositionCustomVisualHandler
{
    internal static readonly object StopMessage = new();

    private CooldownSkillVisualState _state;
    private HudShaderInstance? _tailShaderInstance;
    private CooldownSkillMaterialSkiaTextures? _textures;
    private SKPaint? _dimmedPaint;
    private SKPaint? _tailPaint;
    private SKPaint? _completionPaint;
    private SKPaint? _opacityLayerPaint;
    private bool _hasState;
    private bool _animationWasActive;
    private bool _stopped;

    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, StopMessage))
        {
            _stopped = true;
            DisposeSkiaResources();
            return;
        }

        if (message is not CooldownSkillVisualState state || _stopped)
            return;

        _state = state;
        _hasState = true;
        _animationWasActive = state.HasTail || CooldownSkillVisualClientAnimation.Resolve(
            state.CompletionStartedUtcMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).IsActive;
        Invalidate();
        if (_animationWasActive)
            RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_stopped || !_hasState)
            return;

        var completionActive = CooldownSkillVisualClientAnimation.Resolve(
            _state.CompletionStartedUtcMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).IsActive;
        if (!_state.HasTail && !completionActive)
        {
            if (_animationWasActive)
            {
                _animationWasActive = false;
                Invalidate();
            }

            return;
        }

        _animationWasActive = true;
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext drawingContext)
    {
        if (!_hasState || _stopped)
            return;

        var size = EffectiveSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var skiaFeature = drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (skiaFeature is null)
        {
            RenderStatic(drawingContext, new Rect(0d, 0d, size.X, size.Y), _state);
            return;
        }

        using var lease = skiaFeature.Lease();
        RenderSkia(lease.SkCanvas, (float)size.X, (float)size.Y, lease.CurrentOpacity);
    }

    internal static void RenderStatic(DrawingContext context, Rect bounds, CooldownSkillVisualState state)
    {
        if (!state.HasCooldown || bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        var fillHeight = bounds.Height * state.CooldownProgress;
        if (fillHeight <= 0d)
            return;

        using var clip = context.PushClip(bounds);
        context.FillRectangle(
            new ImmutableSolidColorBrush(CooldownSkillVisualClientStyle.DimmedColor),
            new Rect(bounds.Left, bounds.Bottom - fillHeight, bounds.Width, fillHeight));
        context.FillRectangle(
            new ImmutableSolidColorBrush(CooldownSkillVisualClientStyle.CooldownFillColor),
            new Rect(bounds.Left, bounds.Bottom - fillHeight, bounds.Width, fillHeight));
    }

    private static void RenderStatic(ImmediateDrawingContext context, Rect bounds, CooldownSkillVisualState state)
    {
        if (!state.HasCooldown || bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        var fillHeight = bounds.Height * state.CooldownProgress;
        if (fillHeight <= 0d)
            return;

        using var clip = context.PushClip(bounds);
        context.FillRectangle(
            new ImmutableSolidColorBrush(CooldownSkillVisualClientStyle.DimmedColor),
            new Rect(bounds.Left, bounds.Bottom - fillHeight, bounds.Width, fillHeight));
        context.FillRectangle(
            new ImmutableSolidColorBrush(CooldownSkillVisualClientStyle.CooldownFillColor),
            new Rect(bounds.Left, bounds.Bottom - fillHeight, bounds.Width, fillHeight));
    }

    internal static SKRuntimeEffect CompileTailShaderForDiagnostics() => CooldownSkillTailSkiaProgram.CompileEffect();

    internal static void RenderCooldownMaskForDiagnostics(SKCanvas canvas, float width, float height, float progress)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        DrawCooldownLayers(canvas, width, height, progress, paint);
    }

    internal static void RenderCompletionFrameForDiagnostics(SKCanvas canvas, float width, float height, int frameIndex)
    {
        var frames = CooldownSkillMaterialSkiaTextures.GetForCurrentThread().CompletionFrames;
        if ((uint)frameIndex >= (uint)frames.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        canvas.DrawImage(frames[frameIndex], new SKRect(0f, 0f, width, height), paint);
    }

    private void RenderSkia(SKCanvas canvas, float width, float height, double opacity)
    {
        EnsureSkiaResources();
        using var opacityScope = HudSkiaOpacityScope.Push(canvas, _opacityLayerPaint!, new SKRect(0f, 0f, width, height), opacity);

        if (_state.HasCooldown)
            DrawCooldown(canvas, width, height);

        var completion = CooldownSkillVisualClientAnimation.Resolve(
            _state.CompletionStartedUtcMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (completion.IsActive)
            DrawCompletion(canvas, width, height, completion);
    }

    private void DrawCompletion(
        SKCanvas canvas,
        float width,
        float height,
        CooldownSkillVisualClientAnimationState animation)
    {
        canvas.DrawImage(
            _textures!.CompletionFrames[animation.FrameIndex],
            new SKRect(0f, 0f, width, height),
            _completionPaint);
    }

    private void DrawCooldown(SKCanvas canvas, float width, float height)
    {
        var fillTop = height * (1f - _state.CooldownProgress);
        DrawCooldownLayers(canvas, width, height, _state.CooldownProgress, _dimmedPaint!);

        if (_state.HasTail)
            DrawTail(canvas, width, height, fillTop);
    }

    private static void DrawCooldownLayers(SKCanvas canvas, float width, float height, float progress, SKPaint paint)
    {
        var bounds = new SKRect(0f, 0f, width, height);
        var radius = ResolveNativeScale(width, height) * CooldownSkillVisualClientStyle.CornerRadius;
        var roundedBounds = new SKRoundRect(bounds, radius, radius);
        var fillTop = height * (1f - Math.Clamp(progress, 0f, 1f));
        canvas.Save();
        canvas.ClipRoundRect(roundedBounds, antialias: true);
        paint.Color = ToSkColor(CooldownSkillVisualClientStyle.DimmedColor);
        canvas.DrawRect(0f, fillTop, width, height, paint);
        paint.Color = ToSkColor(CooldownSkillVisualClientStyle.CooldownFillColor);
        canvas.DrawRect(0f, fillTop, width, height, paint);
        canvas.Restore();
    }

    private void DrawTail(SKCanvas canvas, float width, float height, float fillTop)
    {
        var nativeScale = ResolveNativeScale(width, height);
        var tailHeight = CooldownSkillVisualClientStyle.TailSourceHeight * CooldownSkillVisualClientStyle.TailGlowScaleY * nativeScale;
        var tailWidth = CooldownSkillVisualClientStyle.TailSourceWidth * (width / CooldownSkillVisualClientStyle.NativeWidgetSize);
        var tailLeft = (width - tailWidth) * 0.5f;
        var tailTop = fillTop + CooldownSkillVisualClientStyle.TailTranslationY * nativeScale - tailHeight * 0.5f;
        var instance = _tailShaderInstance!;
        instance.Uniforms["origin"] = new SKPoint(tailLeft, tailTop);
        instance.Uniforms["size"] = new SKPoint(tailWidth, tailHeight);
        instance.Uniforms["time"] = (float)(CompositionNow.TotalSeconds % 999d);
        instance.Uniforms["tailGlowAddColor"] = CooldownSkillVisualClientStyle.TailGlowAddColor;
        instance.Uniforms["tailGlowColor"] = CooldownSkillVisualClientStyle.TailGlowColor;

        using var shader = instance.Build();
        _tailPaint!.Shader = shader;
        try
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0f, 0f, width, height));
            canvas.DrawRect(tailLeft, tailTop, tailLeft + tailWidth, tailTop + tailHeight, _tailPaint);
            canvas.Restore();
        }
        finally
        {
            _tailPaint.Shader = null;
        }
    }

    private void EnsureSkiaResources()
    {
        if (_tailShaderInstance is not null)
            return;

        _textures = CooldownSkillMaterialSkiaTextures.GetForCurrentThread();
        _tailShaderInstance = CooldownSkillTailSkiaProgram.GetForCurrentThread().CreateInstance();
        _dimmedPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _tailPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
        _completionPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
        _opacityLayerPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
    }

    private void DisposeSkiaResources()
    {
        _opacityLayerPaint?.Dispose();
        _opacityLayerPaint = null;
        _tailPaint?.Dispose();
        _tailPaint = null;
        _completionPaint?.Dispose();
        _completionPaint = null;
        _dimmedPaint?.Dispose();
        _dimmedPaint = null;
        _tailShaderInstance?.Dispose();
        _tailShaderInstance = null;
        _textures = null;
    }

    private static float ResolveNativeScale(float width, float height) =>
        Math.Min(width, height) / CooldownSkillVisualClientStyle.NativeWidgetSize;

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
}

internal sealed class CooldownSkillTailSkiaProgram
{
    internal const float T2PanningU = -15f;
    internal const float T2TilingU = 0.03f;
    internal const float T2TilingV = 0.01f;
    internal const float T3PanningU = -100f;
    internal const float T3TilingU = 0.03f;
    internal const float T3TilingV = 0.01f;

    private const string ShaderSource = """
        uniform float2 origin;
        uniform float2 size;
        uniform float2 tailGlowAddTextureSize;
        uniform float2 tailGlowTextureSize;
        uniform float2 flareTextureSize;
        uniform float2 gradientTextureSize;
        uniform float time;
        uniform float2 t2Panning;
        uniform float2 t2Tiling;
        uniform float2 t3Panning;
        uniform float2 t3Tiling;
        uniform half4 tailGlowAddColor;
        uniform half4 tailGlowColor;
        uniform shader tailGlowAddTexture;
        uniform shader tailGlowTexture;
        uniform shader flareTexture;
        uniform shader gradientTexture;

        half4 main(float2 position) {
            float2 uv = clamp((position - origin) / max(size, float2(1.0)), 0.0, 1.0);
            half tailGlowAdd = tailGlowAddTexture.eval(uv * tailGlowAddTextureSize).r;
            half tailGlow = tailGlowTexture.eval(uv * tailGlowTextureSize).r;
            half flare = flareTexture.eval(uv * flareTextureSize).r;
            float2 t2Uv = (uv + time * t2Panning) * t2Tiling;
            float2 t3Uv = (uv + time * t3Panning) * t3Tiling;
            half t2Mask = gradientTexture.eval(t2Uv * gradientTextureSize).r;
            half t3Mix = gradientTexture.eval(t3Uv * gradientTextureSize).r;
            half effect = clamp(flare * (t2Mask + t3Mix * 0.24), 0.0, 1.0);
            half3 rgb = tailGlowAddColor.rgb * tailGlowAdd + tailGlowColor.rgb * tailGlow + half3(effect);
            half alpha = clamp(tailGlowAdd + tailGlow + effect, 0.0, 1.0);
            return half4(rgb, alpha);
        }
        """;

    [ThreadStatic]
    private static CooldownSkillTailSkiaProgram? s_current;

    private readonly SKRuntimeEffect _effect;
    private readonly CooldownSkillMaterialSkiaTextures _textures;

    private CooldownSkillTailSkiaProgram()
    {
        _effect = CompileEffect();
        _textures = CooldownSkillMaterialSkiaTextures.GetForCurrentThread();
    }

    internal static CooldownSkillTailSkiaProgram GetForCurrentThread() =>
        s_current ??= new CooldownSkillTailSkiaProgram();

    internal static SKRuntimeEffect CompileEffect()
    {
        var effect = SKRuntimeEffect.CreateShader(ShaderSource, out var errors);
        return effect ?? throw new InvalidOperationException($"Unable to compile cooldown tail shader: {errors}");
    }

    internal HudShaderInstance CreateInstance()
    {
        var instance = new HudShaderInstance(_effect);
        instance.Children["tailGlowAddTexture"] = _textures.TailGlowAddShader;
        instance.Children["tailGlowTexture"] = _textures.TailGlowShader;
        instance.Children["flareTexture"] = _textures.TailEffectFlareShader;
        instance.Children["gradientTexture"] = _textures.TailEffectGradientShader;
        instance.Uniforms["tailGlowAddTextureSize"] = _textures.TailGlowAddTextureSize;
        instance.Uniforms["tailGlowTextureSize"] = _textures.TailGlowTextureSize;
        instance.Uniforms["flareTextureSize"] = _textures.TailEffectFlareTextureSize;
        instance.Uniforms["gradientTextureSize"] = _textures.TailEffectGradientTextureSize;
        instance.Uniforms["t2Panning"] = new SKPoint(T2PanningU, 0f);
        instance.Uniforms["t2Tiling"] = new SKPoint(T2TilingU, T2TilingV);
        instance.Uniforms["t3Panning"] = new SKPoint(T3PanningU, 0f);
        instance.Uniforms["t3Tiling"] = new SKPoint(T3TilingU, T3TilingV);
        return instance;
    }
}

internal readonly record struct CooldownSkillVisualClientAnimationState(
    bool IsActive,
    int FrameIndex);

internal static class CooldownSkillVisualClientAnimation
{
    internal const long DurationMilliseconds = 317;
    internal static ReadOnlySpan<short> FrameStartMilliseconds =>
        [0, 17, 33, 50, 67, 83, 100, 117, 133, 150, 167, 183, 200, 217, 233, 250, 267, 283, 300];

    internal static CooldownSkillVisualClientAnimationState Resolve(
        long startedUtcMilliseconds,
        long nowUtcMilliseconds)
    {
        if (startedUtcMilliseconds <= 0)
            return default;

        var elapsedMilliseconds = nowUtcMilliseconds - startedUtcMilliseconds;
        if (elapsedMilliseconds < 0 || elapsedMilliseconds >= DurationMilliseconds)
            return default;

        var starts = FrameStartMilliseconds;
        var frameIndex = starts.Length - 1;
        while (frameIndex > 0 && elapsedMilliseconds < starts[frameIndex])
            frameIndex--;

        return new CooldownSkillVisualClientAnimationState(true, frameIndex);
    }
}
