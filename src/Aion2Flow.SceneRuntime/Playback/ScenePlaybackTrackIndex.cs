using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackTrackIndex
{
    private readonly ScenePlaybackTrackMarker[] _markers;
    private readonly int[] _nonCombatEventIndexes;
    private readonly Dictionary<ScenePlaybackEventScope, int[]> _eventPostings;

    private ScenePlaybackTrackIndex(
        long startObservationOrdinal,
        long endObservationOrdinalExclusive,
        ScenePlaybackTrackMarker[] markers,
        int[] nonCombatEventIndexes,
        Dictionary<ScenePlaybackEventScope, int[]> eventPostings)
    {
        StartObservationOrdinal = startObservationOrdinal;
        EndObservationOrdinalExclusive = endObservationOrdinalExclusive;
        _markers = markers;
        _nonCombatEventIndexes = nonCombatEventIndexes;
        _eventPostings = eventPostings;
    }

    public long StartObservationOrdinal { get; }

    public int Count => _markers.Length;

    public long EndObservationOrdinalExclusive { get; }

    public static ScenePlaybackTrackIndex Build(SceneJournalSegment segment, CancellationToken cancellationToken = default)
    {
        var startObservationOrdinal = segment.StartObservationOrdinal;
        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var observationCount = checked((int)(endObservationOrdinalExclusive - startObservationOrdinal));
        if (observationCount == 0)
            return new ScenePlaybackTrackIndex(startObservationOrdinal, endObservationOrdinalExclusive, [], [], []);

        var fixedSegment = new SceneJournalSegment(segment.Journal, startObservationOrdinal, endObservationOrdinalExclusive, IsLiveGrowing: false);
        var entities = new EntityStore();
        var boundary = new SceneBoundaryStore();
        var metadata = new RuntimeMetadataRegistry();
        var combat = new CombatStore(observationCount);
        var applier = new DomainEventApplier(entities, boundary, metadata, combat);
        var indexedMarkers = new List<IndexedTrackMarker>(observationCount);
        var cursor = fixedSegment.CreateCursor();
        var observationIndex = 0;
        var sequence = 0;
        var previousPosition = 0L;
        var currentFlushId = -1L;
        var completedFlushId = -1L;
        while (observationIndex < observationCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = fixedSegment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = Math.Max(0, ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry));
                    if (observationIndex > 0 && position < previousPosition)
                        throw new InvalidDataException($"Playback timeline position moved backwards at observation {entry.Stamp.ObservationOrdinal}.");

                    var flushId = entry.Stamp.FlushId;
                    if (currentFlushId > 0 && flushId != currentFlushId && currentFlushId > completedFlushId)
                    {
                        applier.CompleteFlush();
                        completedFlushId = currentFlushId;
                    }

                    currentFlushId = flushId;
                    var materialization = applier.ApplyEntry(entry);
                    if (entry.Domain == ObservedEventDomain.Combat)
                    {
                        observationIndex++;
                        previousPosition = position;
                        continue;
                    }

                    var auraLifecycle = materialization.AuraLifecycle;
                    var marker = ScenePlaybackTrackProjection.CreateObservationMarker(entry, position, position, in auraLifecycle);
                    if (entry.Domain == ObservedEventDomain.EntityVital)
                    {
                        ref readonly var vital = ref entry.EntityVital;
                        if (applier.EntityVitals.TryGet(vital.EntityId, out var state))
                            marker = marker with { MaxHp = state.MaxHp };
                    }

                    indexedMarkers.Add(new IndexedTrackMarker(marker, IsObservation: true, sequence++));
                    observationIndex++;
                    previousPosition = position;
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (observationIndex != observationCount)
            throw new InvalidDataException($"Playback marker index expected {observationCount} observations but read {observationIndex}.");

        var canCompleteFinalFlush = !segment.IsLiveGrowing ||
            (fixedSegment.Journal?.LastCompletedFlushId ?? -1) >= currentFlushId;
        if (currentFlushId > 0 && currentFlushId > completedFlushId && canCompleteFinalFlush)
            applier.CompleteFlush();

        var adapter = new SceneCombatSnapshotAdapter(
            entities,
            applier.EntityVitals,
            combat,
            applier.Mechanics,
            applier.Resources,
            boundary,
            applier.BossFocus);
        _ = adapter.PrepareCurrentFrameEventProjection();
        AppendMaterializedMarkers(indexedMarkers, combat, applier.Mechanics, applier.Resources, adapter, ref sequence, cancellationToken);
        indexedMarkers.Sort(static (left, right) => CompareIndexedMarkers(in left, in right));

        var markers = new ScenePlaybackTrackMarker[indexedMarkers.Count];
        var observationMarkers = new bool[indexedMarkers.Count];
        previousPosition = 0;
        for (var markerIndex = 0; markerIndex < indexedMarkers.Count; markerIndex++)
        {
            var indexedMarker = indexedMarkers[markerIndex];
            var marker = indexedMarker.Marker;
            if (marker.ObservationOrdinal < startObservationOrdinal || marker.ObservationOrdinal >= endObservationOrdinalExclusive)
                throw new InvalidDataException($"Playback materialized marker has invalid source observation {marker.ObservationOrdinal}.");
            if (markerIndex > 0 && marker.PositionMilliseconds < previousPosition)
                throw new InvalidDataException($"Playback materialized timeline position moved backwards at observation {marker.ObservationOrdinal}.");

            markers[markerIndex] = marker;
            observationMarkers[markerIndex] = indexedMarker.IsObservation;
            previousPosition = marker.PositionMilliseconds;
        }

        BackfillAuraIdentities(markers);
        var nonCombatEventIndexes = BuildEventPostings(markers, observationMarkers, out var eventPostings);
        return new ScenePlaybackTrackIndex(startObservationOrdinal, endObservationOrdinalExclusive, markers, nonCombatEventIndexes, eventPostings);
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

        var appliedCount = LowerBoundObservationOrdinal(Math.Min(endObservationOrdinalExclusive, EndObservationOrdinalExclusive));
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

        var appliedMarkerCount = LowerBoundObservationOrdinal(Math.Min(endObservationOrdinalExclusive, EndObservationOrdinalExclusive));
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
                default,
                null,
                null,
                null);
        }

        return new ScenePlaybackEventReadResult(count, endObservationOrdinalExclusive);
    }

    private static void BackfillAuraIdentities(ScenePlaybackTrackMarker[] markers)
    {
        Dictionary<AuraInstanceKey, List<int>>? unresolved = null;
        for (var markerIndex = 0; markerIndex < markers.Length; markerIndex++)
        {
            ref readonly var marker = ref markers[markerIndex];
            if (marker.Track != ScenePlaybackTrack.Aura || marker.TargetEntityId <= 0 || marker.InstanceSequenceId <= 0)
                continue;

            var key = new AuraInstanceKey(marker.TargetEntityId, marker.InstanceSequenceId);
            if (marker.LifecycleEventKind == AuraLifecycleEventKind.Open)
                unresolved?.Remove(key);

            if (marker.DisplayResourceEffectRef.IsEmpty)
            {
                if (marker.LifecycleEventKind != AuraLifecycleEventKind.Result)
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
                        DisplayResourceEffectRef = marker.DisplayResourceEffectRef,
                        AuraSemantics = marker.AuraSemantics
                    };
                }
            }

            if (marker.LifecycleEventKind == AuraLifecycleEventKind.Result)
                unresolved?.Remove(key);
        }
    }

    private static void AppendMaterializedMarkers(
        List<IndexedTrackMarker> markers,
        CombatStore combat,
        MechanicStore mechanics,
        ResourceStore resources,
        SceneCombatSnapshotAdapter adapter,
        ref int sequence,
        CancellationToken cancellationToken)
    {
        var metricEvents = combat.EventSpan;
        for (var eventIndex = 0; eventIndex < metricEvents.Length; eventIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ref readonly var record = ref metricEvents[eventIndex];
            if (!adapter.TryResolveCurrentFrameEventSourcePrepared(in record, out var sourceEntityId))
                continue;

            markers.Add(new IndexedTrackMarker(
                ScenePlaybackTrackProjection.CreateMetricMarker(in record, sourceEntityId),
                IsObservation: false,
                sequence++));
        }

        var mechanicEvents = mechanics.Events;
        for (var eventIndex = 0; eventIndex < mechanicEvents.Count; eventIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = mechanicEvents[eventIndex];
            if (!adapter.TryResolveCurrentFrameEventSourcePrepared(in record, out var sourceEntityId))
                continue;

            markers.Add(new IndexedTrackMarker(
                ScenePlaybackTrackProjection.CreateMechanicMarker(in record, sourceEntityId),
                IsObservation: false,
                sequence++));
        }

        var resourceEvents = resources.Events;
        for (var eventIndex = 0; eventIndex < resourceEvents.Count; eventIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = resourceEvents[eventIndex];
            markers.Add(new IndexedTrackMarker(
                ScenePlaybackTrackProjection.CreateResourceMarker(
                    in record,
                    adapter.ResolveCurrentFrameEventSourcePrepared(in record)),
                IsObservation: false,
                sequence++));
        }
    }

    private static int CompareIndexedMarkers(in IndexedTrackMarker left, in IndexedTrackMarker right)
    {
        var comparison = left.Marker.ObservationOrdinal.CompareTo(right.Marker.ObservationOrdinal);
        if (comparison != 0)
            return comparison;

        comparison = left.Marker.Track.CompareTo(right.Marker.Track);
        return comparison != 0 ? comparison : left.Sequence.CompareTo(right.Sequence);
    }

    private static int[] BuildEventPostings(
        ScenePlaybackTrackMarker[] markers,
        bool[] observationMarkers,
        out Dictionary<ScenePlaybackEventScope, int[]> postings)
    {
        var all = new List<int>(markers.Length);
        var builders = new Dictionary<ScenePlaybackEventScope, List<int>>();
        for (var markerIndex = 0; markerIndex < markers.Length; markerIndex++)
        {
            if (!observationMarkers[markerIndex])
                continue;

            ref readonly var marker = ref markers[markerIndex];
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

    private int LowerBoundObservationOrdinal(long endObservationOrdinalExclusive)
    {
        var low = 0;
        var high = _markers.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_markers[middle].ObservationOrdinal < endObservationOrdinalExclusive)
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

    private readonly record struct IndexedTrackMarker(
        ScenePlaybackTrackMarker Marker,
        bool IsObservation,
        int Sequence);
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
