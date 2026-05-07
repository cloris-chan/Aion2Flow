using Cloris.Aion2Flow.Scene.Model;

namespace Cloris.Aion2Flow.Scene.Runtime;

public sealed class SceneRuntimeClock(long startMonotonicTicks)
{
    private long _nextObservationOrdinal;

    public long NextObservationOrdinal => Volatile.Read(ref _nextObservationOrdinal);

    public TimelineStamp CreateStamp(long packetTimestampMilliseconds, long frameOrdinal, long batchOrdinal)
    {
        long offsetTicks = packetTimestampMilliseconds * TimeSpan.TicksPerMillisecond - startMonotonicTicks;

        return new TimelineStamp
        {
            OffsetTicks = offsetTicks,
            ObservationOrdinal = Interlocked.Increment(ref _nextObservationOrdinal) - 1,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
    }

    public TimelineStamp CreateStampFromOffset(long offsetTicks, long frameOrdinal, long batchOrdinal)
    {
        return new TimelineStamp
        {
            OffsetTicks = offsetTicks,
            ObservationOrdinal = Interlocked.Increment(ref _nextObservationOrdinal) - 1,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
    }
}
