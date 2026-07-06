using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class SkillDisplayProjectionIndex
{
    public static IReadOnlyDictionary<int, IReadOnlyList<SkillDisplayProjection>> BuildByPresentationSkillId(IReadOnlyDictionary<int, SkillDisplayProjection> projections)
        => BuildLookup(projections, static projection => projection.PresentationSkillId);

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillDisplayProjection>> BuildByDisplaySkillId(IReadOnlyDictionary<int, SkillDisplayProjection> projections)
        => BuildLookup(projections, static projection => projection.DisplaySkillId);

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillDisplayProjection>> BuildByBaseSkillId(IReadOnlyDictionary<int, SkillDisplayProjection> projections)
        => BuildLookup(projections, static projection => projection.BaseSkillId);

    private static IReadOnlyDictionary<int, IReadOnlyList<SkillDisplayProjection>> BuildLookup(IReadOnlyDictionary<int, SkillDisplayProjection> projections, Func<SkillDisplayProjection, int> keySelector)
    {
        if (projections.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<SkillDisplayProjection>>();
        }

        var groups = new Dictionary<int, List<SkillDisplayProjection>>();
        foreach (var projection in projections.Values)
        {
            var key = keySelector(projection);
            if (key <= 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(projection);
        }

        var result = new Dictionary<int, IReadOnlyList<SkillDisplayProjection>>(groups.Count);
        foreach (var (key, group) in groups)
        {
            result.Add(key, group.ToArray());
        }

        return result.ToFrozenDictionary();
    }
}
