using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackTrackIndex
{
    private readonly ScenePlaybackTrackMarker[] _markers;

    private ScenePlaybackTrackIndex(long startObservationOrdinal, ScenePlaybackTrackMarker[] markers)
    {
        StartObservationOrdinal = startObservationOrdinal;
        _markers = markers;
    }

    public long StartObservationOrdinal { get; }

    public int Count => _markers.Length;

    public static ScenePlaybackTrackIndex Build(SceneJournalSegment segment, CancellationToken cancellationToken = default)
    {
        if (segment.IsEmpty)
            return new ScenePlaybackTrackIndex(segment.StartObservationOrdinal, []);

        var startObservationOrdinal = segment.StartObservationOrdinal;
        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var markerCount = checked((int)(endObservationOrdinalExclusive - startObservationOrdinal));
        if (markerCount == 0)
            return new ScenePlaybackTrackIndex(startObservationOrdinal, []);

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

                    var lifecycleEventKind = lifecycle.Apply(entry);
                    var marker = ScenePlaybackTrackProjection.CreateMarker(entry, position, position, lifecycleEventKind);
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

        return new ScenePlaybackTrackIndex(startObservationOrdinal, markers);
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
