namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillDefinition(int SkillId, SkillCategory Category, SkillSourceType SourceType, string SourceKey, string? TriggeredSkillIdsCsv)
{
    public IEnumerable<int> EnumerateTriggeredSkillIds()
    {
        if (string.IsNullOrWhiteSpace(TriggeredSkillIdsCsv))
        {
            yield break;
        }

        foreach (var part in TriggeredSkillIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0)
            {
                yield return id;
            }
        }
    }
}
