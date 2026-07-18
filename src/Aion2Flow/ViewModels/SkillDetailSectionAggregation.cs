using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.ViewModels;

internal sealed class SkillDetailSectionAggregation
{
    public readonly Dictionary<CombatEventKey, SkillMetrics> SkillMetrics = [];
    public readonly Dictionary<CombatEventKey, int> EventCounts = [];
    private readonly HashSet<CombatDetailOccurrenceKey> _occurrences = [];

    public bool HasSubsetFilter;
    public long FirstObserved;
    public long LastObserved;

    public void Reset(bool hasSubsetFilter)
    {
        SkillMetrics.Clear();
        EventCounts.Clear();
        _occurrences.Clear();
        HasSubsetFilter = hasSubsetFilter;
        FirstObserved = long.MaxValue;
        LastObserved = long.MinValue;
    }

    public void CountOccurrence(CombatDetailFact fact)
    {
        var occurrenceOrdinal = fact.SourceObservationOrdinal >= 0
            ? fact.SourceObservationOrdinal
            : fact.Revision;
        if (!_occurrences.Add(new CombatDetailOccurrenceKey(
                fact.EventKey,
                fact.SourceId,
                fact.TargetId,
                fact.ObservedAtMilliseconds,
                occurrenceOrdinal)))
        {
            return;
        }

        ref var eventCount = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            EventCounts,
            fact.EventKey,
            out _);
        eventCount++;
    }
}

internal readonly record struct CombatDetailOccurrenceKey(
    CombatEventKey EventKey,
    int SourceId,
    int TargetId,
    long ObservedAtMilliseconds,
    long ObservationOrdinal);
