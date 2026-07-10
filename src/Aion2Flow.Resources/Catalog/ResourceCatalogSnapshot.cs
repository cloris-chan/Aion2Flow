using System.Collections.Frozen;
using System.Globalization;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceCatalogSnapshot(ResourceSharedCatalog shared, ResourceLocaleCatalog locale)
{
    public ResourceSharedCatalog Shared { get; } = shared;
    public string Language { get; } = locale.Language;
    public SkillDisplayCatalog Skills { get; } = BuildSkillDisplayCatalog(shared.SkillDefinitions, locale.SkillNames);
    public SkillDefinitionCatalog SkillDefinitions => Shared.SkillDefinitions;
    public IReadOnlyDictionary<int, SkillClientMetadata> SkillClientMetadata => Shared.SkillClientMetadata;
    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections => Shared.SkillBaseProjections;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillBaseProjection>> SkillBaseProjectionsByBaseSkillId => Shared.SkillBaseProjectionsByBaseSkillId;
    public IReadOnlyList<SkillEffectReference> SkillEffectReferences => Shared.SkillEffectReferences;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> SkillEffectReferencesBySkillId => Shared.SkillEffectReferencesBySkillId;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillEffectReference>> SkillEffectReferencesByEffectCode => Shared.SkillEffectReferencesByEffectCode;
    public IReadOnlyList<SkillRelatedSkill> SkillRelatedSkills => Shared.SkillRelatedSkills;
    public SkillSemanticCatalog SkillSemantics => Shared.SkillSemantics;
    public SkillSemanticOwnerGraph SkillSemanticOwnerGraph => Shared.SkillSemanticOwnerGraph;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByOwnerSkillId => Shared.SkillRelatedSkillsByOwnerSkillId;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByRelatedSkillCode => Shared.SkillRelatedSkillsByRelatedSkillCode;
    public IReadOnlyDictionary<int, IReadOnlyList<SkillRelatedSkill>> SkillRelatedSkillsByRelatedSourceSkillId => Shared.SkillRelatedSkillsByRelatedSourceSkillId;
    public IReadOnlyDictionary<uint, int> EffectSkillIds => Shared.EffectSkillIds;
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; } = BuildNpcDisplayCatalog(shared.NpcDefinitions, locale.NpcCatalogNames);
    public IReadOnlyDictionary<string, LocalizedNpcName> NpcNames { get; } = BuildLocalizedNpcNames(shared.NpcNameDefinitions, locale.NpcNames);
    public IReadOnlyDictionary<uint, string> Maps { get; } = locale.Maps;
    public IReadOnlyDictionary<int, ServerNameEntry> ServerNames { get; } = locale.ServerNames;

    public string ResolveMapName(uint mapId)
    {
        if (mapId == 0)
        {
            return string.Empty;
        }

        return Maps.TryGetValue(mapId, out var name) ? name : mapId.ToString(CultureInfo.InvariantCulture);
    }

    public string ResolveServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        return ServerNames.TryGetValue(code, out var entry) && !string.IsNullOrWhiteSpace(entry.ServerName)
            ? entry.ServerName
            : code.ToString(CultureInfo.InvariantCulture);
    }

    public string ResolveShortServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        return ServerNames.TryGetValue(code, out var entry) && !string.IsNullOrWhiteSpace(entry.ShortServerName)
            ? entry.ShortServerName
            : ResolveServerName(code);
    }

    private static SkillDisplayCatalog BuildSkillDisplayCatalog(SkillDefinitionCatalog definitions, IReadOnlyDictionary<int, string> names)
    {
        var result = new SkillDisplayCatalog(definitions.Count);
        foreach (var definition in definitions)
        {
            var name = names.TryGetValue(definition.SkillId, out var localizedName) && !string.IsNullOrWhiteSpace(localizedName)
                ? localizedName
                : definition.SkillId.ToString(CultureInfo.InvariantCulture);
            result.Add(new SkillDisplayEntry(definition.SkillId, name, definition.Category, definition.SourceType, definition.SourceKey));
        }

        return result;
    }

    private static FrozenDictionary<int, NpcDisplayEntry> BuildNpcDisplayCatalog(IReadOnlyDictionary<int, NpcDefinition> definitions, IReadOnlyDictionary<int, string> names)
    {
        var result = new Dictionary<int, NpcDisplayEntry>(definitions.Count);
        foreach (var (code, definition) in definitions)
        {
            if (names.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                result[code] = new NpcDisplayEntry(code, name, definition.Kind, definition.HpDisplayScale);
            }
        }

        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, LocalizedNpcName> BuildLocalizedNpcNames(IReadOnlyDictionary<string, NpcNameDefinition> definitions, IReadOnlyDictionary<string, string> names)
    {
        var result = new Dictionary<string, LocalizedNpcName>(StringComparer.Ordinal);
        foreach (var (resourceKey, definition) in definitions)
        {
            if (names.TryGetValue(resourceKey, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                result[resourceKey] = new LocalizedNpcName(resourceKey, name, definition.KeyPrefix, definition.SourceKey);
            }
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
