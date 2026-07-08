using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

internal sealed class SkillDetailSectionAggregation
{
    public readonly Dictionary<CombatEventKey, SkillMetrics> SkillMetrics = [];
    public readonly Dictionary<CombatEventKey, int> EventCounts = [];

    public bool HasSubsetFilter;
    public long FirstObserved;
    public long LastObserved;

    public void Reset(bool hasSubsetFilter)
    {
        SkillMetrics.Clear();
        EventCounts.Clear();
        HasSubsetFilter = hasSubsetFilter;
        FirstObserved = long.MaxValue;
        LastObserved = long.MinValue;
    }
}
