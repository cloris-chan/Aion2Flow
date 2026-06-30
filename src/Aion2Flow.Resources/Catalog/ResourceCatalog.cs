using System.Collections.Concurrent;

namespace Cloris.Aion2Flow.Resources.Catalog;

public static class ResourceCatalog
{
    private static readonly Lazy<ResourceSharedCatalog> Shared = new(ResourcePackReader.LoadShared);
    private static readonly ConcurrentDictionary<string, ResourceCatalogSnapshot> Snapshots = new(StringComparer.Ordinal);

    public static ResourceSharedCatalog LoadShared() => Shared.Value;

    public static ResourceCatalogSnapshot Load(string language = ResourceLanguage.English)
    {
        if (!ResourceLanguage.IsSupported(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported resource language.");
        }

        return Snapshots.GetOrAdd(language, static lang => new ResourceCatalogSnapshot(LoadShared(), ResourcePackReader.LoadLocale(lang)));
    }
}
