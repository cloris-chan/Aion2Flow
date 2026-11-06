using System.Collections.Concurrent;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Generated;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class ResourceCatalog
{
    private static readonly ConcurrentDictionary<string, Lazy<ResourceSharedCatalog>> Shared = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string ResourceSetId, string Language), Lazy<ResourceCatalogSnapshot>> Snapshots = new();
    private static readonly ConcurrentDictionary<(ResourceSetDescriptor ResourceSet, string Language), Lazy<ResourceLocaleCatalog>> Locales = new();

    public static ResourceSetDescriptor GetResourceSet(ClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        for (var i = 0; i < ResourcePackManifest.Sets.Count; i++)
        {
            var resourceSet = ResourcePackManifest.Sets[i];
            if (string.Equals(resourceSet.Id, profile.ResourceSetId, StringComparison.Ordinal))
                return resourceSet;
        }

        throw new ArgumentOutOfRangeException(nameof(profile), profile.ResourceSetId, "The client resource set is not bundled.");
    }

    public static ResourceSharedCatalog LoadShared() => LoadShared(ClientProfiles.Purple);

    public static ResourceSharedCatalog LoadShared(ClientProfile profile)
    {
        var resourceSet = GetResourceSet(profile);
        return Shared.GetOrAdd(resourceSet.Id, static (_, set) =>
            new Lazy<ResourceSharedCatalog>(() => ResourcePackReader.LoadShared(set)), resourceSet).Value;
    }

    public static ResourceCatalogSnapshot Load(string language = ResourceLanguage.English)
        => Load(ClientProfiles.Purple, language);

    public static ResourceCatalogSnapshot Load(ClientProfile profile, string? requestedLanguage = null)
    {
        var resourceSet = GetResourceSet(profile);
        var language = resourceSet.ResolveLanguage(requestedLanguage);
        return Snapshots.GetOrAdd((resourceSet.Id, language), static (key, state) => new Lazy<ResourceCatalogSnapshot>(() =>
        {
            var locale = LoadLocale(state.ResourceSet, key.Language);
            var fallback = string.Equals(key.Language, state.ResourceSet.DefaultLanguage, StringComparison.Ordinal)
                ? locale
                : LoadLocale(state.ResourceSet, state.ResourceSet.DefaultLanguage);
            return new ResourceCatalogSnapshot(LoadShared(state.Profile), locale, state.ResourceSet, fallback);
        }), (Profile: profile, ResourceSet: resourceSet)).Value;
    }

    public static ResourceCatalogSnapshot Localize(ResourceCatalogSnapshot origin, string? requestedLanguage)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var resourceSet = origin.ResourceSet;
        var language = resourceSet.ResolveLanguage(requestedLanguage);
        if (string.Equals(origin.Language, language, StringComparison.Ordinal))
            return origin;

        if (Snapshots.TryGetValue((resourceSet.Id, language), out var cached) && cached.IsValueCreated)
        {
            var candidate = cached.Value;
            if (ReferenceEquals(candidate.ResourceSet, resourceSet) && ReferenceEquals(candidate.Shared, origin.Shared))
                return candidate;
        }

        var locale = LoadLocale(resourceSet, language);
        var fallback = string.Equals(language, resourceSet.DefaultLanguage, StringComparison.Ordinal)
            ? locale
            : LoadLocale(resourceSet, resourceSet.DefaultLanguage);
        return new ResourceCatalogSnapshot(origin.Shared, locale, resourceSet, fallback);
    }

    private static ResourceLocaleCatalog LoadLocale(ResourceSetDescriptor resourceSet, string language) =>
        Locales.GetOrAdd((resourceSet, language), static key =>
            new Lazy<ResourceLocaleCatalog>(() => ResourcePackReader.LoadLocale(key.ResourceSet, key.Language))).Value;
}
