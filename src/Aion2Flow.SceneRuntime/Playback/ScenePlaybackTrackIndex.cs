using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackTrackIndex
{
    private readonly ScenePlaybackTrackMarker[] _markers;
    private readonly int[] _nonCombatEventIndexes;
    private readonly Dictionary<ScenePlaybackEventScope, int[]> _eventPostings;

    private ScenePlaybackTrackIndex(
        long startObservationOrdinal,
        ScenePlaybackTrackMarker[] markers,
        int[] nonCombatEventIndexes,
        Dictionary<ScenePlaybackEventScope, int[]> eventPostings)
    {
        StartObservationOrdinal = startObservationOrdinal;
        _markers = markers;
        _nonCombatEventIndexes = nonCombatEventIndexes;
        _eventPostings = eventPostings;
    }

    public long StartObservationOrdinal { get; }

    public int Count => _markers.Length;

    public long EndObservationOrdinalExclusive => checked(StartObservationOrdinal + _markers.Length);

    public static ScenePlaybackTrackIndex Build(SceneJournalSegment segment, CancellationToken cancellationToken = default)
    {
        if (segment.IsEmpty)
            return new ScenePlaybackTrackIndex(segment.StartObservationOrdinal, [], [], []);

        var startObservationOrdinal = segment.StartObservationOrdinal;
        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var markerCount = checked((int)(endObservationOrdinalExclusive - startObservationOrdinal));
        if (markerCount == 0)
            return new ScenePlaybackTrackIndex(startObservationOrdinal, [], [], []);

        var fixedSegment = new SceneJournalSegment(segment.Journal, startObservationOrdinal, endObservationOrdinalExclusive, IsLiveGrowing: false);
        var markers = new ScenePlaybackTrackMarker[markerCount];
        var lifecycle = new ScenePlaybackLifecycleTrackState();
        Dictionary<int, long>? resourceMaximums = null;
        var cursor = fixedSegment.CreateCursor();
        var markerIndex = 0;
        var previousPosition = 0L;
        while (markerIndex < markers.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = fixedSegment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = Math.Max(0, ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry));
                    if (markerIndex > 0 && position < previousPosition)
                        throw new InvalidDataException($"Playback timeline position moved backwards at observation {entry.Stamp.ObservationOrdinal}.");

                    var lifecycleProjection = lifecycle.Apply(entry);
                    var marker = ScenePlaybackTrackProjection.CreateMarker(entry, position, position, lifecycleProjection);
                    if (entry.Domain == ObservedEventDomain.Resource)
                    {
                        ref readonly var resource = ref entry.Resource;
                        resourceMaximums ??= [];
                        if (resource.MaximumValue is > 0)
                            resourceMaximums[resource.EntityId] = resource.MaximumValue.Value;

                        if (resourceMaximums.TryGetValue(resource.EntityId, out var knownMaximum))
                        {
                            marker = marker with
                            {
                                MaximumValue = resource.MaximumValue.HasValue
                                    ? Math.Max(resource.MaximumValue.Value, knownMaximum)
                                    : knownMaximum,
                            };
                        }
                    }

                    markers[markerIndex++] = marker;
                    previousPosition = position;
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (markerIndex != markers.Length)
            throw new InvalidDataException($"Playback marker index expected {markers.Length} observations but read {markerIndex}.");

        BackfillAuraIdentities(markers);
        var nonCombatEventIndexes = BuildEventPostings(markers, out var eventPostings);
        return new ScenePlaybackTrackIndex(startObservationOrdinal, markers, nonCombatEventIndexes, eventPostings);
    }

    public ScenePlaybackTrackMarkerWindow ReadWindow(
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        long endObservationOrdinalExclusive,
        int maxMarkers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPositionMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPositionMilliseconds, startPositionMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMarkers);
        if (maxMarkers == 0 || _markers.Length == 0 || endObservationOrdinalExclusive <= StartObservationOrdinal)
            return default;

        var appliedCount = checked((int)Math.Min(_markers.Length, endObservationOrdinalExclusive - StartObservationOrdinal));
        var first = LowerBound(startPositionMilliseconds, appliedCount);
        var end = UpperBound(endPositionMilliseconds, first, appliedCount);
        first = Math.Max(first, end - maxMarkers);
        return new ScenePlaybackTrackMarkerWindow(_markers, first, end - first);
    }

    public ScenePlaybackEventReadResult CopyLatestNonCombatEvents(
        ScenePlaybackEventScope scope,
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        long endObservationOrdinalExclusive,
        Span<ScenePlaybackEventMarker> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPositionMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPositionMilliseconds, startPositionMilliseconds);
        if (destination.IsEmpty || _markers.Length == 0 || endObservationOrdinalExclusive <= StartObservationOrdinal)
            return new ScenePlaybackEventReadResult(0, endObservationOrdinalExclusive);

        var posting = scope == ScenePlaybackEventScope.All
            ? _nonCombatEventIndexes
            : _eventPostings.GetValueOrDefault(scope);
        if (posting is null || posting.Length == 0)
            return new ScenePlaybackEventReadResult(0, endObservationOrdinalExclusive);

        var appliedMarkerCount = checked((int)Math.Min(_markers.Length, endObservationOrdinalExclusive - StartObservationOrdinal));
        var appliedPostingCount = LowerBoundMarkerIndex(posting, appliedMarkerCount);
        var first = LowerBoundPosition(posting, startPositionMilliseconds, appliedPostingCount);
        var end = UpperBoundPosition(posting, endPositionMilliseconds, first, appliedPostingCount);
        first = Math.Max(first, end - destination.Length);
        var count = end - first;
        for (var i = 0; i < count; i++)
        {
            ref readonly var marker = ref _markers[posting[first + i]];
            destination[i] = new ScenePlaybackEventMarker(
                new ScenePlaybackEventId(ScenePlaybackEventFactKind.Observation, marker.ObservationOrdinal),
                marker,
                default);
        }

        return new ScenePlaybackEventReadResult(count, endObservationOrdinalExclusive);
    }

    private static void BackfillAuraIdentities(ScenePlaybackTrackMarker[] markers)
    {
        Dictionary<ScenePlaybackAuraInstanceKey, List<int>>? unresolved = null;
        for (var markerIndex = 0; markerIndex < markers.Length; markerIndex++)
        {
            ref readonly var marker = ref markers[markerIndex];
            if (marker.Track != ScenePlaybackTrack.Aura || marker.TargetEntityId <= 0 || marker.InstanceSequenceId <= 0)
                continue;

            var key = new ScenePlaybackAuraInstanceKey(marker.TargetEntityId, marker.InstanceSequenceId);
            if (marker.LifecycleEventKind == ScenePlaybackLifecycleEventKind.Open)
                unresolved?.Remove(key);

            if (marker.DisplayResourceEffectRef.IsEmpty)
            {
                if (marker.LifecycleEventKind != ScenePlaybackLifecycleEventKind.Result)
                {
                    unresolved ??= [];
                    if (!unresolved.TryGetValue(key, out var markerIndexes))
                    {
                        markerIndexes = [];
                        unresolved.Add(key, markerIndexes);
                    }

                    markerIndexes.Add(markerIndex);
                }
            }
            else if (unresolved?.Remove(key, out var markerIndexes) == true)
            {
                for (var unresolvedIndex = 0; unresolvedIndex < markerIndexes.Count; unresolvedIndex++)
                {
                    var index = markerIndexes[unresolvedIndex];
                    markers[index] = markers[index] with
                    {
                        DisplayResourceEffectRef = marker.DisplayResourceEffectRef
                    };
                }
            }

            if (marker.LifecycleEventKind == ScenePlaybackLifecycleEventKind.Result)
                unresolved?.Remove(key);
        }
    }

    private static int[] BuildEventPostings(
        ScenePlaybackTrackMarker[] markers,
        out Dictionary<ScenePlaybackEventScope, int[]> postings)
    {
        var all = new List<int>(markers.Length);
        var builders = new Dictionary<ScenePlaybackEventScope, List<int>>();
        for (var markerIndex = 0; markerIndex < markers.Length; markerIndex++)
        {
            ref readonly var marker = ref markers[markerIndex];
            if (marker.Track == ScenePlaybackTrack.Combat)
                continue;

            all.Add(markerIndex);
            if (marker.SourceEntityId > 0)
                AddPosting(builders, ScenePlaybackEventScope.ForCombatant(marker.SourceEntityId), markerIndex);
            if (marker.TargetEntityId > 0 && marker.TargetEntityId != marker.SourceEntityId)
                AddPosting(builders, ScenePlaybackEventScope.ForCombatant(marker.TargetEntityId), markerIndex);

            if (marker.Track != ScenePlaybackTrack.Aura || marker.TargetEntityId <= 0)
                continue;

            AddPosting(
                builders,
                ScenePlaybackEventScope.ForRelation(marker.TargetEntityId, ScenePlaybackEventRelation.Aura),
                markerIndex);
            if (!marker.AuraIdentity.IsEmpty)
                AddPosting(builders, ScenePlaybackEventScope.ForAura(marker.TargetEntityId, marker.AuraIdentity), markerIndex);
        }

        postings = new Dictionary<ScenePlaybackEventScope, int[]>(builders.Count);
        foreach (var (scope, builder) in builders)
            postings.Add(scope, builder.ToArray());
        return all.ToArray();
    }

    private static void AddPosting(
        Dictionary<ScenePlaybackEventScope, List<int>> builders,
        ScenePlaybackEventScope scope,
        int markerIndex)
    {
        if (!builders.TryGetValue(scope, out var builder))
        {
            builder = [];
            builders.Add(scope, builder);
        }

        builder.Add(markerIndex);
    }

    private static int LowerBoundMarkerIndex(int[] posting, int markerIndex)
    {
        var low = 0;
        var high = posting.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (posting[middle] < markerIndex)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int LowerBoundPosition(int[] posting, long positionMilliseconds, int count)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_markers[posting[middle]].PositionMilliseconds < positionMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int UpperBoundPosition(int[] posting, long positionMilliseconds, int start, int count)
    {
        var low = start;
        var high = count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_markers[posting[middle]].PositionMilliseconds <= positionMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int LowerBound(long positionMilliseconds, int count)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_markers[middle].PositionMilliseconds < positionMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int UpperBound(long positionMilliseconds, int start, int count)
    {
        var low = start;
        var high = count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_markers[middle].PositionMilliseconds <= positionMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}

public readonly struct ScenePlaybackTrackMarkerWindow
{
    private readonly ScenePlaybackTrackMarker[]? _markers;
    private readonly int _start;

    internal ScenePlaybackTrackMarkerWindow(ScenePlaybackTrackMarker[] markers, int start, int count)
    {
        _markers = markers;
        _start = start;
        Count = count;
    }

    public int Count { get; }

    public ReadOnlySpan<ScenePlaybackTrackMarker> AsSpan()
        => _markers is null ? [] : _markers.AsSpan(_start, Count);
}
