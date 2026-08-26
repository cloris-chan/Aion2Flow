using System.Security.Cryptography;
using Avalonia.Media;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Tests.Support;
using SkiaSharp;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CooldownSkillVisualTests
{
    public CooldownSkillVisualTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Fact]
    public void ClientWidgetStyle_UsesTheExtractedQuickSlotValues()
    {
        Assert.Equal(70f, CooldownSkillVisualClientStyle.NativeWidgetSize);
        Assert.Equal(2f, CooldownSkillVisualClientStyle.CornerRadius);
        Assert.Equal(70f, CooldownSkillVisualClientStyle.TailSourceWidth);
        Assert.Equal(2f, CooldownSkillVisualClientStyle.TailSourceHeight);
        Assert.Equal(12f, CooldownSkillVisualClientStyle.TailGlowScaleY);
        Assert.Equal(-1f, CooldownSkillVisualClientStyle.TailTranslationY);
        Assert.Equal("99000000", Hex(CooldownSkillVisualClientStyle.DimmedColor));
        Assert.Equal("7F008493", Hex(CooldownSkillVisualClientStyle.CooldownFillColor));
    }

    [Fact]
    public void CompletionAnimation_UsesTheCookedClientAnimationTiming()
    {
        Assert.Equal(317L, CooldownSkillVisualClientAnimation.DurationMilliseconds);
        Assert.Equal(
            new short[] { 0, 17, 33, 50, 67, 83, 100, 117, 133, 150, 167, 183, 200, 217, 233, 250, 267, 283, 300 },
            CooldownSkillVisualClientAnimation.FrameStartMilliseconds.ToArray());
        Assert.Equal(0, CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_000L).FrameIndex);
        Assert.Equal(0, CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_016L).FrameIndex);
        Assert.Equal(1, CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_017L).FrameIndex);
        Assert.Equal(17, CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_299L).FrameIndex);
        Assert.Equal(18, CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_300L).FrameIndex);
        Assert.True(CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_316L).IsActive);
        Assert.False(CooldownSkillVisualClientAnimation.Resolve(1_000L, 1_317L).IsActive);
    }

    [Fact]
    public void ClientCompletionFrames_MatchTheExtractedClientSequence()
    {
        var frames = CooldownSkillMaterialTextureData.Shared.CompletionFrames;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var frame in frames)
            hash.AppendData(frame);

        Assert.Equal(19, frames.Length);
        Assert.Equal(
            "42387A99607D8521F71891B0B0D6D702AEB49F7E1686412AF5B164F5D3B1A9E7",
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Fact]
    public void ClientCompletionFrames_ContainOnlyTheCookedAdditiveEffect()
    {
        var frames = CooldownSkillMaterialTextureData.Shared.CompletionFrames;
        var effectPixelCount = 0;
        foreach (var frame in frames)
        {
            using var bitmap = SKBitmap.Decode(frame);
            Assert.NotNull(bitmap);
            Assert.Equal(64, bitmap.Width);
            Assert.Equal(64, bitmap.Height);
            foreach (var pixel in bitmap.Pixels)
            {
                Assert.Equal(byte.MaxValue, pixel.Alpha);
                if (pixel.Red == 0 && pixel.Green == 0 && pixel.Blue == 0)
                    continue;

                effectPixelCount++;
                Assert.True(pixel.Red + 2 >= pixel.Blue, $"Unexpected completion color {pixel}");
                Assert.True(pixel.Green + 6 >= pixel.Blue, $"Unexpected completion color {pixel}");
            }
        }

        Assert.True(effectPixelCount > 0);
    }

    [Fact]
    public void ClientCompletionFrames_PreserveTheCookedRadialStreakLayer()
    {
        using var bitmap = SKBitmap.Decode(CooldownSkillMaterialTextureData.Shared.CompletionFrames[4]);
        Assert.NotNull(bitmap);

        var center = Brightness(bitmap.GetPixel(32, 32));
        var radialSamples = new[]
        {
            bitmap.GetPixel(32, 4),
            bitmap.GetPixel(10, 10),
            bitmap.GetPixel(18, 18),
            bitmap.GetPixel(50, 18)
        };

        Assert.All(radialSamples, color => Assert.True(Brightness(color) > center + 80));
    }

    [Fact]
    public void ClientCompletionFrames_PreserveTheCookedSoftRectangularFillRetreat()
    {
        using var bitmap = SKBitmap.Decode(CooldownSkillMaterialTextureData.Shared.CompletionFrames[4]);
        Assert.NotNull(bitmap);

        var outerWarmth = WarmComponent(bitmap.GetPixel(4, 32));
        var transitionWarmth = WarmComponent(bitmap.GetPixel(12, 32));
        var innerWarmth = WarmComponent(bitmap.GetPixel(16, 32));

        Assert.True(outerWarmth > 60);
        Assert.InRange(transitionWarmth, 20, outerWarmth - 10);
        Assert.Equal(0, innerWarmth);
        Assert.Equal(0, Brightness(bitmap.GetPixel(32, 32)));

        using var later = SKBitmap.Decode(CooldownSkillMaterialTextureData.Shared.CompletionFrames[7]);
        Assert.NotNull(later);
        Assert.Equal(0, Brightness(later.GetPixel(32, 32)));
    }

    [Fact]
    public void VisualState_ClampsThePacketProjectionBeforePublishingIt()
    {
        var visual = new CooldownSkillVisual { CooldownProgress = double.NaN };

        Assert.Equal(0f, visual.CreateVisualStateForDiagnostics().CooldownProgress);

        visual.CooldownProgress = 2d;
        Assert.Equal(1f, visual.CreateVisualStateForDiagnostics().CooldownProgress);

        visual.CooldownProgress = 0.5d;
        var state = visual.CreateVisualStateForDiagnostics();
        Assert.True(state.HasCooldown);
        Assert.True(state.HasTail);
    }

    [Fact]
    public void CooldownMask_DimsOnlyTheRemainingHeight()
    {
        using var surface = SKSurface.Create(new SKImageInfo(70, 70));
        var source = new SKColor(0x80, 0x40, 0x20, 0xFF);
        surface.Canvas.Clear(source);

        CooldownSkillVisualHandler.RenderCooldownMaskForDiagnostics(surface.Canvas, 70f, 70f, 0.5f);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal(source, bitmap.GetPixel(35, 10));
        Assert.Equal(new SKColor(26, 79, 80, 0xFF), bitmap.GetPixel(35, 55));
    }

    [Fact]
    public void ClientTailShader_CompilesAgainstBundledSkiaRuntime()
    {
        using var tail = CooldownSkillVisualHandler.CompileTailShaderForDiagnostics();

        Assert.NotNull(tail);
    }

    [Fact]
    public void ClientCompletionFrames_ChangeAcrossTheCookedSequence()
    {
        var first = RenderCompletionFrame(0);
        var middle = RenderCompletionFrame(9);
        var final = RenderCompletionFrame(18);

        Assert.Contains(first, HasEffectColor);
        Assert.Contains(middle, HasEffectColor);
        Assert.Contains(final, HasEffectColor);
        Assert.True(first.Zip(middle, static (left, right) => left != right).Count(static changed => changed) > 0);
        Assert.True(middle.Zip(final, static (left, right) => left != right).Count(static changed => changed) > 0);
    }

    [Fact]
    public void TailMaterialParameters_MatchTheExtractedBlinkInstance()
    {
        Assert.Equal(-15f, CooldownSkillTailSkiaProgram.T2PanningU);
        Assert.Equal(0.03f, CooldownSkillTailSkiaProgram.T2TilingU);
        Assert.Equal(0.01f, CooldownSkillTailSkiaProgram.T2TilingV);
        Assert.Equal(-100f, CooldownSkillTailSkiaProgram.T3PanningU);
        Assert.Equal(0.03f, CooldownSkillTailSkiaProgram.T3TilingU);
        Assert.Equal(0.01f, CooldownSkillTailSkiaProgram.T3TilingV);
    }

    [Fact]
    public void ClientTailShader_ChangesAcrossTheExtractedAnimationTime()
    {
        var firstTail = RenderTail(0f);
        var secondTail = RenderTail(1.37f);

        Assert.Contains(firstTail, static color => color.Alpha > 0);
        Assert.True(firstTail[16 * 70].Alpha < 8);
        Assert.True(firstTail[28 * 70 + 35].Alpha > firstTail[16 * 70].Alpha);
        Assert.True(firstTail.Zip(secondTail, static (left, right) => left != right).Count(static changed => changed) > 0);
    }

    private static string Hex(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool HasEffectColor(SKColor color) => color.Red > 0 || color.Green > 0 || color.Blue > 0;

    private static int Brightness(SKColor color) => color.Red + color.Green + color.Blue;

    private static int WarmComponent(SKColor color) => color.Red - color.Blue;

    private static SKColor[] RenderCompletionFrame(int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(70, 70));
        surface.Canvas.Clear(SKColors.Transparent);
        CooldownSkillVisualHandler.RenderCompletionFrameForDiagnostics(surface.Canvas, 70f, 70f, frameIndex);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    private static SKColor[] RenderTail(float time)
    {
        var program = CooldownSkillTailSkiaProgram.GetForCurrentThread();
        using var instance = program.CreateInstance();
        instance.Uniforms["origin"] = new SKPoint(0f, 16f);
        instance.Uniforms["size"] = new SKPoint(70f, 24f);
        instance.Uniforms["time"] = time;
        instance.Uniforms["tailGlowAddColor"] = CooldownSkillVisualClientStyle.TailGlowAddColor;
        instance.Uniforms["tailGlowColor"] = CooldownSkillVisualClientStyle.TailGlowColor;
        using var shader = instance.Build();
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Plus };
        using var surface = SKSurface.Create(new SKImageInfo(70, 56));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0f, 16f, 70f, 40f, paint);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }
}
