namespace Cloris.Aion2Flow.Resources.Catalog;

public static class SkillEffectReferenceIndex
{
    public static IReadOnlyDictionary<uint, int> Build(IReadOnlyList<SkillEffectReference> references)
    {
        if (references.Count == 0)
        {
            return new Dictionary<uint, int>();
        }

        var candidates = new Dictionary<uint, int>();
        var ambiguous = new HashSet<uint>();
        foreach (var reference in references)
        {
            AddEffectCode(candidates, ambiguous, reference.EffectId, reference.SkillId);
            AddEffectCode(candidates, ambiguous, reference.EffectDataId, reference.SkillId);
            AddEffectCode(candidates, ambiguous, reference.AuxEffectId, reference.SkillId);
        }

        foreach (var code in ambiguous)
        {
            candidates.Remove(code);
        }

        return candidates;
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
