using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class SkillRelatedSkillIndex
{
    public static IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> BuildByOwnerSkillId(IReadOnlyList<SkillRelatedSkill> relations)
        => BuildLookup(relations, static relation => relation.OwnerSkillId);

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> BuildByRelatedSkillCode(IReadOnlyList<SkillRelatedSkill> relations)
        => BuildLookup(relations, static relation => relation.RelatedSkillCode);

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> BuildByRelatedSourceSkillId(IReadOnlyList<SkillRelatedSkill> relations)
        => BuildLookup(relations, static relation => relation.RelatedSourceSkillId);

    private static IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> BuildLookup(IReadOnlyList<SkillRelatedSkill> relations, Func<SkillRelatedSkill, int> keySelector)
    {
        if (relations.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<SkillRelatedSkill>>();
        }

        var groups = new Dictionary<int, List<SkillRelatedSkill>>();
        foreach (var relation in relations)
        {
            var key = keySelector(relation);
            if (key <= 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(relation);
        }

        var result = new Dictionary<int, IReadOnlyList<SkillRelatedSkill>>(groups.Count);
        foreach (var (key, group) in groups)
        {
            result.Add(key, group.ToArray());
        }

        return result.ToFrozenDictionary();
    }
}
