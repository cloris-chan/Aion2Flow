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

    public static ScenePlaybackTimeRange ResolveTimeRange(
        SceneJournalSegment segment,
        SceneCombatSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveTimeRange(segment, snapshot, cancellationToken, out var timeRange))
            cancellationToken.ThrowIfCancellationRequested();

        return timeRange;
    }

    public static bool TryResolveTimeRange(
        SceneJournalSegment segment,
        SceneCombatSnapshot snapshot,
        CancellationToken cancellationToken,
        out ScenePlaybackTimeRange timeRange)
    {
        var end = 0L;
        var hasEntries = false;
        var cursor = segment.CreateCursor();
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                timeRange = default;
                return false;
            }

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

        if (cancellationToken.IsCancellationRequested)
        {
            timeRange = default;
            return false;
        }

        if (hasEntries)
        {
            timeRange = new ScenePlaybackTimeRange(0, end, end, true);
            return true;
        }

        if (snapshot.EncounterEndTime > 0)
        {
            timeRange = new ScenePlaybackTimeRange(0, snapshot.EncounterEndTime, snapshot.EncounterEndTime, true);
            return true;
        }

        timeRange = default;
        return true;
    }

    public static ScenePlaybackTimeRange ExtendTimeRange(
        SceneJournalSegment segment,
        long startObservationOrdinal,
        ScenePlaybackTimeRange current,
        SceneCombatSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!TryExtendTimeRange(segment, startObservationOrdinal, current, snapshot, cancellationToken, out var timeRange))
            cancellationToken.ThrowIfCancellationRequested();

        return timeRange;
    }

    public static bool TryExtendTimeRange(
        SceneJournalSegment segment,
        long startObservationOrdinal,
        ScenePlaybackTimeRange current,
        SceneCombatSnapshot snapshot,
        CancellationToken cancellationToken,
        out ScenePlaybackTimeRange timeRange)
    {
        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var scanStart = Math.Clamp(
            startObservationOrdinal,
            segment.StartObservationOrdinal,
            endObservationOrdinalExclusive);
        if (scanStart >= endObservationOrdinalExclusive)
        {
            timeRange = current;
            return true;
        }

        var end = current.HasTiming ? current.EndOffsetMilliseconds : 0L;
        var hasEntries = false;
        var scanSegment = new SceneJournalSegment(
            segment.Journal,
            scanStart,
            endObservationOrdinalExclusive,
            IsLiveGrowing: false);
        var cursor = scanSegment.CreateCursor();
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                timeRange = default;
                return false;
            }

            var result = scanSegment.ReadEntries(cursor, DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    hasEntries = true;
                    end = Math.Max(end, ResolveOffsetMilliseconds(entries[i]));
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            timeRange = default;
            return false;
        }

        if (hasEntries || current.HasTiming)
        {
            timeRange = new ScenePlaybackTimeRange(0, end, end, true);
            return true;
        }

        if (snapshot.EncounterEndTime > 0)
        {
            timeRange = new ScenePlaybackTimeRange(0, snapshot.EncounterEndTime, snapshot.EncounterEndTime, true);
            return true;
        }

        timeRange = default;
        return true;
    }

    public static long ResolveOffsetMilliseconds(ObservedEventEntry entry) => Math.Max(0, entry.ObservedAtMilliseconds);
}
