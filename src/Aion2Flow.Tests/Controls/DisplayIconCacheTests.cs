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
}
