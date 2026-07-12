using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace Cloris.Aion2Flow.Controls;

internal static class HudBrushColor
{
    internal static Color Resolve(IBrush? brush, Color fallback)
    {
        if (brush is not ISolidColorBrush solid)
            return fallback;

        var color = solid.Color;
        var alpha = (byte)Math.Clamp(Math.Round(color.A * Math.Clamp(brush.Opacity, 0d, 1d)), 0d, byte.MaxValue);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}

internal static class HudTextureAsset
{
    private const string AssetRoot = "avares://Aion2Flow/Assets/Images/Hud/";
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    internal static byte[] Read(string name)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var bytes))
                return bytes;

            using var source = AssetLoader.Open(new Uri(AssetRoot + name, UriKind.Absolute));
            using var destination = new MemoryStream();
            source.CopyTo(destination);
            bytes = destination.ToArray();
            Cache.Add(name, bytes);
            return bytes;
        }
    }
}

internal sealed record BossFocusMaterialTextureData(byte[] FineTrail, byte[] BroadTrail, byte[] Gradient)
{
    internal static BossFocusMaterialTextureData Shared { get; } = new(
        HudTextureAsset.Read("UT_Trail_002.png"),
        HudTextureAsset.Read("UT_Trail_057.png"),
        HudTextureAsset.Read("UT_Gradient_007.png"));
}

internal sealed record QuestFlowMaterialTextureData(
    byte[] Trail,
    byte[] Mask,
    byte[] Mix,
    byte[] Normal,
    byte[] DistortionMask)
{
    internal static QuestFlowMaterialTextureData Shared { get; } = new(
        HudTextureAsset.Read("UT_Trail_057.png"),
        HudTextureAsset.Read("UT_Gradient_001.png"),
        HudTextureAsset.Read("UT_Trail_036.png"),
        HudTextureAsset.Read("UT_Normal_002.png"),
        HudTextureAsset.Read("UT_Gradient_007.png"));
}

internal sealed record QuestSelectionParticleTextureData(
    byte[] Particle,
    byte[] Mask,
    byte[] Noise,
    byte[] Normal)
{
    internal static QuestSelectionParticleTextureData Shared { get; } = new(
        HudTextureAsset.Read("UT_Particle_003.png"),
        HudTextureAsset.Read("UT_Circle_002.png"),
        HudTextureAsset.Read("Noise_025.png"),
        HudTextureAsset.Read("UT_Normal_002.png"));
}

internal sealed record QuestCompletionEdgeTextureData(
    byte[] Flare,
    byte[] Mask,
    byte[] Baseline)
{
    internal static QuestCompletionEdgeTextureData Shared { get; } = new(
        HudTextureAsset.Read("UT_Flare_020_1.png"),
        HudTextureAsset.Read("UT_Gradient_010.png"),
        HudTextureAsset.Read("UT_GradationBH.png"));
}

internal sealed class BossFocusMaterialSkiaTextures
{
    [ThreadStatic]
    // Native texture resources stay on the compositor thread for its process lifetime.
    private static BossFocusMaterialSkiaTextures? s_current;

    private readonly SKImage _fineTrailImage;
    private readonly SKImage _broadTrailImage;
    private readonly SKImage _gradientImage;

    private BossFocusMaterialSkiaTextures()
    {
        var textures = BossFocusMaterialTextureData.Shared;
        _fineTrailImage = DecodeImage(textures.FineTrail, nameof(textures.FineTrail));
        _broadTrailImage = DecodeImage(textures.BroadTrail, nameof(textures.BroadTrail));
        _gradientImage = DecodeImage(textures.Gradient, nameof(textures.Gradient));

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        FineTrailShader = _fineTrailImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        BroadTrailShader = _broadTrailImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        GradientShader = _gradientImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
    }

    internal SKShader FineTrailShader { get; }

    internal SKShader BroadTrailShader { get; }

    internal SKShader GradientShader { get; }

    internal SKPoint FineTextureSize => new(_fineTrailImage.Width, _fineTrailImage.Height);

    internal SKPoint BroadTextureSize => new(_broadTrailImage.Width, _broadTrailImage.Height);

    internal SKPoint GradientTextureSize => new(_gradientImage.Width, _gradientImage.Height);

    internal static BossFocusMaterialSkiaTextures GetForCurrentThread() => s_current ??= new BossFocusMaterialSkiaTextures();

    private static SKImage DecodeImage(byte[] bytes, string name) =>
        SKImage.FromEncodedData(bytes) ?? throw new InvalidOperationException($"Unable to decode boss focus material texture {name}.");
}

internal sealed class QuestFlowMaterialSkiaTextures
{
    internal const int MaxAnisotropy = 8;

    [ThreadStatic]
    // Native texture resources stay on the compositor thread for its process lifetime.
    private static QuestFlowMaterialSkiaTextures? s_current;

    private readonly SKImage _trailImage;
    private readonly SKImage _maskImage;
    private readonly SKImage _mixImage;
    private readonly SKImage _normalImage;
    private readonly SKImage _distortionMaskImage;

    private QuestFlowMaterialSkiaTextures()
    {
        var textures = QuestFlowMaterialTextureData.Shared;
        _trailImage = DecodeImage(textures.Trail, nameof(textures.Trail));
        _maskImage = DecodeImage(textures.Mask, nameof(textures.Mask));
        _mixImage = DecodeImage(textures.Mix, nameof(textures.Mix));
        _normalImage = DecodeImage(textures.Normal, nameof(textures.Normal));
        _distortionMaskImage = DecodeImage(textures.DistortionMask, nameof(textures.DistortionMask));

        var sampling = new SKSamplingOptions(MaxAnisotropy);
        TrailShader = _trailImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        MaskShader = _maskImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
        MixShader = _mixImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        NormalShader = _normalImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        DistortionMaskShader = _distortionMaskImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
    }

