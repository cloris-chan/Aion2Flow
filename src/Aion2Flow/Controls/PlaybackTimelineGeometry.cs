using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

internal static class PlaybackTimelineGeometry
{
    public static double PositionToX(double positionMilliseconds, PlaybackTimelineViewport viewport, double width)
    {
        var durationMilliseconds = viewport.DurationMilliseconds;
        if (durationMilliseconds <= 0d || width <= 0d || !double.IsFinite(width) || !double.IsFinite(positionMilliseconds))
            return 0d;

        var ratio = Math.Clamp((positionMilliseconds - viewport.StartMilliseconds) / durationMilliseconds, 0d, 1d);
        return Math.Clamp(ratio * width, 0d, width);
    }

    public static double XToPosition(double x, PlaybackTimelineViewport viewport, double width)
    {
        var durationMilliseconds = viewport.DurationMilliseconds;
        if (durationMilliseconds <= 0d || width <= 0d || !double.IsFinite(width) || !double.IsFinite(x))
            return viewport.StartMilliseconds;

        var ratio = Math.Clamp(x / width, 0d, 1d);
        return viewport.StartMilliseconds + ratio * durationMilliseconds;
    }

    public static bool TryClipSpan(double startMilliseconds, double endMilliseconds, PlaybackTimelineViewport viewport, out double clippedStartMilliseconds, out double clippedEndMilliseconds)
    {
        if (viewport.IsEmpty ||
            !double.IsFinite(startMilliseconds) ||
            !double.IsFinite(endMilliseconds) ||
            endMilliseconds < startMilliseconds ||
            endMilliseconds < viewport.StartMilliseconds ||
            startMilliseconds > viewport.EndMilliseconds)
        {
            clippedStartMilliseconds = 0d;
            clippedEndMilliseconds = 0d;
            return false;
        }

        clippedStartMilliseconds = Math.Max(startMilliseconds, viewport.StartMilliseconds);
        clippedEndMilliseconds = Math.Min(endMilliseconds, viewport.EndMilliseconds);
        return true;
    }
}
