using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class DisplayIconCacheTests
{
    [Theory]
    [InlineData(NpcCatalogKind.Friendly, "UT_Marker_Default.png")]
    [InlineData(NpcCatalogKind.Summon, "UT_Marker_Summon_Common.png")]
    [InlineData(NpcCatalogKind.Boss, "UT_Marker_Monster_Boss.png")]
    [InlineData(NpcCatalogKind.Object, "UT_Marker_Envobj.png")]
    [InlineData(NpcCatalogKind.TrainingDummy, "UT_Marker_SkillMaster.png")]
    [InlineData(NpcCatalogKind.Monster, "UT_Marker_SkillMaster.png")]
    [InlineData(NpcCatalogKind.Unknown, "UT_Marker_Default.png")]
    public void NpcKind_Resolves_Configured_Marker_Asset(NpcCatalogKind kind, string expectedAssetName)
    {
        Assert.Equal(expectedAssetName, DisplayIconCache.ResolveNpcMarkerIconAssetName(kind));
    }

    [Fact]
    public void Missing_NpcCatalogEntry_Does_Not_Show_Default_NpcIcon_For_Generic_Entity()
    {
        Assert.Null(DisplayIconCache.ResolveNpcMarkerIcon(null));
    }

    [Theory]
    [InlineData("purple/ICON_TE_SKILL_004.webp")]
    [InlineData("steam/ICON_TE_SKILL_004.webp")]
    public void SkillIconAssetName_Preserves_Resource_Set_In_Asset_Uri(string assetName)
    {
        Assert.Equal(
            new Uri($"avares://Aion2Flow/Assets/Images/Skills/{assetName}"),
            DisplayIconCache.ResolveSkillIconAssetUri(assetName));
    }

    [Theory]
    [InlineData(ClientVersion.Purple)]
    [InlineData(ClientVersion.Steam)]
    public void Catalog_Skill_Icons_Resolve_To_Embedded_Assets(ClientVersion version)
    {
        AvaloniaTestHost.Run(() =>
        {
            var resources = ResourceCatalog.Load(ClientProfiles.Get(version));
            var assetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skill in resources.SkillDefinitions)
            {
                if (resources.ResolveSkillIconAssetName(skill.SkillId) is { } assetName)
                    assetNames.Add(assetName);
            }

            Assert.NotEmpty(assetNames);
            foreach (var assetName in assetNames)
                Assert.True(AssetLoader.Exists(DisplayIconCache.ResolveSkillIconAssetUri(assetName)), assetName);
        });
    }

    [Theory]
    [InlineData(ClientVersion.Purple, "purple/ICON_TE_SKILL_004.webp")]
    [InlineData(ClientVersion.Steam, "steam/ICON_TE_SKILL_004.webp")]
    public void SkillIcon_Loads_Catalog_Asset_And_Clears_Missing_Icon(ClientVersion version, string expectedAssetName)
    {
        AvaloniaTestHost.Run(() =>
        {
            var assetName = ResourceCatalog.Load(ClientProfiles.Get(version)).ResolveSkillIconAssetName(12240010);
            Assert.Equal(expectedAssetName, assetName);

            var control = new SkillIcon { AssetName = assetName };
            var bitmap = Assert.IsType<Bitmap>(control.Source);
            Assert.True(bitmap.PixelSize.Width > 0);
            Assert.True(bitmap.PixelSize.Height > 0);
            Assert.Same(bitmap, DisplayIconCache.ResolveSkillIcon(assetName));

            control.AssetName = null;
            Assert.Null(control.Source);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("ICON_TE_SKILL_004.webp")]
    [InlineData("../ICON_TE_SKILL_004.webp")]
    [InlineData("/purple/ICON_TE_SKILL_004.webp")]
    [InlineData("purple/../ICON_TE_SKILL_004.webp")]
    [InlineData("purple/nested/ICON_TE_SKILL_004.webp")]
    [InlineData("purple\\ICON_TE_SKILL_004.webp")]
    [InlineData("purple/.webp")]
    [InlineData("purple/ICON_TE_SKILL_004.png")]
    [InlineData("purple/ICON_TE_SKILL_004.webp?query")]
    [InlineData("purple/ICON_TE_SKILL_004#fragment.webp")]
    [InlineData("purple/%2E%2E.webp")]
    public void SkillIconAssetName_Rejects_NonGenerated_Paths(string assetName)
    {
        Assert.Throws<ArgumentException>(() => DisplayIconCache.ResolveSkillIconAssetUri(assetName));
    }
}
