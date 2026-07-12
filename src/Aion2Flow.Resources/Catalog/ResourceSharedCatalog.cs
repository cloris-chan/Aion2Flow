namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceSharedCatalog(
    SkillDefinitionCatalog skillDefinitions,
    IReadOnlyDictionary<int, SkillBaseProjection> skillBaseProjections,
    IReadOnlyDictionary<uint, int> effectSkillIds,
    SkillSemanticRuntimeIndex skillSemanticRuntimeIndex,
    IReadOnlyDictionary<int, NpcDefinition> npcDefinitions)
{
    public SkillDefinitionCatalog SkillDefinitions { get; } = skillDefinitions;
    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections { get; } = skillBaseProjections;
    public IReadOnlyDictionary<uint, int> EffectSkillIds { get; } = effectSkillIds;
    public SkillSemanticRuntimeIndex SkillSemanticRuntimeIndex { get; } = skillSemanticRuntimeIndex;
    public IReadOnlyDictionary<int, NpcDefinition> NpcDefinitions { get; } = npcDefinitions;
}
