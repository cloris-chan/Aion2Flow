using System.Security.Cryptography;
using Avalonia.Media;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Tests.Support;
using SkiaSharp;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class QuestSelectionHighlightTests
{
    public QuestSelectionHighlightTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Fact]
    public void VisualState_TracksSelectionAndBackgroundOnly()
    {
        var highlight = new QuestSelectionHighlight
        {
            SelectionBackground = new SolidColorBrush(Color.FromArgb(64, 1, 2, 3))
        };

        var inactive = highlight.CreateVisualStateForDiagnostics();
        highlight.IsActive = true;
        var active = highlight.CreateVisualStateForDiagnostics();

        Assert.False(inactive.ShouldAnimate);
        Assert.True(active.ShouldAnimate);
        Assert.Equal(Color.FromArgb(64, 1, 2, 3), active.BackgroundColor);
    }

    [Fact]
    public void SelectionFlow_UsesClientTintsAndScaledLeftAlignedBounds()
    {
        var bounds = QuestSelectionHighlightVisualHandler.ResolveFlowLayerBounds(Width, Height);

        Assert.Equal(1.5f, QuestSelectionHighlightVisualHandler.FlowLayerScale);
        Assert.Equal(0f, bounds.Left);
        Assert.Equal(-10.5f, bounds.Top);
        Assert.Equal(660f, bounds.Right);
        Assert.Equal(52.5f, bounds.Bottom);
        Assert.Equal(0x46 / 255f, QuestFlowMaterialSkiaProgram.ClientTintColor.Red);
        Assert.Equal(0x64 / 255f, QuestFlowMaterialSkiaProgram.ClientTintColor.Green);
        Assert.Equal(0x66 / 255f, QuestFlowMaterialSkiaProgram.ClientTintColor.Blue);
        Assert.Equal(0xBE / 255f, QuestSelectionParticleSkiaProgram.ClientTintColor.Red);
        Assert.Equal(0xF7 / 255f, QuestSelectionParticleSkiaProgram.ClientTintColor.Green);
        Assert.Equal(0xFA / 255f, QuestSelectionParticleSkiaProgram.ClientTintColor.Blue);
    }

    [Fact]
    public void MaterialRuntimeEffects_CompileAgainstBundledSkiaRuntime()
    {
        using var wave = QuestSelectionHighlightVisualHandler.CompileWaveShaderForDiagnostics();
        using var particle = QuestSelectionHighlightVisualHandler.CompileParticleShaderForDiagnostics();
        using var edge = QuestSelectionHighlightVisualHandler.CompileEdgeShaderForDiagnostics();
        using var edgeBaseline = QuestSelectionHighlightVisualHandler.CompileEdgeBaselineShaderForDiagnostics();
        using var blender = QuestSelectionHighlightVisualHandler.CompileBlenderForDiagnostics();

        Assert.NotNull(wave);
        Assert.NotNull(particle);
        Assert.NotNull(edge);
        Assert.NotNull(edgeBaseline);
        Assert.NotNull(blender);
    }

    [Fact]
    public void ClientSelectionTextures_MatchCurrentMaterialExports()
    {
        var wave = QuestFlowMaterialTextureData.Shared;
        var particle = QuestSelectionParticleTextureData.Shared;
        var edge = QuestCompletionEdgeTextureData.Shared;

        Assert.Equal(8, QuestFlowMaterialSkiaTextures.MaxAnisotropy);
        Assert.Equal(8, QuestSelectionParticleSkiaTextures.MaxAnisotropy);
        Assert.Equal("66D79DF6F64E8D17DB0CEE8FF82929DAF446A90EC987F07DE26B9C953E2F9234", Hash(wave.Trail));
        Assert.Equal("A5404DD2B6078AED0F481E446E017B32E98E50A99B57E5D176012139408AE4F9", Hash(wave.Mask));
        Assert.Equal("5B10F29EF39969FC798F13CD51FC6164BC76C17D866F14F887446DBFAB2FDD51", Hash(wave.Mix));
        Assert.Equal("38AC7E49F136C6B1293C8A6ACFF9705E6F5B0A5FF96E8BC6BF3D5F62BEB798EA", Hash(wave.Normal));
        Assert.Equal("E12ED69273E2789F96B2A2CB6B01D40DAC20FE0886A4FD5BF7CE60431785F7EF", Hash(wave.DistortionMask));
        Assert.Equal("06681AE41212D11BBD9E2B5F83B1B0BCF3297BBA904CB29FFAADB6598A3FD2B8", Hash(particle.Particle));
        Assert.Equal("3055C86965F1FA2C2011E406B1A0C80166439950C679D8156AE98AB26066D3CB", Hash(particle.Mask));
        Assert.Equal("99172FBFD17042A768C82421381B003317AA6A642E0088C15F832621C216B074", Hash(particle.Noise));
        Assert.Equal("38AC7E49F136C6B1293C8A6ACFF9705E6F5B0A5FF96E8BC6BF3D5F62BEB798EA", Hash(particle.Normal));
        Assert.Equal("341BBFD9D4E6543B1EA7C2606EBF5492493D78EAC7A812B230F60194337AF9EF", Hash(edge.Flare));
        Assert.Equal("A5E63F6074452304B497D48D96D37575F4D9DBD0A3D8AA8FC6F3C5CD5CDF18C1", Hash(edge.Mask));
        Assert.Equal("13441F1596EAA7AED12F2DDB19F6A9A27FF0688ACC7A784097DD8615D724080F", Hash(edge.Baseline));
    }

    [Fact]
    public void SelectionMaterial_RendersAcrossNativeRowHeightAndChangesOverTime()
    {
        var first = RenderFrame(1.5f);
        var second = RenderFrame(2.25f);
        var changedPixelCount = first.Zip(second, static (left, right) => left != right).Count(static changed => changed);
        var changedRows = 0;

        for (var y = 0; y < Height; y++)
        {
            var offset = y * Width;
            if (first.AsSpan(offset, Width).ContainsAnyExcept(second.AsSpan(offset, Width)))
                changedRows++;
        }

        Assert.Equal(60f * 4f / 44f, QuestFlowMaterialSkiaProgram.WidgetUvCompressionY);
        Assert.True(changedPixelCount > Width, $"Expected animated selection pixels, got {changedPixelCount} changes.");
        Assert.True(changedRows >= Height / 2, $"Expected animation across the selected row, got {changedRows}/{Height} changed rows.");
        Assert.Contains(first, static color => color.Alpha > 0 && color.Red + color.Green + color.Blue > 0);
    }

    [Fact]
    public void CompletionEdgeParameters_MatchCurrentClientInstance()
    {
        Assert.Equal(20f, QuestCompletionEdgeSkiaProgram.BandHeight);
        Assert.Equal(1f, QuestCompletionEdgeSkiaProgram.BaselineHeight);
        Assert.Equal(1f, QuestCompletionEdgeSkiaProgram.MainPanningU);
        Assert.Equal(1f, QuestCompletionEdgeSkiaProgram.MainOffsetU);
        Assert.Equal(2f, QuestCompletionEdgeSkiaProgram.MainTilingU);
        Assert.Equal(2f, QuestCompletionEdgeSkiaProgram.MainTilingV);
        Assert.Equal(1.133581f, QuestCompletionEdgeSkiaProgram.MaskTilingU);
        Assert.Equal(1f, QuestCompletionEdgeSkiaProgram.MaskTilingV);
        Assert.Equal(0.25f, QuestCompletionEdgeSkiaProgram.MaskRotationTurns);
        Assert.Equal(3f, QuestCompletionEdgeSkiaProgram.MainColorMultiplier);
        Assert.Equal(Color.FromRgb(0xBA, 0xDC, 0xFF), QuestCompletionEdgeSkiaProgram.BaselineColor);
    }

    [Fact]
    public void CompletionEdgeFlow_ChangesOverTimeAndMirrorsBetweenBands()
    {
        var first = RenderCompletionEdgeBands(0.125f);
        var second = RenderCompletionEdgeBands(0.375f);
        var changedPixelCount = first.Zip(second, static (left, right) => left != right).Count(static changed => changed);
        var mirrorDifference = 0L;
        var centerY = EdgeBandHeight / 2;

        for (var x = 0; x < Width; x++)
        {
            var top = first[(centerY * Width) + x];
            var bottom = first[((centerY + EdgeBandHeight) * Width) + (Width - 1 - x)];
            mirrorDifference += Math.Abs(top.Red - bottom.Red);
            mirrorDifference += Math.Abs(top.Green - bottom.Green);
            mirrorDifference += Math.Abs(top.Blue - bottom.Blue);
        }

        Assert.True(changedPixelCount > Width, $"Expected moving completion edge pixels, got {changedPixelCount} changes.");
        Assert.True(mirrorDifference < Width * 6L, $"Expected horizontally mirrored edge bands, got RGB difference {mirrorDifference}.");
        Assert.Contains(first, static color => color.Alpha > 0 && color.Red + color.Green + color.Blue > 0);
    }

    [Fact]
    public void ParticleParameters_MatchCurrentClientInstance()
    {
        Assert.Equal(-0.1f, QuestSelectionParticleSkiaProgram.MainPanningU);
        Assert.Equal(4f, QuestSelectionParticleSkiaProgram.MainTiling);
        Assert.Equal(1.3f, QuestSelectionParticleSkiaProgram.MaskTiling);
        Assert.Equal(-0.05f, QuestSelectionParticleSkiaProgram.NoisePanningU);
        Assert.Equal(0.04f, QuestSelectionParticleSkiaProgram.NoisePanningV);
        Assert.Equal(5f, QuestSelectionParticleSkiaProgram.NoiseTiling);
        Assert.Equal(0.068f, QuestSelectionParticleSkiaProgram.DistortionPanningU);
        Assert.Equal(0.05f, QuestSelectionParticleSkiaProgram.DistortionPanningV);
        Assert.Equal(1.076974f, QuestSelectionParticleSkiaProgram.DistortionTilingU);
        Assert.Equal(1.276276f, QuestSelectionParticleSkiaProgram.DistortionTilingV);
        Assert.Equal(0.117333f, QuestSelectionParticleSkiaProgram.DistortionRotation);
        Assert.Equal(-0.443259f, QuestSelectionParticleSkiaProgram.DistortionIntensity);
        Assert.Equal(2f, QuestSelectionParticleSkiaProgram.NoiseMultiply);
    }

    private const int Width = 440;
    private const int Height = 42;
    private const int EdgeBandHeight = 20;

    private static SKColor[] RenderFrame(float time)
    {
        var waveProgram = QuestFlowMaterialSkiaProgram.GetForCurrentThread();
        using var wave = waveProgram.CreateInstance();
        using var particle = QuestSelectionParticleSkiaProgram.GetForCurrentThread().CreateInstance();
        var bounds = QuestSelectionHighlightVisualHandler.ResolveFlowLayerBounds(Width, Height);
        Configure(wave, time, bounds, QuestFlowMaterialSkiaProgram.ClientTintColor);
        Configure(particle, time, bounds, QuestSelectionParticleSkiaProgram.ClientTintColor);

        using var waveShader = wave.Build();
        using var particleShader = particle.Build();
        using var backgroundPaint = new SKPaint { Color = new SKColor(12, 26, 32, 72) };
        using var particlePaint = new SKPaint { Shader = particleShader, Blender = waveProgram.Blender };
        using var wavePaint = new SKPaint { Shader = waveShader, Blender = waveProgram.Blender };
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0f, 0f, Width, Height, backgroundPaint);
        surface.Canvas.DrawRect(bounds, particlePaint);
        surface.Canvas.DrawRect(bounds, wavePaint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    private static void Configure(HudShaderInstance instance, float time, SKRect bounds, SKColorF tint)
    {
        instance.Uniforms["origin"] = new SKPoint(bounds.Left, bounds.Top);
        instance.Uniforms["size"] = new SKPoint(bounds.Width, bounds.Height);
        instance.Uniforms["time"] = time;
        instance.Uniforms["flowColor"] = tint;
    }

    private static SKColor[] RenderCompletionEdgeBands(float time)
    {
        var edgeProgram = QuestCompletionEdgeSkiaProgram.GetForCurrentThread();
        using var top = edgeProgram.CreateInstance();
        using var bottom = edgeProgram.CreateInstance();
        ConfigureEdge(top, time, 0f, true);
        ConfigureEdge(bottom, time, EdgeBandHeight, false);

        using var topShader = top.Build();
        using var bottomShader = bottom.Build();
        using var topPaint = new SKPaint { Shader = topShader, Blender = QuestFlowMaterialSkiaProgram.GetForCurrentThread().Blender };
        using var bottomPaint = new SKPaint { Shader = bottomShader, Blender = QuestFlowMaterialSkiaProgram.GetForCurrentThread().Blender };
        using var surface = SKSurface.Create(new SKImageInfo(Width, EdgeBandHeight * 2));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0f, 0f, Width, EdgeBandHeight, topPaint);
        surface.Canvas.DrawRect(0f, EdgeBandHeight, Width, EdgeBandHeight, bottomPaint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    private static void ConfigureEdge(HudShaderInstance instance, float time, float top, bool mirrorX)
    {
        instance.Uniforms["origin"] = new SKPoint(0f, top);
        instance.Uniforms["size"] = new SKPoint(Width, EdgeBandHeight);
        instance.Uniforms["time"] = time;
        instance.Uniforms["mirrorX"] = mirrorX ? 1f : 0f;
        instance.Uniforms["edgeColor"] = QuestCompletionEdgeSkiaProgram.FlareSkColor;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
