using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class LocalizationServicesTests
{
    [Fact]
    public void LocalizationService_Indexer_Updates_When_Language_Changes()
    {
        var languageService = new LanguageService();
        languageService.SetLanguage(LanguageService.English);
        languageService.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(languageService);

        Assert.Equal("就緒", localization["Status_Ready"]);

        var changed = languageService.SetLanguage(LanguageService.English);

        Assert.True(changed);
        Assert.Equal("Ready", localization["Status_Ready"]);
    }

    [Fact]
    public void GameResourceService_Reloads_Skill_And_Npc_Names_When_Language_Changes()
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            var zhSkills = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills;
            var enSkills = ResourceCatalog.Load(ResourceLanguage.English).Skills;
            var zhCatalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
            var enCatalog = ResourceCatalog.Load(ResourceLanguage.English).NpcCatalog;
            var zhServerNames = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).ServerNames;
            var enServerNames = ResourceCatalog.Load(ResourceLanguage.English).ServerNames;

            Assert.True(zhSkills.TryGetValue(2011101, out var zhSkill));
            Assert.True(enSkills.TryGetValue(2011101, out var enSkill));
            Assert.NotEqual(zhSkill.Name, enSkill.Name);

            Assert.True(zhCatalog.TryGetValue(2000002, out var zhNpc));
            Assert.True(enCatalog.TryGetValue(2000002, out var enNpc));
            Assert.NotEqual(zhNpc.Name, enNpc.Name);

            Assert.True(zhServerNames.TryGetValue(1001, out var zhServer));
            Assert.True(enServerNames.TryGetValue(1001, out var enServer));
            Assert.NotEqual(zhServer.ServerName, enServer.ServerName);

            Assert.Equal(zhSkill.Name, resources.ResolveSkillName(2011101));
            Assert.True(resources.TryResolveNpcCatalogEntry(2000002, out var initialNpc));
            Assert.Equal(zhNpc.Name, initialNpc.Name);
            Assert.Equal(zhServer.ServerName, resources.ResolveServerName(1001));
            Assert.Equal(zhServer.ShortServerName, resources.ResolveShortServerName(1001));

            string? changedLanguage = null;
            resources.ResourcesChanged += (_, language) => changedLanguage = language;

            var switched = languageService.SetLanguage(LanguageService.English);

            Assert.True(switched);
            Assert.Equal(LanguageService.English, changedLanguage);
            Assert.Equal(enSkill.Name, resources.ResolveSkillName(2011101));
            Assert.True(resources.TryResolveNpcCatalogEntry(2000002, out var updatedNpc));
            Assert.Equal(enNpc.Name, updatedNpc.Name);
            Assert.Equal(enServer.ServerName, resources.ResolveServerName(1001));
            Assert.Equal(enServer.ShortServerName, resources.ResolveShortServerName(1001));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Theory]
    [InlineData(100014, "火之精靈：基本攻擊", "ICON_EL_SKILL_010.webp")]
    [InlineData(100018, "火之精靈：基本攻擊", "ICON_EL_SKILL_010.webp")]
    [InlineData(100024, "水之精靈：基本攻擊", "ICON_EL_SKILL_011.webp")]
    [InlineData(100028, "水之精靈：基本攻擊", "ICON_EL_SKILL_011.webp")]
    [InlineData(100034, "風之精靈：基本攻擊", "ICON_EL_SKILL_012.webp")]
    [InlineData(100048, "地之精靈：基本攻擊", "ICON_EL_SKILL_013.webp")]
    [InlineData(17040257, "天罰", "ICON_CL_SKILL_005.webp")]
    [InlineData(170402571, "天罰", "ICON_CL_SKILL_005.webp")]
    [InlineData(16030047, "大地震動", "ICON_EL_SKILL_003.webp")]
    [InlineData(160300471, "大地震動", "ICON_EL_SKILL_003.webp")]
    [InlineData(17440047, "高潔氣息", "ICON_CL_SKILL_046.webp")]
    [InlineData(17730001, "主神恩寵", "ICON_CL_SKILL_Passive_012.webp")]
    [InlineData(3001110, "神石：海格黛的束縛", "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    [InlineData(30011101, "神石：海格黛的束縛", "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    [InlineData(3000122, "神石：海格黛的聰明", "Icon_Item_Usable_Godstone_WP_r_016.webp")]
    public void GameResourceService_Resolves_Display_Resources_For_Packet_Variants(int skillCode, string expectedName, string expectedIcon)
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal(expectedName, resources.ResolveSkillName(skillCode));
            Assert.Equal(expectedIcon, resources.ResolveSkillIconAssetName(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Theory]
    [InlineData(17270040, "救援", "ICON_CL_SKILL_026.webp")]
    [InlineData(17270047, "救援", "ICON_CL_SKILL_026.webp")]
    [InlineData(17280010, "權能爆炸", "ICON_CL_SKILL_027.webp")]
    [InlineData(17290000, "免罪", "ICON_CL_SKILL_028.webp")]
    [InlineData(17420010, "尤斯迪埃權能", "ICON_CL_SKILL_042.webp")]
    public void GameResourceService_Resolves_Cleric_Stigma_Display_Resources(int skillCode, string expectedName, string expectedIcon)
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal(expectedName, resources.ResolveSkillName(skillCode));
            Assert.Equal(expectedIcon, resources.ResolveSkillIconAssetName(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Theory]
    [InlineData(1227237, "攻擊", "ICON_TE_SKILL_001.webp")]
    [InlineData(1227265, "亡靈迅殺", "ICON_TE_SKILL_001.webp")]
    public void GameResourceService_Resolves_Client_SkillDat_Display_Names(int skillCode, string expectedName, string expectedIcon)
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal(expectedName, resources.ResolveSkillName(skillCode));
            Assert.Equal(expectedIcon, resources.ResolveSkillIconAssetName(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Fact]
    public void GameResourceService_Uses_SkillDat_Alias_Display_Id()
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal("天罰", resources.ResolveSkillName(17040257));
            Assert.Equal("ICON_CL_SKILL_005.webp", resources.ResolveSkillIconAssetName(17040257));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Fact]
    public void GameResourceService_Resolves_RowBase_For_SameFamily_Skill()
    {
        var languageService = new LanguageService();
        languageService.SetLanguage(LanguageService.TraditionalChinese);
        using var resources = new GameResourceService(languageService);

        Assert.Equal(13_160_000, resources.ResolveBaseSkillIdForCode(13_160_007));
        Assert.Equal(13_160_000, resources.ResolveBaseSkillIdForCode(13_160_000));
    }

    [Theory]
    [InlineData(12130030, "捕獲")]
    [InlineData(12780001, "激昂")]
    [InlineData(2210103, "疾走咒文書")]
    [InlineData(11190000, "跳躍鑿擊")]
    [InlineData(13130000, "紋樣爆炸")]
    [InlineData(13050000, "閃光斬")]
    public void GameResourceService_Resolves_Runtime_Aura_And_Cooldown_RowBase_Ids(int resourceId, string expectedName)
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal(expectedName, resources.ResolveSkillName(resourceId));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Theory]
    [InlineData(1607415, "攻擊", "ICON_TE_SKILL_001.webp")]
    [InlineData(1607400, "攻擊", "ICON_TE_SKILL_001.webp")]
    public void GameResourceService_Resolves_Exact_Client_Skills_Without_Player_Family_Fallback(int skillCode, string expectedName, string expectedIcon)
    {
        try
        {
            var languageService = new LanguageService();
            languageService.SetLanguage(LanguageService.English);
            languageService.SetLanguage(LanguageService.TraditionalChinese);
            using var resources = new GameResourceService(languageService);

            Assert.Equal(expectedName, resources.ResolveSkillName(skillCode));
            Assert.Equal(expectedIcon, resources.ResolveSkillIconAssetName(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }
}
