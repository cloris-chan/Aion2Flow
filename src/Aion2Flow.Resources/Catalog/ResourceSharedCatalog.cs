namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceSharedCatalog(
    SkillDefinitionCatalog skillDefinitions,
    IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
    IReadOnlyDictionary<int, SkillDisplayProjection> skillDisplayProjections,
    IReadOnlyList<SkillEffectReference> skillEffectReferences,
    IReadOnlyDictionary<int, NpcDefinition> npcDefinitions,
    IReadOnlyDictionary<string, NpcNameDefinition> npcNameDefinitions,
    IReadOnlySet<uint> knownMapIds,
    IReadOnlySet<int> serverCodes)
{
    public SkillDefinitionCatalog SkillDefinitions { get; } = skillDefinitions;
    public IReadOnlyDictionary<int, SkillClientMetadata> SkillClientMetadata { get; } = skillClientMetadata;
    public IReadOnlyDictionary<int, SkillDisplayProjection> SkillDisplayProjections { get; } = skillDisplayProjections;
    public IReadOnlyList<SkillEffectReference> SkillEffectReferences { get; } = skillEffectReferences;
    public IReadOnlyDictionary<int, NpcDefinition> NpcDefinitions { get; } = npcDefinitions;
    public IReadOnlyDictionary<string, NpcNameDefinition> NpcNameDefinitions { get; } = npcNameDefinitions;
    public IReadOnlySet<uint> KnownMapIds { get; } = knownMapIds;
    public IReadOnlySet<int> ServerCodes { get; } = serverCodes;
    public IReadOnlyDictionary<uint, int> EffectSkillIds { get; } = SkillEffectReferenceIndex.Build(skillEffectReferences);
}
