using Cloris.Aion2Flow.SceneRuntime.Journal;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackTrackReader
{
    private static readonly ScenePlaybackTrack[] Tracks = Enum.GetValues<ScenePlaybackTrack>();

    public static ScenePlaybackTrackReadResult Read(SceneJournalSegment segment, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkers)
        => Read(segment, startPositionMilliseconds, endPositionMilliseconds, maxMarkers, segment.CreateCursor());

    public static ScenePlaybackTrackReadResult Read(SceneJournalSegment segment, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkers, JournalCursor cursor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxMarkers);
        var markers = new List<ScenePlaybackTrackMarker>(Math.Min(maxMarkers, 256));
        var current = cursor;
        var lifecycle = CreateLifecycleState(segment, cursor);
        var hasMore = false;
        var stopWindow = false;
        while (markers.Count < maxMarkers)
        {
            JournalCursor? nextCursor = null;
            var result = segment.ReadEntries(current, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(in entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        nextCursor = new JournalCursor(entry.Stamp.ObservationOrdinal);
                        return;
                    }

                    var isAuraRenewal = lifecycle.Apply(in entry);
                    if (position < startPositionMilliseconds)
                        continue;

                    markers.Add(ScenePlaybackTrackProjection.CreateMarker(in entry, offset, position, isAuraRenewal));
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

    public static ScenePlaybackTrackSampledReadResult ReadSampled(SceneJournalSegment segment, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkersPerTrack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarkersPerTrack);
        if (segment.IsEmpty || endPositionMilliseconds < startPositionMilliseconds)
            return ScenePlaybackTrackSampledReadResult.Empty;

        var buckets = new SampleBucket[checked(Tracks.Length * maxMarkersPerTrack)];
        var trackCounts = new int[Tracks.Length];
        var windowDuration = Math.Max(1, endPositionMilliseconds - startPositionMilliseconds);
        var cursor = segment.CreateCursor();
        var lifecycle = new ScenePlaybackLifecycleTrackState();
        var stopWindow = false;
        while (!stopWindow)
        {
            var result = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(in entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        return;
                    }

                    var isAuraRenewal = lifecycle.Apply(in entry);
                    if (position < startPositionMilliseconds)
                        continue;

                    var marker = ScenePlaybackTrackProjection.CreateMarker(in entry, offset, position, isAuraRenewal);
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

    private static ScenePlaybackLifecycleTrackState CreateLifecycleState(SceneJournalSegment segment, JournalCursor cursor)
    {
        var result = new ScenePlaybackLifecycleTrackState();
        var current = segment.CreateCursor();
        var end = Math.Clamp(cursor.NextObservationOrdinal, segment.StartObservationOrdinal, segment.CurrentEndObservationOrdinalExclusive);
        while (current.NextObservationOrdinal < end)
        {
            var read = segment.ReadEntries(current, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    if (entry.Stamp.ObservationOrdinal >= end)
                        return;
                    result.Apply(in entry);
                }
            });
            if (read.Count == 0)
                break;
            current = read.Cursor;
        }

        return result;
    }

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

public enum ScenePlaybackLifecycleEventKind : byte
{
    None,
    Open,
    Renew,
    Result
}

public readonly record struct ScenePlaybackTrackMarker(
    ScenePlaybackTrack Track,
    long PositionMilliseconds,
    long OffsetMilliseconds,
    long ObservationOrdinal,
    int SourceEntityId,
    int TargetEntityId,
    int SkillCode,
    long Amount,
    long? CurrentValue,
    long? MaximumValue,
    int ResourceKind,
    int ResultCode,
    ScenePlaybackLifecycleEventKind LifecycleEventKind,
    int InstanceSequenceId,
    int DurationMilliseconds,
    uint DisplayResourceEffectRefRaw);

public readonly record struct ScenePlaybackTrackReadResult(IReadOnlyList<ScenePlaybackTrackMarker> Markers, bool HasMore, JournalCursor NextCursor);

public readonly record struct ScenePlaybackTrackSample(ScenePlaybackTrackMarker Marker, int EventCount);

public readonly record struct ScenePlaybackTrackCount(ScenePlaybackTrack Track, int Count);

public readonly record struct ScenePlaybackTrackSampledReadResult(IReadOnlyList<ScenePlaybackTrackSample> Samples, IReadOnlyList<ScenePlaybackTrackCount> TrackCounts)
{
    public static ScenePlaybackTrackSampledReadResult Empty { get; } = new([], []);
}
