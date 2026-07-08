using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class SkillBaseProjectionIndex
{
    public static IReadOnlyDictionary<int, IReadOnlyList<SkillBaseProjection>> BuildByBaseSkillId(IReadOnlyDictionary<int, SkillBaseProjection> projections)
    {
        if (projections.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<SkillBaseProjection>>();
        }

        var groups = new Dictionary<int, List<SkillBaseProjection>>();
        foreach (var projection in projections.Values)
        {
            var key = projection.BaseSkillId;
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

        var result = new Dictionary<int, IReadOnlyList<SkillBaseProjection>>(groups.Count);
        foreach (var (key, group) in groups)
        {
            result.Add(key, group.ToArray());
        }

        return result.ToFrozenDictionary();
    }
}
