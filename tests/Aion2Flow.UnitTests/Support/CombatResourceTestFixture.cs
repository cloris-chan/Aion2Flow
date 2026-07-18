using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class CombatResourceTestFixture
{
    private static readonly ResourceCatalogSnapshot s_baseline = ResourceCatalog.Load(ResourceLanguage.English);

    public static void SetResources(
        SkillDisplayCatalog skills,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillBaseProjection>? skillBaseProjections = null,
        IReadOnlyDictionary<uint, int>? effectSkillIds = null)
    {
        CombatResourceRegistry.SetGameResources(CreateSnapshot(skills, npcCatalog, skillBaseProjections, effectSkillIds));
    }

    private static ResourceCatalogSnapshot CreateSnapshot(
        SkillDisplayCatalog skills,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillBaseProjection>? skillBaseProjections,
        IReadOnlyDictionary<uint, int>? effectSkillIds)
    {
        var definitionsById = s_baseline.SkillDefinitions.ToDictionary(static definition => definition.SkillId);
        var skillNames = s_baseline.Skills.ToDictionary(static skill => skill.SkillId, static skill => skill.Name);
        foreach (var skill in skills)
        {
            definitionsById[skill.SkillId] = new SkillDefinition(skill.SkillId, skill.Category, skill.SourceType);
            skillNames[skill.SkillId] = skill.Name;
        }

        var definitions = new SkillDefinitionCatalog(definitionsById.Count);
        foreach (var definition in definitionsById.Values)
        {
            definitions.Add(definition);
        }

        var projections = new Dictionary<int, SkillBaseProjection>(s_baseline.SkillBaseProjections);
        if (skillBaseProjections is not null)
        {
            foreach (var (skillId, projection) in skillBaseProjections)
            {
                projections[skillId] = projection;
            }
        }

        var effectSkills = new Dictionary<uint, int>(s_baseline.EffectSkillIds);
        if (effectSkillIds is not null)
        {
            foreach (var (effectId, skillId) in effectSkillIds)
            {
                effectSkills[effectId] = skillId;
            }
        }

        var npcDefinitions = new Dictionary<int, NpcDefinition>(s_baseline.Shared.NpcDefinitions);
        var npcNames = s_baseline.NpcCatalog.ToDictionary(static pair => pair.Key, static pair => pair.Value.Name);
        foreach (var (npcCode, npc) in npcCatalog)
        {
            npcDefinitions[npcCode] = new NpcDefinition(npcCode, npc.Kind, npc.HpDisplayScale);
            npcNames[npcCode] = npc.Name;
        }

        var shared = new ResourceSharedCatalog(
            definitions,
            projections,
            effectSkills,
            s_baseline.SkillSemanticRuntimeIndex,
            npcDefinitions);
        var locale = new ResourceLocaleCatalog(
            s_baseline.Language,
            skillNames,
            npcNames,
            s_baseline.Maps,
            s_baseline.ServerNames);
        return new ResourceCatalogSnapshot(shared, locale);
    }
}
