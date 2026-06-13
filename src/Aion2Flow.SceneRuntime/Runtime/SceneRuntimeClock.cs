using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneRuntimeClock(long sceneStartedAtMilliseconds)
{
    private long _nextObservationOrdinal;
    private long _sceneStartedAtMilliseconds = sceneStartedAtMilliseconds;

    public long NextObservationOrdinal => Volatile.Read(ref _nextObservationOrdinal);
    internal long SceneStartedAtMilliseconds => Volatile.Read(ref _sceneStartedAtMilliseconds);

    public void Reset(DateTimeOffset sceneStartedAt) => Volatile.Write(ref _sceneStartedAtMilliseconds, sceneStartedAt.ToUnixTimeMilliseconds());

    public TimelineStamp CreateStamp(long captureTimestampMilliseconds, long frameOrdinal, long batchOrdinal)
    {
        var offsetMilliseconds = captureTimestampMilliseconds - Volatile.Read(ref _sceneStartedAtMilliseconds);
        var offsetTicks = Math.Max(0, offsetMilliseconds) * TimeSpan.TicksPerMillisecond;

        return new TimelineStamp
        {
            OffsetTicks = offsetTicks,
            ObservationOrdinal = Interlocked.Increment(ref _nextObservationOrdinal) - 1,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
    }

}
