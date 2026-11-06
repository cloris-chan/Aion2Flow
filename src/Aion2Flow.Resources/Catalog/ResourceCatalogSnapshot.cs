using System.Collections.Frozen;
using System.Globalization;
using Cloris.Aion2Flow.Clients;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceCatalogSnapshot(
    ResourceSharedCatalog shared,
    ResourceLocaleCatalog locale,
    ResourceSetDescriptor? resourceSet = null,
    ResourceLocaleCatalog? fallback = null)
{
    public ResourceSetDescriptor ResourceSet { get; } = resourceSet ?? ResourceCatalog.GetResourceSet(ClientProfiles.Purple);
    public ResourceSharedCatalog Shared { get; } = shared;
    public string Language { get; } = locale.Language;
    public SkillDisplayCatalog Skills { get; } = BuildSkillDisplayCatalog(shared.SkillDefinitions, locale.SkillNames, fallback?.SkillNames);
    public SkillDefinitionCatalog SkillDefinitions => Shared.SkillDefinitions;
    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections => Shared.SkillBaseProjections;
    public SkillSemanticRuntimeIndex SkillSemanticRuntimeIndex => Shared.SkillSemanticRuntimeIndex;
    public IReadOnlyDictionary<uint, int> EffectSkillIds => Shared.EffectSkillIds;
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; } = BuildNpcDisplayCatalog(shared.NpcDefinitions, locale.NpcCatalogNames, fallback?.NpcCatalogNames);
    public IReadOnlyDictionary<uint, string> Maps { get; } = MergeNames(locale.Maps, fallback?.Maps);
    public IReadOnlyDictionary<int, ServerNameEntry> ServerNames { get; } = MergeServerNames(locale.ServerNames, fallback?.ServerNames);

    public string? ResolveSkillIconAssetName(int skillCode) => ResourceSet.ResolveSkillIconAssetName(skillCode);

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

    private static SkillDisplayCatalog BuildSkillDisplayCatalog(SkillDefinitionCatalog definitions, IReadOnlyDictionary<int, string> names, IReadOnlyDictionary<int, string>? fallback)
    {
        var result = new SkillDisplayCatalog(definitions.Count);
        foreach (var definition in definitions)
        {
            var name = ResolveName(names, fallback, definition.SkillId) ?? definition.SkillId.ToString(CultureInfo.InvariantCulture);
            result.Add(new SkillDisplayEntry(definition.SkillId, name, definition.Category, definition.SourceType));
        }

        return result;
    }

    private static FrozenDictionary<int, NpcDisplayEntry> BuildNpcDisplayCatalog(IReadOnlyDictionary<int, NpcDefinition> definitions, IReadOnlyDictionary<int, string> names, IReadOnlyDictionary<int, string>? fallback)
    {
        var result = new Dictionary<int, NpcDisplayEntry>(definitions.Count);
        foreach (var (code, definition) in definitions)
        {
            var name = ResolveName(names, fallback, code);
            if (name is not null)
            {
                result[code] = new NpcDisplayEntry(code, name, definition.Kind, definition.HpDisplayScale);
            }
        }

        return result.ToFrozenDictionary();
    }

    private static string? ResolveName<TKey>(IReadOnlyDictionary<TKey, string> names, IReadOnlyDictionary<TKey, string>? fallback, TKey key) where TKey : notnull
    {
        if (names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return fallback is not null && fallback.TryGetValue(key, out name) && !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    private static IReadOnlyDictionary<TKey, string> MergeNames<TKey>(IReadOnlyDictionary<TKey, string> names, IReadOnlyDictionary<TKey, string>? fallback) where TKey : notnull
    {
        if (fallback is null || ReferenceEquals(names, fallback))
            return names;

        var result = new Dictionary<TKey, string>(fallback);
        foreach (var (key, name) in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                result[key] = name;
        }

        return result.ToFrozenDictionary();
    }

    private static IReadOnlyDictionary<int, ServerNameEntry> MergeServerNames(IReadOnlyDictionary<int, ServerNameEntry> names, IReadOnlyDictionary<int, ServerNameEntry>? fallback)
    {
        if (fallback is null || ReferenceEquals(names, fallback))
            return names;

        var result = new Dictionary<int, ServerNameEntry>(fallback);
        foreach (var (key, entry) in names)
        {
            if (fallback.TryGetValue(key, out var fallbackEntry))
            {
                result[key] = new ServerNameEntry(entry.Code,
                    string.IsNullOrWhiteSpace(entry.ServerName) ? fallbackEntry.ServerName : entry.ServerName,
                    string.IsNullOrWhiteSpace(entry.ShortServerName) ? fallbackEntry.ShortServerName : entry.ShortServerName);
            }
            else
            {
                result[key] = entry;
            }
        }

        return result.ToFrozenDictionary();
    }
}
