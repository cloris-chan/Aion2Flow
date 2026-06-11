using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackTrackReader
{
    private static readonly ScenePlaybackTrack[] Tracks = Enum.GetValues<ScenePlaybackTrack>();

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
            var result = segment.ReadEntries(current, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var timestamp = ScenePlaybackTimeline.ResolveTimestampMilliseconds(in entry);
                    var position = ScenePlaybackTimeline.ResolvePositionMilliseconds(timeRange, timestamp);
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

    public static ScenePlaybackTrackSampledReadResult ReadSampled(SceneJournalSegment segment, ScenePlaybackTimeRange timeRange, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkersPerTrack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarkersPerTrack);
        if (segment.IsEmpty || endPositionMilliseconds < startPositionMilliseconds)
            return ScenePlaybackTrackSampledReadResult.Empty;

        var buckets = new SampleBucket[checked(Tracks.Length * maxMarkersPerTrack)];
        var trackCounts = new int[Tracks.Length];
        var windowDuration = Math.Max(1, endPositionMilliseconds - startPositionMilliseconds);
        var cursor = segment.CreateCursor();
        var stopWindow = false;
        while (!stopWindow)
        {
            var result = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var timestamp = ScenePlaybackTimeline.ResolveTimestampMilliseconds(in entry);
                    var position = ScenePlaybackTimeline.ResolvePositionMilliseconds(timeRange, timestamp);
                    if (position < startPositionMilliseconds)
                        continue;
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        return;
                    }

                    var marker = CreateMarker(in entry, timestamp, position);
                    var trackIndex = (int)marker.Track;
                    trackCounts[trackIndex]++;
                    var ratio = (position - startPositionMilliseconds) / (double)windowDuration;
                    var bucketIndex = Math.Clamp((int)(ratio * maxMarkersPerTrack), 0, maxMarkersPerTrack - 1);
                    buckets[trackIndex * maxMarkersPerTrack + bucketIndex].Add(in marker);
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        var samples = new List<ScenePlaybackTrackSample>(Math.Min(buckets.Length, 256));
        var counts = new List<ScenePlaybackTrackCount>(Tracks.Length);
        for (var trackIndex = 0; trackIndex < Tracks.Length; trackIndex++)
        {
            var count = trackCounts[trackIndex];
            if (count == 0)
                continue;

            counts.Add(new ScenePlaybackTrackCount(Tracks[trackIndex], count));
            var offset = trackIndex * maxMarkersPerTrack;
            for (var bucketIndex = 0; bucketIndex < maxMarkersPerTrack; bucketIndex++)
            {
                ref readonly var bucket = ref buckets[offset + bucketIndex];
                if (bucket.Count > 0)
                    samples.Add(bucket.CreateSample());
            }
        }

        return new ScenePlaybackTrackSampledReadResult(samples.ToArray(), counts.ToArray());
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

    private struct SampleBucket
    {
        private ScenePlaybackTrackMarker _marker;
        private double _averagePosition;

        public int Count { get; private set; }

        public void Add(in ScenePlaybackTrackMarker marker)
        {
            Count++;
            _averagePosition += (marker.PositionMilliseconds - _averagePosition) / Count;
            if (Count == 1 || AbsoluteMagnitude(marker.Amount) > AbsoluteMagnitude(_marker.Amount))
                _marker = marker;
        }

        public readonly ScenePlaybackTrackSample CreateSample()
            => new(_marker with { PositionMilliseconds = (long)Math.Round(_averagePosition, MidpointRounding.AwayFromZero) }, Count);

        private static ulong AbsoluteMagnitude(long value)
            => value >= 0 ? (ulong)value : (ulong)(-(value + 1)) + 1;
    }
}

public readonly record struct ScenePlaybackTrackMarker(ScenePlaybackTrack Track, long PositionMilliseconds, long TimestampMilliseconds, long ObservationOrdinal, int SourceEntityId, int TargetEntityId, int SkillCode, long Amount, long? CurrentValue, long? MaximumValue, int ResourceKind, int ResultCode);

public readonly record struct ScenePlaybackTrackReadResult(IReadOnlyList<ScenePlaybackTrackMarker> Markers, bool HasMore, JournalCursor NextCursor);

public readonly record struct ScenePlaybackTrackSample(ScenePlaybackTrackMarker Marker, int EventCount);

public readonly record struct ScenePlaybackTrackCount(ScenePlaybackTrack Track, int Count);

public readonly record struct ScenePlaybackTrackSampledReadResult(IReadOnlyList<ScenePlaybackTrackSample> Samples, IReadOnlyList<ScenePlaybackTrackCount> TrackCounts)
{
    public static ScenePlaybackTrackSampledReadResult Empty { get; } = new([], []);
}
