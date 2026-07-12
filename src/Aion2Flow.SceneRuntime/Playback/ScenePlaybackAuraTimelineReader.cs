using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public static class ScenePlaybackAuraTimelineReader
{
    public static ScenePlaybackAuraTimeline Read(SceneJournalSegment segment, int targetEntityId, long durationMilliseconds, CancellationToken cancellationToken = default)
    {
        if (segment.IsEmpty || targetEntityId <= 0 || durationMilliseconds <= 0)
            return ScenePlaybackAuraTimeline.Empty;

        var active = new Dictionary<int, ActiveAura>();
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
                    var entry = entries[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = Math.Max(0, ScenePlaybackTimeline.ResolveOffsetMilliseconds(entry));
                    if (entry.Domain == ObservedEventDomain.Aura && entry.Aura.EntityId == targetEntityId)
                    {
                        ref readonly var aura = ref entry.Aura;
                        if (ScenePlaybackAuraProtocol.IsTrackableOpen(in aura))
                            ApplyOpen(in aura, position, durationMilliseconds, active, coverages, applications);
                        else if (aura.Kind == AuraObservationKind.Open)
                            ApplyReplacement(in aura, position, durationMilliseconds, active, coverages);
                        else if (aura.Kind == AuraObservationKind.Result)
                            ApplyResult(in aura, position, durationMilliseconds, active, coverages);
                    }
                    else if (entry.Domain == ObservedEventDomain.Action &&
                             entry.Action.SourceEntityId == targetEntityId &&
                             ScenePlaybackAuraProtocol.IsRenewal(in entry.Action))
                    {
                        ref readonly var action = ref entry.Action;
                        ApplyRenew(in action, position, durationMilliseconds, active, coverages, applications);
                    }
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

    private static void ApplyOpen(
        in AuraObservation observation,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<int, ActiveAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (active.Remove(observation.InstanceSequenceId, out var previous))
            AddCoverage(previous, Math.Min(positionMilliseconds, ResolveCoverageEnd(previous, durationMilliseconds)), durationMilliseconds, coverages);

        var aura = new ActiveAura(
            observation.EntityId,
            observation.EchoSourceEntityId,
            observation.InstanceSequenceId,
            observation.BuffResourceEffectRef,
            observation.HeadValue,
            positionMilliseconds,
            ResolveExpiration(positionMilliseconds, observation.HeadValue));
        active.Add(observation.InstanceSequenceId, aura);
        AddApplication(aura, positionMilliseconds, ScenePlaybackLifecycleEventKind.Open, durationMilliseconds, applications);
    }

    private static void ApplyReplacement(
        in AuraObservation observation,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<int, ActiveAura> active,
        List<ScenePlaybackAuraCoverage> coverages)
    {
        if (!active.Remove(observation.InstanceSequenceId, out var aura))
            return;

        AddCoverage(aura, Math.Min(positionMilliseconds, ResolveCoverageEnd(aura, durationMilliseconds)), durationMilliseconds, coverages);
    }

    private static void ApplyRenew(
        in ActionObservation observation,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<int, ActiveAura> active,
        List<ScenePlaybackAuraCoverage> coverages,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (!active.TryGetValue(observation.InstanceSequenceId, out var aura))
            return;

        if (aura.DisplayResourceEffectRef.IsEmpty && !observation.ActionResourceEffectRef.IsEmpty)
            aura.DisplayResourceEffectRef = observation.ActionResourceEffectRef;
        var previousEnd = ResolveCoverageEnd(aura, durationMilliseconds);
        if (positionMilliseconds > previousEnd)
        {
            AddCoverage(aura, previousEnd, durationMilliseconds, coverages);
            aura.CoverageStartMilliseconds = positionMilliseconds;
        }

        aura.OriginEntityId = observation.SourceEntityIdCopy;
        aura.ExpirationMilliseconds = ResolveExpiration(positionMilliseconds, aura.DurationMilliseconds);
        AddApplication(aura, positionMilliseconds, ScenePlaybackLifecycleEventKind.Renew, durationMilliseconds, applications);
    }

    private static void ApplyResult(
        in AuraObservation observation,
        long positionMilliseconds,
        long durationMilliseconds,
        Dictionary<int, ActiveAura> active,
        List<ScenePlaybackAuraCoverage> coverages)
    {
        if (!active.Remove(observation.InstanceSequenceId, out var aura))
            return;

        AddCoverage(aura, Math.Min(positionMilliseconds, ResolveCoverageEnd(aura, durationMilliseconds)), durationMilliseconds, coverages);
    }

    private static void AddApplication(
        ActiveAura aura,
        long positionMilliseconds,
        ScenePlaybackLifecycleEventKind kind,
        long durationMilliseconds,
        List<ScenePlaybackAuraApplication> applications)
    {
        if (positionMilliseconds > durationMilliseconds)
            return;

        applications.Add(new ScenePlaybackAuraApplication(
            aura.EntityId,
            aura.OriginEntityId,
            aura.InstanceSequenceId,
            aura.DisplayResourceEffectRef,
            Math.Clamp(positionMilliseconds, 0, durationMilliseconds),
            kind));
    }

    private static void AddCoverage(ActiveAura aura, long endMilliseconds, long durationMilliseconds, List<ScenePlaybackAuraCoverage> coverages)
    {
        var start = Math.Clamp(aura.CoverageStartMilliseconds, 0, durationMilliseconds);
        var end = Math.Clamp(endMilliseconds, 0, durationMilliseconds);
        if (end <= start)
            return;

        coverages.Add(new ScenePlaybackAuraCoverage(
            aura.EntityId,
            aura.OriginEntityId,
            aura.InstanceSequenceId,
            aura.DisplayResourceEffectRef,
            start,
            end));
    }

    private static long ResolveCoverageEnd(ActiveAura aura, long durationMilliseconds)
        => aura.ExpirationMilliseconds ?? durationMilliseconds;

    private static long? ResolveExpiration(long positionMilliseconds, ushort durationMilliseconds)
    {
        if (durationMilliseconds == ushort.MaxValue)
            return null;

        return positionMilliseconds > long.MaxValue - durationMilliseconds
            ? long.MaxValue
            : positionMilliseconds + durationMilliseconds;
    }

    private sealed class ActiveAura(
        int entityId,
        int originEntityId,
        int instanceSequenceId,
        ResourceEffectRef displayResourceEffectRef,
        ushort durationMilliseconds,
        long coverageStartMilliseconds,
        long? expirationMilliseconds)
    {
        public int EntityId { get; } = entityId;
        public int OriginEntityId { get; set; } = originEntityId;
        public int InstanceSequenceId { get; } = instanceSequenceId;
        public ResourceEffectRef DisplayResourceEffectRef { get; set; } = displayResourceEffectRef;
        public ushort DurationMilliseconds { get; } = durationMilliseconds;
        public long CoverageStartMilliseconds { get; set; } = coverageStartMilliseconds;
        public long? ExpirationMilliseconds { get; set; } = expirationMilliseconds;
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
    long StartMilliseconds,
    long EndMilliseconds);

public readonly record struct ScenePlaybackAuraApplication(
    int EntityId,
    int OriginEntityId,
    int InstanceSequenceId,
    ResourceEffectRef DisplayResourceEffectRef,
    long PositionMilliseconds,
    ScenePlaybackLifecycleEventKind Kind);
