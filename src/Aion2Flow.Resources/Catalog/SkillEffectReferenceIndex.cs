using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class SkillEffectReferenceIndex
{
    public static IReadOnlyDictionary<uint, int> BuildUnambiguousSkillIdsByEffectCode(IReadOnlyList<SkillEffectReference> references)
    {
        if (references.Count == 0)
        {
            return new Dictionary<uint, int>();
        }

        var candidates = new Dictionary<uint, int>();
        var ambiguous = new HashSet<uint>();
        foreach (var reference in references)
        {
            AddEffectCode(candidates, ambiguous, reference.EffectCode, reference.SkillId);
        }

        foreach (var code in ambiguous)
        {
            candidates.Remove(code);
        }

        return candidates;
    }

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> BuildBySkillId(IReadOnlyList<SkillEffectReference> references)
        => BuildLookup(references, static reference => reference.SkillId);

    public static IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> BuildByEffectCode(IReadOnlyList<SkillEffectReference> references)
        => BuildLookup(references, static reference => reference.EffectCode);

    private static IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> BuildLookup(IReadOnlyList<SkillEffectReference> references, Func<SkillEffectReference, int> keySelector)
    {
        if (references.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<SkillEffectReference>>();
        }

        var groups = new Dictionary<int, List<SkillEffectReference>>();
        foreach (var reference in references)
        {
            var key = keySelector(reference);
            if (key <= 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(reference);
        }

        var result = new Dictionary<int, IReadOnlyList<SkillEffectReference>>(groups.Count);
        foreach (var (key, group) in groups)
        {
            result.Add(key, group.ToArray());
        }

        return result.ToFrozenDictionary();
    }

    private static void AddEffectCode(Dictionary<uint, int> candidates, HashSet<uint> ambiguous, int code, int skillId)
    {
        if (code <= 0)
        {
            return;
        }

        var key = unchecked((uint)code);
        if (ambiguous.Contains(key))
        {
            return;
        }

        if (candidates.TryGetValue(key, out var existingSkillId))
        {
            if (existingSkillId != skillId)
            {
                ambiguous.Add(key);
            }

            return;
        }

        candidates[key] = skillId;
    }
}
