using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackAuraTimelineReader
{
    public static ScenePlaybackAuraTimeline Read(SceneJournalSegment segment, int targetEntityId, long durationMilliseconds, CancellationToken cancellationToken = default)
    {
        if (segment.IsEmpty || targetEntityId <= 0 || durationMilliseconds <= 0)
            return ScenePlaybackAuraTimeline.Empty;

        var lifecycle = new AuraStore();
        var active = new Dictionary<AuraInstanceKey, TimelineAura>();
        var coverages = new List<ScenePlaybackAuraCoverage>();
        var applications = new List<ScenePlaybackAuraApplication>();
        var cursor = segment.CreateCursor();
        while (cursor.NextObservationOrdinal < segment.CurrentEndObservationOrdinalExclusive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = segment.ReadEntries(cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[i];
                    var transition = lifecycle.Apply(entry);
                    if (!transition.HasPreviousState && !transition.HasState)
                        continue;

                    var position = Math.Max(0, ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry));
                    ApplyTransition(
                        in transition,
                        targetEntityId,
                        position,
                        durationMilliseconds,
                        active,
                        coverages,
                        applications);
                }
            });

            if (read.Count == 0)
                break;

            cursor = read.Cursor;
        }

        foreach (var aura in active.Values)
            AddCoverage(aura, ResolveCoverageEnd(aura, durationMilliseconds), durationMilliseconds, coverages);

        coverages.Sort(ScenePlaybackAuraCoverageComparer.Instance);
        applications.Sort(ScenePlaybackAuraApplicationComparer.Instance);
        return new ScenePlaybackAuraTimeline(coverages.ToArray(), applications.ToArray());
    }

    private static void ApplyTransition(
        in AuraLifecycleTransition transition,
        int targetEntityId,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<AuraInstanceKey, TimelineAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (transition.RemovedByReplacement)
        {
            ClosePrevious(
                transition.PreviousState.Key,
                positionMilliseconds,
                durationMilliseconds,
                active,
                coverages);
            return;
        }

        switch (transition.Kind)
        {
            case AuraLifecycleEventKind.Open:
                ApplyOpen(in transition, targetEntityId, positionMilliseconds, durationMilliseconds, active, coverages, applications);
                break;
            case AuraLifecycleEventKind.Renew:
                ApplyRenew(in transition, targetEntityId, positionMilliseconds, durationMilliseconds, active, coverages, applications);
                break;
            case AuraLifecycleEventKind.Result:
                ApplyResult(in transition, targetEntityId, positionMilliseconds, durationMilliseconds, active, coverages, applications);
                break;
        }
    }

    private static void ApplyOpen(
        in AuraLifecycleTransition transition,
        int targetEntityId,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<AuraInstanceKey, TimelineAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (transition.HasPreviousState)
        {
            ClosePrevious(
                transition.PreviousState.Key,
                positionMilliseconds,
                durationMilliseconds,
                active,
                coverages);
        }

        var state = transition.State;
        if (state.TargetEntityId != targetEntityId)
            return;

        var aura = new TimelineAura(in state, positionMilliseconds);
        active[state.Key] = aura;
        AddApplication(aura, positionMilliseconds, AuraLifecycleEventKind.Open, durationMilliseconds, applications);
    }

    private static void ApplyRenew(
        in AuraLifecycleTransition transition,
        int targetEntityId,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<AuraInstanceKey, TimelineAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        var state = transition.State;
        if (state.TargetEntityId != targetEntityId || !active.TryGetValue(state.Key, out var aura))
            return;

        var previousCoverageEnd = ResolveCoverageEnd(aura, durationMilliseconds);
        if (positionMilliseconds > previousCoverageEnd)
        {
            AddCoverage(aura, previousCoverageEnd, durationMilliseconds, coverages);
            aura.CoverageStartMilliseconds = positionMilliseconds;
        }
        else if (RequiresCoverageBoundary(aura, in state) && positionMilliseconds > aura.CoverageStartMilliseconds)
        {
            AddCoverage(aura, positionMilliseconds, durationMilliseconds, coverages);
            aura.CoverageStartMilliseconds = positionMilliseconds;
        }

        ApplyResolvedState(aura, in state, coverages, applications);
        aura.OriginEntityId = state.OriginEntityId;
        aura.ExpirationMilliseconds = state.ExpiresAtMilliseconds;
        AddApplication(aura, positionMilliseconds, AuraLifecycleEventKind.Renew, durationMilliseconds, applications);
    }

    private static void ApplyResult(
        in AuraLifecycleTransition transition,
        int targetEntityId,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<AuraInstanceKey, TimelineAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        var state = transition.State;
        if (state.TargetEntityId != targetEntityId || !active.Remove(state.Key, out var aura))
            return;

        if (CanApplyResultStateToPriorCoverage(aura, in state))
            ApplyResolvedState(aura, in state, coverages, applications);
        AddCoverage(
            aura,
            Math.Min(positionMilliseconds, ResolveCoverageEnd(aura, durationMilliseconds)),
            durationMilliseconds,
            coverages);
    }

    private static bool CanApplyResultStateToPriorCoverage(TimelineAura aura, in AuraInstanceState state) =>
        aura.DisplayResourceEffectRef.IsEmpty ||
        state.ResourceEffectRef.IsEmpty ||
        aura.DisplayResourceEffectRef == state.ResourceEffectRef;

    private static void ClosePrevious(
        AuraInstanceKey key,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<AuraInstanceKey, TimelineAura> active,
        List<ScenePlaybackAuraCoverage> coverages)
    {
        if (!active.Remove(key, out var aura))
            return;

        AddCoverage(
            aura,
            Math.Min(positionMilliseconds, ResolveCoverageEnd(aura, durationMilliseconds)),
            durationMilliseconds,
            coverages);
    }

    private static bool RequiresCoverageBoundary(TimelineAura aura, in AuraInstanceState state) =>
        aura.OriginEntityId != state.OriginEntityId ||
        (!aura.DisplayResourceEffectRef.IsEmpty &&
         !state.ResourceEffectRef.IsEmpty &&
         aura.DisplayResourceEffectRef != state.ResourceEffectRef);

    private static void ApplyResolvedState(
        TimelineAura aura,
        in AuraInstanceState state,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (state.ResourceEffectRef.IsEmpty)
            return;

        var backfill = aura.DisplayResourceEffectRef.IsEmpty;
        aura.DisplayResourceEffectRef = state.ResourceEffectRef;
        aura.Semantics = state.Semantics;
        if (backfill)
            BackfillReferences(aura, coverages, applications);
    }

    private static void AddApplication(
        TimelineAura aura,
        long positionMilliseconds,
        AuraLifecycleEventKind kind,
        long durationMilliseconds,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (positionMilliseconds > durationMilliseconds)
            return;

        var applicationIndex = applications.Count;
        applications.Add(new ScenePlaybackAuraApplication(
            aura.TargetEntityId,
            aura.OriginEntityId,
            aura.InstanceSequenceId,
            aura.DisplayResourceEffectRef,
            aura.Semantics,
            Math.Clamp(positionMilliseconds, 0, durationMilliseconds),
            kind));
        if (aura.DisplayResourceEffectRef.IsEmpty)
            aura.AddUnresolvedApplication(applicationIndex);
    }

    private static void AddCoverage(
        TimelineAura aura,
        long endMilliseconds,
        long durationMilliseconds,
        List<ScenePlaybackAuraCoverage> coverages)
    {
        var start = Math.Clamp(aura.CoverageStartMilliseconds, 0, durationMilliseconds);
        var end = Math.Clamp(endMilliseconds, 0, durationMilliseconds);
        if (end <= start)
            return;

        var coverageIndex = coverages.Count;
        coverages.Add(new ScenePlaybackAuraCoverage(
            aura.TargetEntityId,
            aura.OriginEntityId,
            aura.InstanceSequenceId,
            aura.DisplayResourceEffectRef,
            aura.Semantics,
            start,
            end));
        if (aura.DisplayResourceEffectRef.IsEmpty)
            aura.AddUnresolvedCoverage(coverageIndex);
    }

    private static void BackfillReferences(
        TimelineAura aura,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (aura.DisplayResourceEffectRef.IsEmpty)
            return;

        if (aura.UnresolvedCoverageIndexes is { Count: > 0 } coverageIndexes)
        {
            for (var i = 0; i < coverageIndexes.Count; i++)
            {
                var index = coverageIndexes[i];
                coverages[index] = coverages[index] with
                {
                    DisplayResourceEffectRef = aura.DisplayResourceEffectRef,
                    Semantics = aura.Semantics
                };
            }
        }

        if (aura.UnresolvedApplicationIndexes is { Count: > 0 } applicationIndexes)
        {
            for (var i = 0; i < applicationIndexes.Count; i++)
            {
                var index = applicationIndexes[i];
                applications[index] = applications[index] with
                {
                    DisplayResourceEffectRef = aura.DisplayResourceEffectRef,
                    Semantics = aura.Semantics
                };
            }
        }

        aura.ClearUnresolvedReferences();
    }

    private static long ResolveCoverageEnd(TimelineAura aura, long durationMilliseconds) =>
        aura.ExpirationMilliseconds ?? durationMilliseconds;

    private sealed class TimelineAura
    {
        public TimelineAura(in AuraInstanceState state, long coverageStartMilliseconds)
        {
            TargetEntityId = state.TargetEntityId;
            OriginEntityId = state.OriginEntityId;
            InstanceSequenceId = state.InstanceSequenceId;
            DisplayResourceEffectRef = state.ResourceEffectRef;
            Semantics = state.Semantics;
            CoverageStartMilliseconds = coverageStartMilliseconds;
            ExpirationMilliseconds = state.ExpiresAtMilliseconds;
        }

        public int TargetEntityId { get; }
        public int OriginEntityId { get; set; }
        public int InstanceSequenceId { get; }
        public ResourceEffectRef DisplayResourceEffectRef { get; set; }
        public AuraSemanticValue Semantics { get; set; }
        public long CoverageStartMilliseconds { get; set; }
        public long? ExpirationMilliseconds { get; set; }
        public List<int>? UnresolvedCoverageIndexes { get; private set; }
        public List<int>? UnresolvedApplicationIndexes { get; private set; }

        public void AddUnresolvedCoverage(int coverageIndex)
        {
            UnresolvedCoverageIndexes ??= [];
            UnresolvedCoverageIndexes.Add(coverageIndex);
        }

        public void AddUnresolvedApplication(int applicationIndex)
        {
            UnresolvedApplicationIndexes ??= [];
            UnresolvedApplicationIndexes.Add(applicationIndex);
        }

        public void ClearUnresolvedReferences()
        {
            UnresolvedCoverageIndexes = null;
            UnresolvedApplicationIndexes = null;
        }
    }

    private sealed class ScenePlaybackAuraCoverageComparer : IComparer<ScenePlaybackAuraCoverage>
    {
        public static ScenePlaybackAuraCoverageComparer Instance { get; } = new();

        public int Compare(ScenePlaybackAuraCoverage left, ScenePlaybackAuraCoverage right)
        {
            var comparison = left.DisplayResourceEffectRef.RawId.CompareTo(right.DisplayResourceEffectRef.RawId);
            if (comparison != 0)
                return comparison;

            comparison = left.InstanceSequenceId.CompareTo(right.InstanceSequenceId);
            return comparison != 0 ? comparison : left.StartMilliseconds.CompareTo(right.StartMilliseconds);
        }
    }

    private sealed class ScenePlaybackAuraApplicationComparer : IComparer<ScenePlaybackAuraApplication>
    {
        public static ScenePlaybackAuraApplicationComparer Instance { get; } = new();

        public int Compare(ScenePlaybackAuraApplication left, ScenePlaybackAuraApplication right)
        {
            var comparison = left.DisplayResourceEffectRef.RawId.CompareTo(right.DisplayResourceEffectRef.RawId);
            if (comparison != 0)
                return comparison;

            comparison = left.InstanceSequenceId.CompareTo(right.InstanceSequenceId);
            return comparison != 0 ? comparison : left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        }
    }
}

public sealed class ScenePlaybackAuraTimeline(IReadOnlyList<ScenePlaybackAuraCoverage> coverages, IReadOnlyList<ScenePlaybackAuraApplication> applications)
{
    public static ScenePlaybackAuraTimeline Empty { get; } = new([], []);

    public IReadOnlyList<ScenePlaybackAuraCoverage> Coverages { get; } = coverages;

    public IReadOnlyList<ScenePlaybackAuraApplication> Applications { get; } = applications;
}

public readonly record struct ScenePlaybackAuraCoverage(
    int EntityId,
    int OriginEntityId,
    int InstanceSequenceId,
    ResourceEffectRef DisplayResourceEffectRef,
    AuraSemanticValue Semantics,
    long StartMilliseconds,
    long EndMilliseconds);

public readonly record struct ScenePlaybackAuraApplication(
    int EntityId,
    int OriginEntityId,
    int InstanceSequenceId,
    ResourceEffectRef DisplayResourceEffectRef,
    AuraSemanticValue Semantics,
    long PositionMilliseconds,
    AuraLifecycleEventKind Kind);
