namespace Cloris.Aion2Flow.Capture;

internal sealed class CaptureTimestampMapper
{
    private readonly TimeProvider _timeProvider;
    private readonly long _originTimestamp;
    private readonly long _originUnixMilliseconds;

    public CaptureTimestampMapper()
        : this(TimeProvider.System)
    {
    }

    internal CaptureTimestampMapper(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _originTimestamp = timeProvider.GetTimestamp();
        _originUnixMilliseconds = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    public long ToTimelineUnixMilliseconds(long captureTimestamp)
    {
        var elapsedMilliseconds = _timeProvider.GetElapsedTime(_originTimestamp, captureTimestamp).Ticks /
            TimeSpan.TicksPerMillisecond;
        return checked(_originUnixMilliseconds + elapsedMilliseconds);
    }

    public long ToCurrentUtcUnixMilliseconds(long captureTimestamp)
    {
        var calibrationTimestamp = _timeProvider.GetTimestamp();
        var calibrationUnixMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var elapsedMilliseconds = _timeProvider.GetElapsedTime(captureTimestamp, calibrationTimestamp).Ticks /
            TimeSpan.TicksPerMillisecond;
        return checked(calibrationUnixMilliseconds - elapsedMilliseconds);
    }
}
