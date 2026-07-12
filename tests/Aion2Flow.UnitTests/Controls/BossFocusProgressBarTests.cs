using System.Numerics;
using System.Security.Cryptography;
using Avalonia.Media;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Presentation;
using SkiaSharp;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class BossFocusProgressBarTests
{
    public BossFocusProgressBarTests()
    {
        AvaloniaTestHost.EnsureInitialized();
    }

    [Fact]
    public void VisualState_ClampsSegmentAndAnimationParameters()
    {
        var bar = new BossFocusProgressBar
        {
            Segment = new ProgressSegment(1.25d, new SolidColorBrush(Color.FromArgb(200, 20, 40, 60), 0.5d)),
            Background = new SolidColorBrush(Color.FromRgb(1, 2, 3)),
            OuterShadowBrush = new SolidColorBrush(Color.FromRgb(4, 5, 6)),
            FrameBrush = new SolidColorBrush(Color.FromRgb(7, 8, 9)),
            InnerShadowBrush = new SolidColorBrush(Color.FromRgb(10, 11, 12)),
            ChamferWidth = -8d,
            FrameThickness = -2d,
            FlowSpeed = 8d,
            FlowStrength = -1d
        };

        var state = bar.CreateVisualStateForDiagnostics();

        Assert.Equal(1f, state.Ratio);
        Assert.Equal(Color.FromArgb(100, 20, 40, 60), state.FillColor);
        Assert.Equal(Color.FromRgb(1, 2, 3), state.TrackColor);
        Assert.Equal(Color.FromRgb(4, 5, 6), state.OuterShadowColor);
        Assert.Equal(Color.FromRgb(7, 8, 9), state.FrameColor);
        Assert.Equal(Color.FromRgb(10, 11, 12), state.InnerShadowColor);
        Assert.Equal(0f, state.ChamferWidth);
        Assert.Equal(0f, state.FrameThickness);
        Assert.Equal(4f, state.FlowSpeed);
        Assert.Equal(0f, state.FlowStrength);
        Assert.False(state.ShouldAnimate);
    }

    [Fact]
    public void Geometry_CreatesSymmetricHexagonWithoutAcuteEndPoints()
    {
        var geometry = BossFocusProgressBarGeometry.Create(400f, 26f, 8f, 2f);

        AssertVector(new Vector2(0f, 13f), geometry.Shadow.LeftPoint);
        AssertVector(new Vector2(1.5f, 13f), geometry.Outer.LeftPoint);
        AssertVector(new Vector2(8.577f, 1.5f), geometry.Outer.TopLeft);
        AssertVector(new Vector2(391.423f, 1.5f), geometry.Outer.TopRight);
        AssertVector(new Vector2(398.5f, 13f), geometry.Outer.RightPoint);
        AssertVector(new Vector2(391.423f, 24.5f), geometry.Outer.BottomRight);
        AssertVector(new Vector2(8.577f, 24.5f), geometry.Outer.BottomLeft);
        AssertVector(new Vector2(3.5f, 13f), geometry.Inner.LeftPoint);
        AssertVector(new Vector2(396.5f, 13f), geometry.Inner.RightPoint);

        var upperEdge = geometry.Outer.TopLeft - geometry.Outer.LeftPoint;
        var lowerEdge = geometry.Outer.BottomLeft - geometry.Outer.LeftPoint;
        var endAngle = MathF.Acos(Vector2.Dot(upperEdge, lowerEdge) / (upperEdge.Length() * lowerEdge.Length())) * 180f / MathF.PI;
        Assert.InRange(endAngle, 116f, 118f);
    }

    [Fact]
    public void MaterialRuntimeEffects_CompileAgainstBundledSkiaRuntime()
    {
        using var shader = BossFocusProgressBarVisualHandler.CompileShaderForDiagnostics();
        using var blender = BossFocusProgressBarVisualHandler.CompileBlenderForDiagnostics();

        Assert.NotNull(shader);
        Assert.NotNull(blender);
    }

    [Fact]
    public void ClientGaugeTextures_MatchCurrentMaterialExports()
    {
        var textures = BossFocusMaterialTextureData.Shared;

        Assert.Equal("9D5D442A889F3B367E7978B645592647ADF0052397269251658A2C989E0601D1", Hash(textures.FineTrail));
        Assert.Equal("66D79DF6F64E8D17DB0CEE8FF82929DAF446A90EC987F07DE26B9C953E2F9234", Hash(textures.BroadTrail));
        Assert.Equal("E12ED69273E2789F96B2A2CB6B01D40DAC20FE0886A4FD5BF7CE60431785F7EF", Hash(textures.Gradient));
    }

    [Fact]
    public void Shader_RendersClientMaterialFlowWithStableTimePeriod()
    {
        var first = RenderFrame(0f);
        var second = RenderFrame(0.75f);
        var nextPeriod = RenderFrame(BossFocusProgressBarSkiaProgram.TimePeriodSeconds);

        Assert.Contains(first, static color => color.Alpha > 0);
        Assert.True(first.Distinct().Count() > 8);
        Assert.False(first.SequenceEqual(second));
        Assert.Equal(first, nextPeriod);
    }

    private static SKColor[] RenderFrame(float time)
    {
        const int width = 360;
        const int height = 18;
        var program = BossFocusProgressBarSkiaProgram.GetForCurrentThread();
        using var instance = program.CreateInstance();
        instance.Uniforms["origin"] = new SKPoint(0f, 0f);
        instance.Uniforms["size"] = new SKPoint(width, height);
        instance.Uniforms["time"] = time;
        instance.Uniforms["speed"] = 1f;
        instance.Uniforms["strength"] = 1f;

        using var shader = instance.Build();
        using var paint = new SKPaint { Shader = shader, Blender = program.Blender };
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(new SKColor(0xDF, 0x21, 0x4A));
        surface.Canvas.DrawRect(0f, 0f, width, height, paint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    private static void AssertVector(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 3);
        Assert.Equal(expected.Y, actual.Y, precision: 3);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
