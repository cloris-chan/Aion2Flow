using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackTrackReader
{
    private static readonly ScenePlaybackTrack[] Tracks = Enum.GetValues<ScenePlaybackTrack>();
    private static readonly int TrackBucketCount = ResolveTrackBucketCount();

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
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        nextCursor = new JournalCursor(entry.Stamp.ObservationOrdinal);
                        return;
                    }

                    var lifecycleEventKind = lifecycle.Apply(entry);
                    if (position < startPositionMilliseconds)
                        continue;

                    markers.Add(ScenePlaybackTrackProjection.CreateMarker(entry, offset, position, lifecycleEventKind));
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

        var buckets = new SampleBucket[checked(TrackBucketCount * maxMarkersPerTrack)];
        var trackCounts = new int[TrackBucketCount];
        var windowDuration = Math.Max(1, endPositionMilliseconds - startPositionMilliseconds);
        var cursor = segment.CreateCursor();
        var lifecycle = new ScenePlaybackLifecycleTrackState();
        var stopWindow = false;
        while (!stopWindow)
        {
            var result = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        return;
                    }

                    var lifecycleEventKind = lifecycle.Apply(entry);
                    if (position < startPositionMilliseconds)
                        continue;

                    var marker = ScenePlaybackTrackProjection.CreateMarker(entry, offset, position, lifecycleEventKind);
                    var trackIndex = ToTrackIndex(marker.Track);
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
        for (var trackOrdinal = 0; trackOrdinal < Tracks.Length; trackOrdinal++)
        {
            var track = Tracks[trackOrdinal];
            var trackIndex = ToTrackIndex(track);
            var count = trackCounts[trackIndex];
            if (count == 0)
                continue;

            counts.Add(new ScenePlaybackTrackCount(track, count));
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

    public static ScenePlaybackCombatantTrackSampledReadResult ReadCombatantSampled(SceneJournalSegment segment, long startPositionMilliseconds, long endPositionMilliseconds, int maxMarkersPerCombatantTrack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarkersPerCombatantTrack);
        if (segment.IsEmpty || endPositionMilliseconds < startPositionMilliseconds)
            return ScenePlaybackCombatantTrackSampledReadResult.Empty;

        var combatants = new Dictionary<int, CombatantSampleAccumulator>();
        var windowDuration = Math.Max(1, endPositionMilliseconds - startPositionMilliseconds);
        var cursor = segment.CreateCursor();
        var lifecycle = new ScenePlaybackLifecycleTrackState();
        var stopWindow = false;
        while (!stopWindow)
        {
            var result = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        return;
                    }

                    var lifecycleEventKind = lifecycle.Apply(entry);
                    if (position < startPositionMilliseconds)
                        continue;

                    var marker = ScenePlaybackTrackProjection.CreateMarker(entry, offset, position, lifecycleEventKind);
                    AddCombatantSample(combatants, marker.SourceEntityId, in marker, startPositionMilliseconds, windowDuration, maxMarkersPerCombatantTrack);
                    if (marker.TargetEntityId != marker.SourceEntityId)
                        AddCombatantSample(combatants, marker.TargetEntityId, in marker, startPositionMilliseconds, windowDuration, maxMarkersPerCombatantTrack);
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (combatants.Count == 0)
            return ScenePlaybackCombatantTrackSampledReadResult.Empty;

        var results = new List<ScenePlaybackCombatantTrackSamples>(combatants.Count);
        foreach (var (combatantId, accumulator) in combatants)
            results.Add(accumulator.CreateSamples(combatantId));
        return new ScenePlaybackCombatantTrackSampledReadResult(results.ToArray());
    }

    public static ScenePlaybackCombatSkillSampledReadResult ReadCombatSkillSampled(
        SceneJournalSegment segment,
        int combatantId,
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        int maxSkills,
        int maxMarkersPerSkill)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSkills);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarkersPerSkill);
        if (segment.IsEmpty || combatantId <= 0 || endPositionMilliseconds < startPositionMilliseconds)
            return ScenePlaybackCombatSkillSampledReadResult.Empty;

        var skills = new Dictionary<CombatEventKey, CombatSkillSampleAccumulator>();
        var windowDuration = Math.Max(1, endPositionMilliseconds - startPositionMilliseconds);
        var cursor = segment.CreateCursor();
        var stopWindow = false;
        while (!stopWindow)
        {
            var result = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry);
                    var position = Math.Max(0, offset);
                    if (position > endPositionMilliseconds)
                    {
                        stopWindow = true;
                        return;
                    }

                    if (position < startPositionMilliseconds || entry.Domain != ObservedEventDomain.Combat)
                        continue;

                    ref readonly var combat = ref entry.Combat;
                    var sourceEntityId = entry.SourceEntityId;
                    var targetEntityId = entry.TargetEntityId;
                    if (sourceEntityId != combatantId && targetEntityId != combatantId)
                        continue;

                    var eventKey = CombatEventKey.FromObservation(in combat);
                    if (!eventKey.HasSkillCode && eventKey.BodyResourceEffectRef.IsEmpty && eventKey.DetailResourceEffectRef.IsEmpty)
                        continue;

                    if (!skills.TryGetValue(eventKey, out var accumulator))
                    {
                        accumulator = new CombatSkillSampleAccumulator(maxMarkersPerSkill);
                        skills.Add(eventKey, accumulator);
                    }

                    var marker = new ScenePlaybackCombatSkillMarker(
                        position,
                        entry.Stamp.ObservationOrdinal,
                        sourceEntityId,
                        targetEntityId,
                        eventKey,
                        combat.Damage);
                    var bucketIndex = Math.Clamp((int)((position - startPositionMilliseconds) / (double)windowDuration * maxMarkersPerSkill), 0, maxMarkersPerSkill - 1);
                    accumulator.Add(in marker, bucketIndex);
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (skills.Count == 0)
            return ScenePlaybackCombatSkillSampledReadResult.Empty;

        var results = new List<ScenePlaybackCombatSkillSamples>(skills.Count);
        foreach (var (eventKey, accumulator) in skills)
            results.Add(accumulator.CreateSamples(eventKey));

        results.Sort(static (left, right) =>
        {
            var comparison = AbsoluteMagnitude(right.Amount).CompareTo(AbsoluteMagnitude(left.Amount));
            if (comparison != 0)
                return comparison;

            comparison = right.Count.CompareTo(left.Count);
            return comparison != 0 ? comparison : left.EventKey.CompareTo(right.EventKey);
        });
        if (results.Count > maxSkills)
            results.RemoveRange(maxSkills, results.Count - maxSkills);

        return new ScenePlaybackCombatSkillSampledReadResult(results.ToArray());
    }

    private static void AddCombatantSample(Dictionary<int, CombatantSampleAccumulator> combatants, int combatantId, in ScenePlaybackTrackMarker marker, long startPositionMilliseconds, long windowDuration, int maxMarkersPerCombatantTrack)
    {
        if (combatantId <= 0)
            return;

        if (!combatants.TryGetValue(combatantId, out var accumulator))
        {
            accumulator = new CombatantSampleAccumulator(maxMarkersPerCombatantTrack);
            combatants.Add(combatantId, accumulator);
        }

        var bucketIndex = Math.Clamp((int)((marker.PositionMilliseconds - startPositionMilliseconds) / (double)windowDuration * maxMarkersPerCombatantTrack), 0, maxMarkersPerCombatantTrack - 1);
        accumulator.Add(in marker, bucketIndex);
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
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.Stamp.ObservationOrdinal >= end)
                        return;
                    result.Apply(entry);
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

    private struct CombatSkillSampleBucket
    {
        private ScenePlaybackCombatSkillMarker _marker;
        private double _averagePosition;

        public int Count { get; private set; }

        public void Add(in ScenePlaybackCombatSkillMarker marker)
        {
            Count++;
            _averagePosition += (marker.PositionMilliseconds - _averagePosition) / Count;
            if (Count == 1 || AbsoluteMagnitude(marker.Amount) > AbsoluteMagnitude(_marker.Amount))
                _marker = marker;
        }

        public readonly ScenePlaybackCombatSkillSample CreateSample()
            => new(_marker with { PositionMilliseconds = (long)Math.Round(_averagePosition, MidpointRounding.AwayFromZero) }, Count);
    }

    private sealed class CombatantSampleAccumulator(int maxMarkersPerCombatantTrack)
    {
        private readonly SampleBucket[] _buckets = new SampleBucket[checked(TrackBucketCount * maxMarkersPerCombatantTrack)];
        private readonly int[] _trackCounts = new int[TrackBucketCount];
        private readonly int _maxMarkersPerTrack = maxMarkersPerCombatantTrack;

        public void Add(in ScenePlaybackTrackMarker marker, int bucketIndex)
        {
            var trackIndex = ToTrackIndex(marker.Track);
            _trackCounts[trackIndex]++;
            _buckets[trackIndex * _maxMarkersPerTrack + bucketIndex].Add(in marker);
        }

        public ScenePlaybackCombatantTrackSamples CreateSamples(int combatantId)
        {
            var samples = new List<ScenePlaybackTrackSample>(Math.Min(_buckets.Length, 64));
            var counts = new List<ScenePlaybackTrackCount>(Tracks.Length);
            for (var trackOrdinal = 0; trackOrdinal < Tracks.Length; trackOrdinal++)
            {
                var track = Tracks[trackOrdinal];
                var trackIndex = ToTrackIndex(track);
                var count = _trackCounts[trackIndex];
                if (count == 0)
                    continue;

                counts.Add(new ScenePlaybackTrackCount(track, count));
                var offset = trackIndex * _maxMarkersPerTrack;
                for (var bucketIndex = 0; bucketIndex < _maxMarkersPerTrack; bucketIndex++)
                {
                    ref readonly var bucket = ref _buckets[offset + bucketIndex];
                    if (bucket.Count > 0)
                        samples.Add(bucket.CreateSample());
                }
            }

            return new ScenePlaybackCombatantTrackSamples(combatantId, samples.ToArray(), counts.ToArray());
        }
    }

    private sealed class CombatSkillSampleAccumulator(int maxMarkersPerSkill)
    {
        private readonly CombatSkillSampleBucket[] _buckets = new CombatSkillSampleBucket[maxMarkersPerSkill];

        public int Count { get; private set; }

        public long Amount { get; private set; }

        public void Add(in ScenePlaybackCombatSkillMarker marker, int bucketIndex)
        {
            Count++;
            Amount += marker.Amount;
            _buckets[bucketIndex].Add(in marker);
        }

        public ScenePlaybackCombatSkillSamples CreateSamples(CombatEventKey eventKey)
        {
            var samples = new List<ScenePlaybackCombatSkillSample>(Math.Min(_buckets.Length, maxMarkersPerSkill));
            for (var bucketIndex = 0; bucketIndex < _buckets.Length; bucketIndex++)
            {
                ref readonly var bucket = ref _buckets[bucketIndex];
                if (bucket.Count > 0)
                    samples.Add(bucket.CreateSample());
            }

            return new ScenePlaybackCombatSkillSamples(eventKey, samples.ToArray(), Count, Amount);
        }
    }

    private static int ToTrackIndex(ScenePlaybackTrack track)
    {
        var index = (int)track;
        if ((uint)index >= (uint)TrackBucketCount)
            throw new InvalidOperationException($"Unknown playback track: {track}.");

        return index;
    }

    private static int ResolveTrackBucketCount()
    {
        var max = -1;
        for (var i = 0; i < Tracks.Length; i++)
        {
            var index = (int)Tracks[i];
            if (index < 0)
                throw new InvalidOperationException($"Playback track cannot be negative: {Tracks[i]}.");

            max = Math.Max(max, index);
        }

        return checked(max + 1);
    }

    private static ulong AbsoluteMagnitude(long value)
        => value >= 0 ? (ulong)value : (ulong)(-(value + 1)) + 1;
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

