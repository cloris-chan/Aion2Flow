namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillDefinition(
    int SkillId,
    SkillCategory Category,
    SkillSourceType SourceType,
    int MaxAvailableCount = 0);
