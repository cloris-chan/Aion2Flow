namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceSharedCatalog(
    SkillDefinitionCatalog skillDefinitions,
    IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
    IReadOnlyDictionary<int, SkillBaseProjection> skillBaseProjections,
    IReadOnlyList<SkillEffectReference> skillEffectReferences,
    IReadOnlyList<SkillRelatedSkill> skillRelatedSkills,
    SkillSemanticCatalog skillSemantics,
    IReadOnlyDictionary<int, NpcDefinition> npcDefinitions,
    IReadOnlyDictionary<string, NpcNameDefinition> npcNameDefinitions,
    IReadOnlySet<uint> knownMapIds,
    IReadOnlySet<int> serverCodes)
{
    public SkillDefinitionCatalog SkillDefinitions { get; } = skillDefinitions;
    public IReadOnlyDictionary<int, SkillClientMetadata> SkillClientMetadata { get; } = skillClientMetadata;
    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections { get; } = skillBaseProjections;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillBaseProjection>> SkillBaseProjectionsByBaseSkillId { get; } = SkillBaseProjectionIndex.BuildByBaseSkillId(skillBaseProjections);
    public IReadOnlyList<SkillEffectReference> SkillEffectReferences { get; } = skillEffectReferences;
    public IReadOnlyList<SkillRelatedSkill> SkillRelatedSkills { get; } = skillRelatedSkills;
    public SkillSemanticCatalog SkillSemantics { get; } = skillSemantics;
    public SkillSemanticOwnerGraph SkillSemanticOwnerGraph { get; } = SkillSemanticOwnerGraph.Build(skillSemantics, skillEffectReferences);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByOwnerSkillId { get; } = SkillRelatedSkillIndex.BuildByOwnerSkillId(skillRelatedSkills);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByRelatedSkillCode { get; } = SkillRelatedSkillIndex.BuildByRelatedSkillCode(skillRelatedSkills);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByRelatedSourceSkillId { get; } = SkillRelatedSkillIndex.BuildByRelatedSourceSkillId(skillRelatedSkills);
    public IReadOnlyDictionary<int, NpcDefinition> NpcDefinitions { get; } = npcDefinitions;
    public IReadOnlyDictionary<string, NpcNameDefinition> NpcNameDefinitions { get; } = npcNameDefinitions;
    public IReadOnlySet<uint> KnownMapIds { get; } = knownMapIds;
    public IReadOnlySet<int> ServerCodes { get; } = serverCodes;
    public IReadOnlyDictionary<uint, int> EffectSkillIds { get; } = SkillEffectReferenceIndex.BuildUnambiguousSkillIdsByEffectCode(skillEffectReferences);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> SkillEffectReferencesBySkillId { get; } = SkillEffectReferenceIndex.BuildBySkillId(skillEffectReferences);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> SkillEffectReferencesByEffectCode { get; } = SkillEffectReferenceIndex.BuildByEffectCode(skillEffectReferences);
}
