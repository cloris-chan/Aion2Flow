using SkiaSharp;

namespace Cloris.Aion2Flow.Controls;

internal readonly struct HudSkiaOpacityScope : IDisposable
{
    private readonly SKCanvas? _canvas;
    private readonly int _restoreCount;

    private HudSkiaOpacityScope(SKCanvas canvas, int restoreCount)
    {
        _canvas = canvas;
        _restoreCount = restoreCount;
    }

    internal static HudSkiaOpacityScope Push(SKCanvas canvas, SKPaint layerPaint, SKRect bounds, double opacity)
    {
        var resolvedOpacity = ResolveOpacity(opacity);
        if (resolvedOpacity >= 1f)
            return default;

        // Raw SKCanvas calls do not consume the paint-level opacity carried by an Avalonia Skia lease.
        layerPaint.ColorF = new SKColorF(1f, 1f, 1f, resolvedOpacity);
        var restoreCount = canvas.SaveLayer(bounds, layerPaint);
        return new HudSkiaOpacityScope(canvas, restoreCount);
    }

    public void Dispose() => _canvas?.RestoreToCount(_restoreCount);

    private static float ResolveOpacity(double opacity) =>
        double.IsFinite(opacity)
            ? (float)Math.Clamp(opacity, 0d, 1d)
            : 1f;
}
