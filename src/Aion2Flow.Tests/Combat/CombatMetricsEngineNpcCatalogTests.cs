using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineNpcCatalogTests
{
    [Fact]
    public void LoadSkillMap_Also_Loads_NpcCatalog()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");

        Assert.True(CombatMetricsEngine.TryResolveNpcCatalogEntry(2000002, out var entry));
        Assert.Equal("德拉克紐特弓手", entry.Name);
        Assert.Equal(NpcCatalogKind.Monster, entry.Kind);
    }

    [Theory]
    [InlineData(NpcCatalogKind.Monster, NpcKind.Monster)]
    [InlineData(NpcCatalogKind.Boss, NpcKind.Boss)]
    [InlineData(NpcCatalogKind.Summon, NpcKind.Summon)]
    [InlineData(NpcCatalogKind.Friendly, NpcKind.Friendly)]
    [InlineData(NpcCatalogKind.Unknown, NpcKind.Unknown)]
    [InlineData(NpcCatalogKind.Object, NpcKind.Unknown)]
    public void ResolveNpcKind_Maps_Catalog_Kind_Enum(NpcCatalogKind kind, NpcKind expected)
    {
        Assert.Equal(expected, CombatMetricsEngine.ResolveNpcKind(kind));
    }
}
