using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Controls;

public sealed class DisplayIconCacheTests
{
    [Theory]
    [InlineData(NpcCatalogKind.Friendly, "UT_Marker_Default.png")]
    [InlineData(NpcCatalogKind.Summon, "UT_Marker_Summon_Common.png")]
    [InlineData(NpcCatalogKind.Boss, "UT_Marker_Monster_Boss.png")]
    [InlineData(NpcCatalogKind.Object, "UT_Marker_Envobj.png")]
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

    [Fact]
    public void SkillIconAssetName_Resolves_Embedded_Asset_Uri()
    {
        Assert.Equal(
            new Uri("avares://Aion2Flow/Assets/Images/Skills/ICON_TE_SKILL_004.webp"),
            DisplayIconCache.ResolveSkillIconAssetUri("ICON_TE_SKILL_004.webp"));
    }

    [Theory]
    [InlineData("../ICON_TE_SKILL_004.webp")]
    [InlineData("ICON_TE_SKILL_004.png")]
    public void SkillIconAssetName_Rejects_NonGenerated_Paths(string assetName)
    {
        Assert.Throws<ArgumentException>(() => DisplayIconCache.ResolveSkillIconAssetUri(assetName));
    }
}