    internal SKShader TrailShader { get; }

    internal SKShader MaskShader { get; }

    internal SKShader MixShader { get; }

    internal SKShader NormalShader { get; }

    internal SKShader DistortionMaskShader { get; }

    internal SKPoint TrailTextureSize => new(_trailImage.Width, _trailImage.Height);

    internal SKPoint MaskTextureSize => new(_maskImage.Width, _maskImage.Height);

    internal SKPoint MixTextureSize => new(_mixImage.Width, _mixImage.Height);

    internal SKPoint NormalTextureSize => new(_normalImage.Width, _normalImage.Height);

    internal SKPoint DistortionMaskTextureSize => new(_distortionMaskImage.Width, _distortionMaskImage.Height);

    internal static QuestFlowMaterialSkiaTextures GetForCurrentThread() =>
        s_current ??= new QuestFlowMaterialSkiaTextures();

    private static SKImage DecodeImage(byte[] bytes, string name) =>
        SKImage.FromEncodedData(bytes) ?? throw new InvalidOperationException($"Unable to decode quest flow material texture {name}.");
}

internal sealed class QuestSelectionParticleSkiaTextures
{
    internal const int MaxAnisotropy = 8;

    [ThreadStatic]
    // Native texture resources stay on the compositor thread for its process lifetime.
    private static QuestSelectionParticleSkiaTextures? s_current;

    private readonly SKImage _particleImage;
    private readonly SKImage _maskImage;
    private readonly SKImage _noiseImage;
    private readonly SKImage _normalImage;

    private QuestSelectionParticleSkiaTextures()
    {
        var textures = QuestSelectionParticleTextureData.Shared;
        _particleImage = DecodeImage(textures.Particle, nameof(textures.Particle));
        _maskImage = DecodeImage(textures.Mask, nameof(textures.Mask));
        _noiseImage = DecodeImage(textures.Noise, nameof(textures.Noise));
        _normalImage = DecodeImage(textures.Normal, nameof(textures.Normal));

        var sampling = new SKSamplingOptions(MaxAnisotropy);
        ParticleShader = _particleImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        MaskShader = _maskImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
        NoiseShader = _noiseImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        NormalShader = _normalImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
    }

    internal SKShader ParticleShader { get; }

    internal SKShader MaskShader { get; }

    internal SKShader NoiseShader { get; }

    internal SKShader NormalShader { get; }

    internal SKPoint ParticleTextureSize => new(_particleImage.Width, _particleImage.Height);

    internal SKPoint MaskTextureSize => new(_maskImage.Width, _maskImage.Height);

    internal SKPoint NoiseTextureSize => new(_noiseImage.Width, _noiseImage.Height);

    internal SKPoint NormalTextureSize => new(_normalImage.Width, _normalImage.Height);

    internal static QuestSelectionParticleSkiaTextures GetForCurrentThread() =>
        s_current ??= new QuestSelectionParticleSkiaTextures();

    private static SKImage DecodeImage(byte[] bytes, string name) =>
        SKImage.FromEncodedData(bytes) ?? throw new InvalidOperationException($"Unable to decode quest selection particle texture {name}.");
}

internal sealed class QuestCompletionEdgeSkiaTextures
{
    [ThreadStatic]
    // Native texture resources stay on the compositor thread for its process lifetime.
    private static QuestCompletionEdgeSkiaTextures? s_current;

    private readonly SKImage _flareImage;
    private readonly SKImage _maskImage;
    private readonly SKImage _baselineImage;

    private QuestCompletionEdgeSkiaTextures()
    {
        var textures = QuestCompletionEdgeTextureData.Shared;
        _flareImage = DecodeImage(textures.Flare, nameof(textures.Flare));
        _maskImage = DecodeImage(textures.Mask, nameof(textures.Mask));
        _baselineImage = DecodeImage(textures.Baseline, nameof(textures.Baseline));

        var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        FlareShader = _flareImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        MaskShader = _maskImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling);
        BaselineShader = _baselineImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
    }

    internal SKShader FlareShader { get; }

    internal SKShader MaskShader { get; }

    internal SKShader BaselineShader { get; }

    internal SKPoint FlareTextureSize => new(_flareImage.Width, _flareImage.Height);

    internal SKPoint MaskTextureSize => new(_maskImage.Width, _maskImage.Height);

    internal SKPoint BaselineTextureSize => new(_baselineImage.Width, _baselineImage.Height);

    internal static QuestCompletionEdgeSkiaTextures GetForCurrentThread() =>
        s_current ??= new QuestCompletionEdgeSkiaTextures();

    private static SKImage DecodeImage(byte[] bytes, string name) =>
        SKImage.FromEncodedData(bytes) ?? throw new InvalidOperationException($"Unable to decode quest completion edge texture {name}.");
}

internal sealed class HudShaderInstance : IDisposable
{
    // SKRuntimeShaderBuilder owns its effect; this instance deliberately borrows a thread-cached effect.
    private readonly SKRuntimeEffect _effect;

    internal HudShaderInstance(SKRuntimeEffect effect)
    {
        _effect = effect;
        Uniforms = new SKRuntimeEffectUniforms(effect);
        Children = new SKRuntimeEffectChildren(effect);
    }

    internal SKRuntimeEffectUniforms Uniforms { get; }

    internal SKRuntimeEffectChildren Children { get; }

    internal SKShader Build() => _effect.ToShader(Uniforms, Children);

    public void Dispose()
    {
        Uniforms.Dispose();
        Children.Dispose();
    }
}
