using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackTrackReader
{
    private const int DefaultReadBatchSize = 512;

    public static ScenePlaybackTrackReadResult Read(SceneJournalSegment segment, ScenePlaybackTimeRange timeRange, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkers)
        => Read(segment, timeRange, startPositionMilliseconds, endPositionMilliseconds, maxMarkers, segment.CreateCursor());

    public static ScenePlaybackTrackReadResult Read(SceneJournalSegment segment, ScenePlaybackTimeRange timeRange, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkers, JournalCursor cursor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxMarkers);
        var markers = new List<ScenePlaybackTrackMarker>(Math.Min(maxMarkers, 256));
        var current = cursor;
        var hasMore = false;
        var stopWindow = false;
        while (markers.Count < maxMarkers)
        {
            JournalCursor? nextCursor = null;
            var result = segment.ReadEntries(current, DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var timestamp = ResolveTimestampMilliseconds(in entry);
                    var position = ResolvePositionMilliseconds(timeRange, timestamp);
                    if (position < startPositionMilliseconds)
                        continue;
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        nextCursor = new JournalCursor(entry.Stamp.ObservationOrdinal);
                        return;
                    }

                    markers.Add(CreateMarker(in entry, timestamp, position));
                    if (markers.Count >= maxMarkers)
                    {
                        hasMore = true;
                        nextCursor = new JournalCursor(entry.Stamp.ObservationOrdinal + 1);
                        return;
                    }
                }
            });

            if (result.Count == 0)
                break;

            current = nextCursor ?? result.Cursor;
            if (hasMore || stopWindow)
                break;
        }

        return new ScenePlaybackTrackReadResult(markers.ToArray(), hasMore, current);
    }

    private static ScenePlaybackTrackMarker CreateMarker(in ObservedEventEnvelope entry, long timestamp, long position)
    {
        var track = ResolveTrack(entry.Domain);
        var skillCode = 0;
        var amount = 0L;
        long? currentValue = null;
        long? maximumValue = null;
        var resourceKind = 0;
        var resultCode = 0;
        if (entry.Combat is { } combat)
        {
            skillCode = combat.SkillCode;
            amount = combat.Damage;
        }
        else if (entry.Resource is { } resource)
        {
            currentValue = resource.CurrentValue;
            maximumValue = resource.MaximumValue;
            resourceKind = resource.ResourceKind;
            amount = resource.Delta ?? 0;
        }
        else if (entry.Aura is { } aura)
        {
            skillCode = aura.SkillCode;
            resultCode = aura.ResultCode;
        }

        return new ScenePlaybackTrackMarker(track, position, timestamp, entry.Stamp.ObservationOrdinal, entry.SourceEntityId, entry.TargetEntityId, skillCode, amount, currentValue, maximumValue, resourceKind, resultCode);
    }

    private static ScenePlaybackTrack ResolveTrack(ObservedEventDomain domain) => domain switch
    {
        ObservedEventDomain.Combat => ScenePlaybackTrack.Combat,
        ObservedEventDomain.Resource => ScenePlaybackTrack.Resource,
        ObservedEventDomain.Aura => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Scene => ScenePlaybackTrack.Scene,
        ObservedEventDomain.State => ScenePlaybackTrack.State,
        ObservedEventDomain.Diagnostic => ScenePlaybackTrack.Diagnostic,
        ObservedEventDomain.Action => ScenePlaybackTrack.Action,
        _ => ScenePlaybackTrack.Other
    };

    private static long ResolvePositionMilliseconds(ScenePlaybackTimeRange timeRange, long timestamp)
    {
        if (!timeRange.HasTimestamps || timestamp <= 0)
            return Math.Max(0, timestamp);

        return Math.Max(0, timestamp - timeRange.StartTimestampMilliseconds);
    }

    private static long ResolveTimestampMilliseconds(in ObservedEventEnvelope entry)
    {
        if (entry.Raw.TimestampMilliseconds > 0)
            return entry.Raw.TimestampMilliseconds;

        return entry.Stamp.OffsetTicks > 0 ? entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond : 0;
    }
}

public readonly record struct ScenePlaybackTrackMarker(ScenePlaybackTrack Track, long PositionMilliseconds, long TimestampMilliseconds, long ObservationOrdinal, int SourceEntityId, int TargetEntityId, int SkillCode, long Amount, long? CurrentValue, long? MaximumValue, int ResourceKind, int ResultCode);

public readonly record struct ScenePlaybackTrackReadResult(IReadOnlyList<ScenePlaybackTrackMarker> Markers, bool HasMore, JournalCursor NextCursor);
