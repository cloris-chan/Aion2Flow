using Cloris.Aion2Flow.Controls;
using SkiaSharp;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class HudSkiaOpacityScopeTests
{
    [Theory]
    [InlineData(1d, 255)]
    [InlineData(0.5d, 128)]
    public void Push_CompositesOverlappingPrimitivesAsSingleLayer(double opacity, byte expectedAlpha)
    {
        const int width = 4;
        const int height = 2;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var layerPaint = new SKPaint();
        using var contentPaint = new SKPaint { Color = SKColors.Red };
        surface.Canvas.Clear(SKColors.Transparent);

        using (HudSkiaOpacityScope.Push(surface.Canvas, layerPaint, new SKRect(0f, 0f, width, height), opacity))
        {
            surface.Canvas.DrawRect(0f, 0f, 3f, height, contentPaint);
            contentPaint.Color = SKColors.Blue;
            surface.Canvas.DrawRect(1f, 0f, 3f, height, contentPaint);
        }

        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.All(bitmap.Pixels, color => Assert.Equal(expectedAlpha, color.Alpha));
    }

    [Fact]
    public void Dispose_RestoresNestedSavesToTheScopeBoundary()
    {
        using var surface = SKSurface.Create(new SKImageInfo(4, 2));
        using var layerPaint = new SKPaint();
        var initialSaveCount = surface.Canvas.SaveCount;

        using (HudSkiaOpacityScope.Push(surface.Canvas, layerPaint, new SKRect(0f, 0f, 4f, 2f), 0.5d))
        {
            surface.Canvas.Save();
            surface.Canvas.ClipRect(new SKRect(0f, 0f, 1f, 1f));
        }

        Assert.Equal(initialSaveCount, surface.Canvas.SaveCount);
    }
}
