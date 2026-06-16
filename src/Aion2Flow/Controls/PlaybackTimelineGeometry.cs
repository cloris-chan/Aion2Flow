namespace Cloris.Aion2Flow.Controls;

internal static class PlaybackTimelineGeometry
{
    public static double PositionToX(double positionMilliseconds, double durationMilliseconds, double width)
    {
        if (durationMilliseconds <= 0 || width <= 0)
            return 0d;

        var ratio = Math.Clamp(positionMilliseconds / durationMilliseconds, 0d, 1d);
        return Math.Clamp(ratio * width, 0d, width);
    }

    public static double XToPosition(double x, double durationMilliseconds, double width)
    {
        if (durationMilliseconds <= 0 || width <= 0)
            return 0d;

        var ratio = Math.Clamp(x / width, 0d, 1d);
        return ratio * durationMilliseconds;
    }
}
