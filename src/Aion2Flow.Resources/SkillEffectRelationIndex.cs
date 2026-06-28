namespace Cloris.Aion2Flow.Resources;

public static class SkillEffectRelationIndex
{
    public static IReadOnlyDictionary<uint, int> Build(IReadOnlyList<SkillEffectRelation> relations)
    {
        if (relations.Count == 0)
        {
            return new Dictionary<uint, int>();
        }

        var candidates = new Dictionary<uint, int>();
        var ambiguous = new HashSet<uint>();
        foreach (var relation in relations)
        {
            AddEffectCode(candidates, ambiguous, relation.EffectId, relation.SkillId);
            AddEffectCode(candidates, ambiguous, relation.EffectDataId, relation.SkillId);
            AddEffectCode(candidates, ambiguous, relation.AuxEffectId, relation.SkillId);
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
