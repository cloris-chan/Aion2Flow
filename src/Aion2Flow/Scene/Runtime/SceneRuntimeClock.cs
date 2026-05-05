using Cloris.Aion2Flow.Scene.Model;

namespace Cloris.Aion2Flow.Scene.Runtime;

public sealed class SceneRuntimeClock(long startMonotonicTicks)
{
    private long _nextObservationOrdinal;

    public long NextObservationOrdinal => _nextObservationOrdinal;

    public TimelineStamp CreateStamp(long packetTimestampMilliseconds, long frameOrdinal, long batchOrdinal)
    {
        long offsetTicks = packetTimestampMilliseconds * TimeSpan.TicksPerMillisecond - startMonotonicTicks;

        return new TimelineStamp
        {
            OffsetTicks = offsetTicks,
            ObservationOrdinal = _nextObservationOrdinal++,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
    }

    public TimelineStamp CreateStampFromOffset(long offsetTicks, long frameOrdinal, long batchOrdinal)
    {
        return new TimelineStamp
        {
            OffsetTicks = offsetTicks,
            ObservationOrdinal = _nextObservationOrdinal++,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
    }
}
