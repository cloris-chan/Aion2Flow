using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal static class ScenePlaybackTimeline
{
    public const int DefaultReadBatchSize = 512;

    public static long ClampPosition(long positionMilliseconds, long durationMilliseconds)
    {
        if (positionMilliseconds <= 0)
            return 0;

        return durationMilliseconds > 0 ? Math.Min(positionMilliseconds, durationMilliseconds) : positionMilliseconds;
    }

    public static ScenePlaybackTimeRange ResolveTimeRange(SceneJournalSegment segment, SceneCombatSnapshot snapshot)
    {
        var start = long.MaxValue;
        var end = long.MinValue;
        var cursor = segment.CreateCursor();
        while (true)
        {
            var result = segment.ReadEntries(cursor, DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var timestamp = ResolveTimestampMilliseconds(in entry);
                    if (timestamp <= 0)
                        continue;

                    start = Math.Min(start, timestamp);
                    end = Math.Max(end, timestamp);
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (start != long.MaxValue && end != long.MinValue)
            return new ScenePlaybackTimeRange(start, end, Math.Max(0, end - start), true);

        if (snapshot.EncounterStartTime > 0)
        {
            var fallbackEnd = snapshot.EncounterEndTime >= snapshot.EncounterStartTime ? snapshot.EncounterEndTime : snapshot.EncounterStartTime;
            return new ScenePlaybackTimeRange(snapshot.EncounterStartTime, fallbackEnd, Math.Max(0, fallbackEnd - snapshot.EncounterStartTime), true);
        }

        return default;
    }

    public static long ResolvePositionMilliseconds(ScenePlaybackTimeRange timeRange, long timestamp)
    {
        if (!timeRange.HasTimestamps || timestamp <= 0)
            return Math.Max(0, timestamp);

        return Math.Max(0, timestamp - timeRange.StartTimestampMilliseconds);
    }

    public static long ResolveTimestampMilliseconds(in ObservedEventEnvelope entry)
    {
        if (entry.Raw.TimestampMilliseconds > 0)
            return entry.Raw.TimestampMilliseconds;

        return entry.Stamp.OffsetTicks > 0 ? entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond : 0;
    }
}