public readonly record struct ScenePlaybackCombatantTrackSamples(int CombatantId, IReadOnlyList<ScenePlaybackTrackSample> Samples, IReadOnlyList<ScenePlaybackTrackCount> TrackCounts);

public readonly record struct ScenePlaybackCombatantTrackSampledReadResult(IReadOnlyList<ScenePlaybackCombatantTrackSamples> Combatants)
{
    public static ScenePlaybackCombatantTrackSampledReadResult Empty { get; } = new([]);
}

public readonly record struct ScenePlaybackCombatSkillMarker(
    long PositionMilliseconds,
    long ObservationOrdinal,
    int SourceEntityId,
    int TargetEntityId,
    CombatEventKey EventKey,
    long Amount);

public readonly record struct ScenePlaybackCombatSkillSample(ScenePlaybackCombatSkillMarker Marker, int EventCount);

public readonly record struct ScenePlaybackCombatSkillSamples(CombatEventKey EventKey, IReadOnlyList<ScenePlaybackCombatSkillSample> Samples, int Count, long Amount)
{
    public int SkillCode => EventKey.SkillCode;
}

public readonly record struct ScenePlaybackCombatSkillSampledReadResult(IReadOnlyList<ScenePlaybackCombatSkillSamples> Skills)
{
    public static ScenePlaybackCombatSkillSampledReadResult Empty { get; } = new([]);
}
