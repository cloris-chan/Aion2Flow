using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;

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
        var end = 0L;
        var hasEntries = false;
        var cursor = segment.CreateCursor();
        while (true)
        {
            var result = segment.ReadEntries(cursor, DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    hasEntries = true;
                    end = Math.Max(end, ResolveOffsetMilliseconds(entry));
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (hasEntries)
            return new ScenePlaybackTimeRange(0, end, end, true);

        if (snapshot.EncounterEndTime > 0)
        {
            return new ScenePlaybackTimeRange(0, snapshot.EncounterEndTime, snapshot.EncounterEndTime, true);
        }

        return default;
    }

    public static long ResolveOffsetMilliseconds(ObservedEventEntry entry) => Math.Max(0, entry.ObservedAtMilliseconds);
}
