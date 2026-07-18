using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal sealed class ScenePlaybackMaterializedEventIndex
{
    private readonly List<ScenePlaybackEventMarker> _markers = [];
    private readonly List<int> _all = [];
    private readonly Dictionary<ScenePlaybackEventScope, List<int>> _postings = [];
    private readonly HashSet<List<int>> _outOfOrderPostings = [];
    private CombatDetailProjectionVersion _projectionVersion;
    private int _indexedMetricEventCount;
    private int _indexedMechanicEventCount;
    private int _indexedResourceEventCount;
    private bool _hasProjectionVersion;

    public ScenePlaybackEventReadResult CopyLatest(
        CombatStore combat,
        MechanicStore mechanics,
        ResourceStore resources,
        SceneCombatSnapshotAdapter adapter,
        ScenePlaybackEventScope scope,
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        long endObservationOrdinalExclusive,
        Span<ScenePlaybackEventMarker> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPositionMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPositionMilliseconds, startPositionMilliseconds);
        if (destination.IsEmpty || !scope.IncludesMaterializedEvents)
            return new ScenePlaybackEventReadResult(0, endObservationOrdinalExclusive);

        Refresh(combat, mechanics, resources, adapter);
        var posting = scope == ScenePlaybackEventScope.All
            ? _all
            : _postings.GetValueOrDefault(scope);
        if (posting is null || posting.Count == 0)
            return new ScenePlaybackEventReadResult(0, endObservationOrdinalExclusive);

        var first = LowerBound(posting, startPositionMilliseconds);
        var end = UpperBound(posting, endPositionMilliseconds, first);
        first = Math.Max(first, end - destination.Length);
        var count = end - first;
        for (var i = 0; i < count; i++)
            destination[i] = _markers[posting[first + i]];

        return new ScenePlaybackEventReadResult(count, endObservationOrdinalExclusive);
    }

    private void Refresh(
        CombatStore combat,
        MechanicStore mechanics,
        ResourceStore resources,
        SceneCombatSnapshotAdapter adapter)
    {
        var projectionVersion = adapter.PrepareCurrentFrameEventProjection();
        var metricEvents = combat.EventSpan;
        var mechanicEvents = mechanics.Events;
        var resourceEvents = resources.Events;
        if (!_hasProjectionVersion ||
            projectionVersion != _projectionVersion ||
            _indexedMetricEventCount > metricEvents.Length ||
            _indexedMechanicEventCount > mechanicEvents.Count ||
            _indexedResourceEventCount > resourceEvents.Count)
        {
            Rebuild(metricEvents, mechanicEvents, resourceEvents, adapter, projectionVersion);
            return;
        }

        BeginPostingBatch();
        try
        {
            AppendMetrics(metricEvents, adapter, _indexedMetricEventCount);
            AppendMechanics(mechanicEvents, adapter, _indexedMechanicEventCount);
            AppendResources(resourceEvents, adapter, _indexedResourceEventCount);
        }
        finally
        {
            CompletePostingBatch();
        }

        UpdateIndexedCounts(metricEvents.Length, mechanicEvents.Count, resourceEvents.Count);
    }

    private void Rebuild(
        CombatEventRange metricEvents,
        IReadOnlyList<CombatMechanicEventRecord> mechanicEvents,
        IReadOnlyList<CombatResourceEventRecord> resourceEvents,
        SceneCombatSnapshotAdapter adapter,
        CombatDetailProjectionVersion projectionVersion)
    {
        _markers.Clear();
        _all.Clear();
        _postings.Clear();
        BeginPostingBatch();
        try
        {
            AppendMetrics(metricEvents, adapter, 0);
            AppendMechanics(mechanicEvents, adapter, 0);
            AppendResources(resourceEvents, adapter, 0);
        }
        finally
        {
            CompletePostingBatch();
        }

        UpdateIndexedCounts(metricEvents.Length, mechanicEvents.Count, resourceEvents.Count);
        _projectionVersion = projectionVersion;
        _hasProjectionVersion = true;
    }

    private void AppendMetrics(CombatEventRange events, SceneCombatSnapshotAdapter adapter, int startIndex)
    {
        for (var eventIndex = startIndex; eventIndex < events.Length; eventIndex++)
        {
            ref readonly var record = ref events[eventIndex];
            if (!adapter.TryResolveCurrentFrameEventSourcePrepared(in record, out var sourceEntityId))
                continue;

            AddMetric(in record, sourceEntityId);
        }
    }

    private void AppendMechanics(
        IReadOnlyList<CombatMechanicEventRecord> events,
        SceneCombatSnapshotAdapter adapter,
        int startIndex)
    {
        for (var eventIndex = startIndex; eventIndex < events.Count; eventIndex++)
        {
            var record = events[eventIndex];
            if (!adapter.TryResolveCurrentFrameEventSourcePrepared(in record, out var sourceEntityId))
                continue;

            AddMechanic(in record, sourceEntityId);
        }
    }

    private void AppendResources(
        IReadOnlyList<CombatResourceEventRecord> events,
        SceneCombatSnapshotAdapter adapter,
        int startIndex)
    {
        for (var eventIndex = startIndex; eventIndex < events.Count; eventIndex++)
        {
            var record = events[eventIndex];
            AddResource(in record, adapter.ResolveCurrentFrameEventSourcePrepared(in record));
        }
    }

    private void AddMetric(in CombatEventRecord record, int sourceEntityId)
    {
        var contribution = record.Contribution;
        var trackMarker = ScenePlaybackTrackProjection.CreateMetricMarker(in record, sourceEntityId);
        var marker = CreateEventMarker(
            ScenePlaybackEventFactKind.Metric,
            record.Revision,
            in trackMarker,
            contribution,
            null,
            null);
        Add(in marker);
    }

    private void AddMechanic(in CombatMechanicEventRecord record, int sourceEntityId)
    {
        var trackMarker = ScenePlaybackTrackProjection.CreateMechanicMarker(in record, sourceEntityId);
        var marker = CreateEventMarker(
            ScenePlaybackEventFactKind.Mechanic,
            record.Revision,
            in trackMarker,
            null,
            record.Mechanic,
            null);
        Add(in marker);
    }

    private void AddResource(in CombatResourceEventRecord record, int sourceEntityId)
    {
        var trackMarker = ScenePlaybackTrackProjection.CreateResourceMarker(in record, sourceEntityId);
        var marker = CreateEventMarker(
            ScenePlaybackEventFactKind.Resource,
            record.Revision,
            in trackMarker,
            null,
            null,
            record.Resource);
        Add(in marker);
    }

    private static ScenePlaybackEventMarker CreateEventMarker(
        ScenePlaybackEventFactKind factKind,
        long revision,
        in ScenePlaybackTrackMarker trackMarker,
        CombatContribution? contribution,
        CombatMechanicOccurrence? mechanic,
        CombatResourceOccurrence? resource)
    {
        return new ScenePlaybackEventMarker(
            new ScenePlaybackEventId(factKind, revision),
            trackMarker,
            SkillBaseKey.FromEventKey(trackMarker.EventKey),
            contribution,
            mechanic,
            resource);
    }

    private void Add(in ScenePlaybackEventMarker marker)
    {
        var markerIndex = _markers.Count;
        _markers.Add(marker);
        AddPosting(_all, markerIndex);

        var sourceEntityId = marker.SourceEntityId;
        var targetEntityId = marker.TargetEntityId;
        if (sourceEntityId > 0)
        {
            AddPosting(ScenePlaybackEventScope.ForCombatant(sourceEntityId), markerIndex);
            AddPosting(ScenePlaybackEventScope.ForRelation(sourceEntityId, ScenePlaybackEventRelation.Outgoing), markerIndex);
            AddCategoryPostings(sourceEntityId, ScenePlaybackEventRelation.Outgoing, in marker, markerIndex);
        }

        if (targetEntityId <= 0)
            return;

        if (targetEntityId != sourceEntityId)
            AddPosting(ScenePlaybackEventScope.ForCombatant(targetEntityId), markerIndex);
        AddPosting(ScenePlaybackEventScope.ForRelation(targetEntityId, ScenePlaybackEventRelation.Incoming), markerIndex);
        AddCategoryPostings(targetEntityId, ScenePlaybackEventRelation.Incoming, in marker, markerIndex);
    }

    private void AddCategoryPostings(
        int combatantId,
        ScenePlaybackEventRelation relation,
        in ScenePlaybackEventMarker marker,
        int markerIndex)
    {
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Damage, ScenePlaybackCombatEventFlags.Damage, in marker, markerIndex);
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Healing, ScenePlaybackCombatEventFlags.Healing, in marker, markerIndex);
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Shield, ScenePlaybackCombatEventFlags.Shield, in marker, markerIndex);
    }

    private void AddCategoryPosting(
        int combatantId,
        ScenePlaybackEventRelation relation,
        CombatContributionCategory category,
        ScenePlaybackCombatEventFlags expectedFlag,
        in ScenePlaybackEventMarker marker,
        int markerIndex)
    {
        if ((marker.CombatEventFlags & expectedFlag) == 0)
            return;

        AddPosting(ScenePlaybackEventScope.ForCategory(combatantId, relation, category), markerIndex);
        AddPosting(ScenePlaybackEventScope.ForSkill(combatantId, relation, category, marker.SkillBaseKey), markerIndex);
    }

    private void AddPosting(ScenePlaybackEventScope scope, int markerIndex)
    {
        if (!_postings.TryGetValue(scope, out var posting))
        {
            posting = [];
            _postings.Add(scope, posting);
        }

        AddPosting(posting, markerIndex);
    }

    private void AddPosting(List<int> posting, int markerIndex)
    {
        var previousMarkerIndex = posting.Count == 0 ? -1 : posting[^1];
        posting.Add(markerIndex);
        if (previousMarkerIndex >= 0 &&
            CompareMarkers(_markers[previousMarkerIndex], _markers[markerIndex]) > 0)
            _outOfOrderPostings.Add(posting);
    }

    private void BeginPostingBatch()
    {
        _outOfOrderPostings.Clear();
    }

    private void CompletePostingBatch()
    {
        foreach (var posting in _outOfOrderPostings)
            posting.Sort(CompareMarkerIndexes);

        _outOfOrderPostings.Clear();
    }

    private int LowerBound(List<int> posting, long positionMilliseconds)
    {
        var low = 0;
        var high = posting.Count;
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

    private int UpperBound(List<int> posting, long positionMilliseconds, int start)
    {
        var low = start;
        var high = posting.Count;
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

    private void UpdateIndexedCounts(int metricCount, int mechanicCount, int resourceCount)
    {
        _indexedMetricEventCount = metricCount;
        _indexedMechanicEventCount = mechanicCount;
        _indexedResourceEventCount = resourceCount;
    }

    private static int CompareMarkers(in ScenePlaybackEventMarker left, in ScenePlaybackEventMarker right)
    {
        var comparison = left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
    }

    private int CompareMarkerIndexes(int left, int right) =>
        CompareMarkers(_markers[left], _markers[right]);

}
