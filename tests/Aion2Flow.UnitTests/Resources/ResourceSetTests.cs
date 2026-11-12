using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class ResourceSetTests
{
    [Theory]
    [InlineData(ClientVersion.Purple, ProtocolRevision.Purple20260701)]
    [InlineData(ClientVersion.Steam, ProtocolRevision.Steam20261029)]
    public void CurrentClientBindsOneProtocolAndResourceSet(ClientVersion version, ProtocolRevision revision)
    {
        var profile = Assert.Single(ClientProfiles.Supported, profile => profile.Version == version);
        var descriptor = ResourceCatalog.GetResourceSet(profile);
        Assert.Equal(revision, profile.ProtocolRevision);
        Assert.Equal(profile.ResourceSetId, descriptor.Id);
        Assert.Equal(64, descriptor.Revision.Length);
        Assert.Contains(descriptor.DefaultLanguage, descriptor.Languages);
        Assert.Same(profile, ClientProfiles.Get(version));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientProfiles.Get((ClientVersion)99));
    }

    [Fact]
    public void SteamLocalesAndResourcesStayInsideSteamSet()
    {
        var steam = ResourceCatalog.GetResourceSet(ClientProfiles.Steam);
        Assert.Equal(["de-DE", "en-US", "es-ES", "fr-FR", "ja-JP", "ko-KR", "pt-BR", "ru-RU"], steam.Languages);
        var english = ResourceCatalog.Load(ClientProfiles.Steam);
        Assert.Same(english, ResourceCatalog.Load(ClientProfiles.Steam, ResourceLanguage.TraditionalChinese));
        Assert.NotSame(ResourceCatalog.LoadShared(ClientProfiles.Purple), english.Shared);
        foreach (var language in steam.Languages)
        {
            var localized = ResourceCatalog.Load(ClientProfiles.Steam, language);
            Assert.Same(english.Shared, localized.Shared);
            Assert.Equal(language, localized.Language);
            Assert.Equal(15_541, localized.SkillDefinitions.Count);
            Assert.Equal(12_620, localized.NpcCatalog.Count);
            Assert.Equal(823, localized.Maps.Count);
            Assert.StartsWith("steam/", localized.ResolveSkillIconAssetName(12_240_010));
        }
    }

    [Fact]
    public void MissingLocaleResolvesInsideSelectedSetAndSharesCachedSnapshot()
    {
        var profile = ClientProfiles.Purple;
        var descriptor = ResourceCatalog.GetResourceSet(profile);
        var expected = ResourceCatalog.Load(profile, descriptor.DefaultLanguage);
        Assert.Same(expected, ResourceCatalog.Load(profile, "ja-JP"));
        Assert.Same(expected, ResourceCatalog.Load(profile));
        Assert.Same(expected.Shared, ResourceCatalog.LoadShared(profile));
        Assert.Same(expected.ResourceSet, ResourceCatalog.Load(profile, ResourceLanguage.Korean).ResourceSet);
    }

    [Fact]
    public void ResourceSetOwnsItsLocaleListAndIconResolver()
    {
        ResourceLocalePackEntry[] locales = [new("en-US", "a", 1, 1), new("ja-JP", "b", 1, 2)];
        var descriptor = new ResourceSetDescriptor("test", "revision", "ja-JP", 1, new("shared", 1, 1), locales,
            id => id == 12 ? "test/icon.webp" : null);
        locales[0] = new("zh-TW", "replacement", 1, 1);

        Assert.Equal(["en-US", "ja-JP"], descriptor.Languages);
        Assert.Equal("ja-JP", descriptor.ResolveLanguage("zh-TW"));
        Assert.Equal("en-US", descriptor.ResolveLanguage("en-US"));
        Assert.Equal("test/icon.webp", descriptor.ResolveSkillIconAssetName(12));
        Assert.Null(descriptor.ResolveSkillIconAssetName(13));
    }

    [Fact]
    public void CachedResourceLookupDoesNotAllocate()
    {
        var profile = ClientProfiles.Purple;
        _ = ResourceCatalog.Load(profile, ResourceLanguage.English);
        _ = ResourceCatalog.LoadShared(profile);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = ResourceCatalog.Load(profile, ResourceLanguage.English);
            _ = ResourceCatalog.LoadShared(profile);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void LocalizePreservesOriginatingSharedDataAndContentIdentity()
    {
        var resourceSet = ResourceCatalog.GetResourceSet(ClientProfiles.Purple);
        var shared = CreateShared();
        var originLocale = new ResourceLocaleCatalog("en-US", new Dictionary<int, string> { [1] = "custom" },
            new Dictionary<int, string>(), new Dictionary<uint, string>(), new Dictionary<int, ServerNameEntry>());
        var origin = new ResourceCatalogSnapshot(shared, originLocale, resourceSet);
        _ = ResourceCatalog.Load(ClientProfiles.Purple, ResourceLanguage.Korean);

        var localized = ResourceCatalog.Localize(origin, ResourceLanguage.Korean);

        Assert.Same(shared, localized.Shared);
        Assert.Same(resourceSet, localized.ResourceSet);
        Assert.Equal(3, localized.SkillDefinitions.Count);
        Assert.Equal(ResourceLanguage.Korean, localized.Language);
        Assert.Same(origin, ResourceCatalog.Localize(origin, ResourceLanguage.English));
    }

    [Fact]
    public void PartialLocaleFallsBackToSameSetDefaultThenNumericIdentity()
    {
        var shared = CreateShared();
        var translated = new ResourceLocaleCatalog("ko-KR",
            new Dictionary<int, string> { [1] = "", [3] = "translated" }, new Dictionary<int, string>(),
            new Dictionary<uint, string> { [10] = "translated map" },
            new Dictionary<int, ServerNameEntry> { [20] = new(20, "", "short") });
        var fallback = new ResourceLocaleCatalog("en-US",
            new Dictionary<int, string> { [1] = "default skill", [3] = "default other" },
            new Dictionary<int, string> { [1] = "default npc" },
            new Dictionary<uint, string> { [10] = "default map", [11] = "fallback map" },
            new Dictionary<int, ServerNameEntry> { [20] = new(20, "default server", "default short") });
        var snapshot = new ResourceCatalogSnapshot(shared, translated, fallback: fallback);

        Assert.Equal("default skill", snapshot.Skills[1].Name);
        Assert.Equal("2", snapshot.Skills[2].Name);
        Assert.Equal("translated", snapshot.Skills[3].Name);
        Assert.Equal("default npc", snapshot.NpcCatalog[1].Name);
        Assert.Equal("translated map", snapshot.ResolveMapName(10));
        Assert.Equal("fallback map", snapshot.ResolveMapName(11));
        Assert.Equal("12", snapshot.ResolveMapName(12));
        Assert.Equal("default server", snapshot.ResolveServerName(20));
        Assert.Equal("short", snapshot.ResolveShortServerName(20));
        Assert.Equal("21", snapshot.ResolveServerName(21));
    }

    private static ResourceSharedCatalog CreateShared()
    {
        SkillDefinitionCatalog definitions =
        [
            new(1, SkillCategory.Unknown, SkillSourceType.Unknown),
            new(2, SkillCategory.Unknown, SkillSourceType.Unknown),
            new(3, SkillCategory.Unknown, SkillSourceType.Unknown)
        ];
        return new(definitions, new Dictionary<int, SkillBaseProjection>(), new Dictionary<uint, int>(),
            SkillSemanticRuntimeIndex.Empty, new Dictionary<int, NpcDefinition> { [1] = new(1, NpcCatalogKind.Monster, NpcHpDisplayScale.Normal) });
    }
}
