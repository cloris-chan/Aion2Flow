using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal sealed class ScenePlaybackCombatEventIndex
{
    private readonly List<ScenePlaybackEventMarker> _markers = [];
    private readonly List<int> _all = [];
    private readonly Dictionary<ScenePlaybackEventScope, List<int>> _postings = [];
    private CombatDetailProjectionVersion _projectionVersion;
    private int _indexedEventCount;
    private bool _hasProjectionVersion;

    public ScenePlaybackEventReadResult CopyLatest(
        CombatStore combat,
        SceneCombatSnapshotAdapter adapter,
        ScenePlaybackEventScope scope,
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        long endObservationOrdinalExclusive,
        Span<ScenePlaybackEventMarker> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPositionMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPositionMilliseconds, startPositionMilliseconds);
        if (destination.IsEmpty || !scope.IncludesCombatEvents)
            return new ScenePlaybackEventReadResult(0, endObservationOrdinalExclusive);

        Refresh(combat, adapter);
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

    private void Refresh(CombatStore combat, SceneCombatSnapshotAdapter adapter)
    {
        var projectionVersion = adapter.PrepareCurrentFrameEventProjection();
        var events = combat.EventSpan;
        if (!_hasProjectionVersion ||
            projectionVersion != _projectionVersion ||
            _indexedEventCount > events.Length)
        {
            Rebuild(events, adapter, projectionVersion);
            return;
        }

        Append(events, adapter, _indexedEventCount);
        _indexedEventCount = events.Length;
    }

    private void Rebuild(
        CombatEventRange events,
        SceneCombatSnapshotAdapter adapter,
        CombatDetailProjectionVersion projectionVersion)
    {
        _markers.Clear();
        _all.Clear();
        _postings.Clear();
        Append(events, adapter, 0);
        _indexedEventCount = events.Length;
        _projectionVersion = projectionVersion;
        _hasProjectionVersion = true;
    }

    private void Append(CombatEventRange events, SceneCombatSnapshotAdapter adapter, int startIndex)
    {
        for (var eventIndex = startIndex; eventIndex < events.Length; eventIndex++)
        {
            ref readonly var record = ref events[eventIndex];
            if (!adapter.TryResolveCurrentFrameEventSourcePrepared(in record, out var sourceEntityId))
                continue;

            Add(in record, sourceEntityId);
        }
    }

    private void Add(in CombatEventRecord record, int sourceEntityId)
    {
        var contribution = record.Contribution;
        var observation = record.Observation;
        var trackMarker = new ScenePlaybackTrackMarker(
            ScenePlaybackTrack.Combat,
            Math.Max(0, record.ObservedAtMilliseconds),
            Math.Max(0, record.ObservedAtMilliseconds),
            record.SourceObservationOrdinal,
            sourceEntityId,
            record.TargetId,
            record.EventKey,
            ResolveFlags(in contribution),
            ResolvePrimaryAmount(in observation, in contribution),
            null,
            null,
            0,
            0,
            ScenePlaybackLifecycleEventKind.None,
            0,
            0,
            default);
        var marker = new ScenePlaybackEventMarker(
            new ScenePlaybackEventId(ScenePlaybackEventFactKind.Combat, record.Revision),
            trackMarker,
            SkillBaseKey.FromEventKey(record.EventKey));
        var markerIndex = _markers.Count;
        _markers.Add(marker);
        AddPosting(_all, markerIndex);

        var targetEntityId = record.TargetId;
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
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Damage, ScenePlaybackCombatEventFlags.Damage, marker.CombatEventFlags, marker.SkillBaseKey, markerIndex);
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Healing, ScenePlaybackCombatEventFlags.Healing, marker.CombatEventFlags, marker.SkillBaseKey, markerIndex);
        AddCategoryPosting(combatantId, relation, CombatContributionCategory.Shield, ScenePlaybackCombatEventFlags.Shield, marker.CombatEventFlags, marker.SkillBaseKey, markerIndex);
    }

    private void AddCategoryPosting(
        int combatantId,
        ScenePlaybackEventRelation relation,
        CombatContributionCategory category,
        ScenePlaybackCombatEventFlags expectedFlag,
        ScenePlaybackCombatEventFlags markerFlags,
        SkillBaseKey skillBaseKey,
        int markerIndex)
    {
        if ((markerFlags & expectedFlag) == 0)
            return;

        AddPosting(ScenePlaybackEventScope.ForCategory(combatantId, relation, category), markerIndex);
        AddPosting(ScenePlaybackEventScope.ForSkill(combatantId, relation, category, skillBaseKey), markerIndex);
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
        if (posting.Count == 0 || CompareMarkers(_markers[posting[^1]], _markers[markerIndex]) <= 0)
        {
            posting.Add(markerIndex);
            return;
        }

        posting.Insert(UpperBound(posting, _markers[markerIndex]), markerIndex);
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

    private int UpperBound(List<int> posting, in ScenePlaybackEventMarker marker)
    {
        var low = 0;
        var high = posting.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (CompareMarkers(_markers[posting[middle]], marker) <= 0)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int CompareMarkers(in ScenePlaybackEventMarker left, in ScenePlaybackEventMarker right)
    {
        var comparison = left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
    }

    private static ScenePlaybackCombatEventFlags ResolveFlags(in CombatContribution contribution)
    {
        var flags = ScenePlaybackCombatEventFlags.None;
        if (contribution.CountsAsDamage)
            flags |= ScenePlaybackCombatEventFlags.Damage;
        if (contribution.CountsAsHealing)
            flags |= ScenePlaybackCombatEventFlags.Healing;
        if (contribution.CountsAsShieldGrant || contribution.CountsAsShieldAbsorbed)
            flags |= ScenePlaybackCombatEventFlags.Shield;
        return flags;
    }

    private static long ResolvePrimaryAmount(in CombatObservation observation, in CombatContribution contribution)
    {
        if (contribution.CountsAsDamage)
            return contribution.DamageAmount;
        if (contribution.CountsAsHealing)
            return contribution.HealingAmount;
        if (contribution.CountsAsShieldGrant)
            return contribution.ShieldGrantAmount;
        if (contribution.CountsAsShieldAbsorbed)
            return contribution.ShieldAbsorbedAmount;
        return observation.Damage;
    }
}
