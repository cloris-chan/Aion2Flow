using Cloris.Aion2Flow.Resources;
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

            var zhSkills = ResourceDatabase.LoadSkills(LanguageService.TraditionalChinese);
            var enSkills = ResourceDatabase.LoadSkills(LanguageService.English);
            var zhCatalog = ResourceDatabase.LoadNpcCatalog(LanguageService.TraditionalChinese);
            var enCatalog = ResourceDatabase.LoadNpcCatalog(LanguageService.English);

            Assert.True(zhSkills.TryGetValue(2011101, out var zhSkill));
            Assert.True(enSkills.TryGetValue(2011101, out var enSkill));
            Assert.NotEqual(zhSkill.Name, enSkill.Name);

            Assert.True(zhCatalog.TryGetValue(2000002, out var zhNpc));
            Assert.True(enCatalog.TryGetValue(2000002, out var enNpc));
            Assert.NotEqual(zhNpc.Name, enNpc.Name);

            Assert.Equal(zhSkill.Name, resources.ResolveSkillName(2011101));
            Assert.True(resources.TryResolveNpcCatalogEntry(2000002, out var initialNpc));
            Assert.Equal(zhNpc.Name, initialNpc.Name);

            string? changedLanguage = null;
            resources.ResourcesChanged += (_, language) => changedLanguage = language;

            var switched = languageService.SetLanguage(LanguageService.English);

            Assert.True(switched);
            Assert.Equal(LanguageService.English, changedLanguage);
            Assert.Equal(enSkill.Name, resources.ResolveSkillName(2011101));
            Assert.True(resources.TryResolveNpcCatalogEntry(2000002, out var updatedNpc));
            Assert.Equal(enNpc.Name, updatedNpc.Name);
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap(LanguageService.TraditionalChinese);
        }
    }

    [Theory]
    [InlineData(17040257, "審判之電", "ICON_CL_SKILL_004.webp")]
    [InlineData(17730001, "主神恩寵", "ICON_CL_SKILL_Passive_012.webp")]
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
}
