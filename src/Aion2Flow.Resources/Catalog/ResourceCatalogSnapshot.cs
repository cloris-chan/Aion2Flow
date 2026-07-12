using System.Collections.Frozen;
using System.Globalization;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceCatalogSnapshot(ResourceSharedCatalog shared, ResourceLocaleCatalog locale)
{
    public ResourceSharedCatalog Shared { get; } = shared;
    public string Language { get; } = locale.Language;
    public SkillDisplayCatalog Skills { get; } = BuildSkillDisplayCatalog(shared.SkillDefinitions, locale.SkillNames);
    public SkillDefinitionCatalog SkillDefinitions => Shared.SkillDefinitions;
    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections => Shared.SkillBaseProjections;
    public SkillSemanticRuntimeIndex SkillSemanticRuntimeIndex => Shared.SkillSemanticRuntimeIndex;
    public IReadOnlyDictionary<uint, int> EffectSkillIds => Shared.EffectSkillIds;
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; } = BuildNpcDisplayCatalog(shared.NpcDefinitions, locale.NpcCatalogNames);
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
            result.Add(new SkillDisplayEntry(definition.SkillId, name, definition.Category, definition.SourceType));
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
}
