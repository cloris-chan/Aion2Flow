using System.Collections;
using System.Globalization;
using System.Resources;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Localization;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class LocalizationServicesTests
{
    [Fact]
    public void InterfaceLanguagesCoverSteamAndHaveCompleteTranslations()
    {
        var supportedLanguages = LanguageService.SupportedLanguages;
        foreach (var language in ResourceCatalog.GetResourceSet(ClientProfiles.Steam).Languages)
        {
            Assert.Contains(language, supportedLanguages);
        }

        Assert.Contains(LanguageService.TraditionalChinese, supportedLanguages);
        var manager = new ResourceManager(Strings.ResourceManager.BaseName, typeof(Strings).Assembly);
        var neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(neutral);
        var keys = neutral.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var language in supportedLanguages)
        {
            var culture = language == LanguageService.English
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(language);
            var translations = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            Assert.True(translations is not null, $"Missing interface resources for {language}.");
            Assert.Equal(keys.Count, translations.Cast<DictionaryEntry>().Count());
            foreach (var key in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(translations.GetString(key)), $"Missing {key} in {language}.");
            }
        }
    }

    [Fact]
    public void LocalizationService_Indexer_Updates_When_Language_Changes()
    {
        var languageService = new LanguageService();
        languageService.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(languageService);

        Assert.Equal("就緒", localization["Status_Ready"]);
        Assert.True(languageService.SetLanguage(LanguageService.English));
        Assert.Equal("Ready", localization["Status_Ready"]);
    }

    [Fact]
    public void InterfaceAndResourceLanguagesChangeIndependently()
    {
        using var fixture = new ResourceSettingsFixture();
        using var localization = new LocalizationService(fixture.Language);
        using var resources = new GameResourceService(fixture.ClientSettings);
        var contextResources = resources.GetForClient(SceneClientContext.Purple);
        var zh = ResourceCatalog.Load(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese);
        var en = ResourceCatalog.Load(ClientProfiles.Purple, ResourceLanguage.English);
        Assert.True(zh.Skills.TryGetValue(2011101, out var zhSkill));
        Assert.True(en.Skills.TryGetValue(2011101, out var enSkill));
        Assert.Equal(zhSkill.Name, resources.ResolveSkillName(2011101));
        Assert.Equal(zh.ResolveServerName(1001), resources.ResolveServerName(1001));

        var resourcesChanged = 0;
        resources.ResourcesChanged += (_, _) => resourcesChanged++;
        fixture.Language.SetLanguage(LanguageService.Korean);
        Assert.Equal(ResourceLanguage.TraditionalChinese, resources.CurrentLanguage);
        Assert.Equal(0, resourcesChanged);

        Assert.True(fixture.ClientSettings.SetResourceLanguage(ResourceLanguage.English));
        Assert.Equal(LanguageService.Korean, fixture.Language.CurrentLanguage);
        Assert.Equal(ResourceLanguage.English, resources.CurrentLanguage);
        Assert.Equal(enSkill.Name, resources.ResolveSkillName(2011101));
        Assert.Equal(en.NpcCatalog[2000002].Name, resources.ResolveNpcName(2000002));
        Assert.Equal(en.ResolveServerName(1001), resources.ResolveServerName(1001));
        Assert.Equal(en.ResolveShortServerName(1001), resources.ResolveShortServerName(1001));
        Assert.Equal(ResourceLanguage.English, contextResources.CurrentLanguage);
        Assert.Equal(1, resourcesChanged);
        Assert.Equal(ResourceLanguage.English, fixture.Settings.Current.ResourceLanguage);
    }

    [Fact]
    public void SwitchingClientSelectsItsOwnLocalesWithoutChangingInterfaceOrCapturedContext()
    {
        using var fixture = new ResourceSettingsFixture();
        using var resources = new GameResourceService(fixture.ClientSettings);
        var capturedPurple = resources.GetForClient(SceneClientContext.Purple);

        Assert.True(fixture.ClientSettings.SetClientVersion(ClientVersion.Steam));
        Assert.Equal(LanguageService.English, fixture.Language.CurrentLanguage);
        Assert.Equal(ClientVersion.Steam, fixture.Settings.Current.ClientVersion);
        Assert.Equal("steam", resources.Catalog.ResourceSet.Id);
        Assert.Equal(ResourceLanguage.English, resources.CurrentLanguage);
        Assert.Equal("purple", capturedPurple.Catalog.ResourceSet.Id);
        Assert.DoesNotContain(ResourceLanguage.TraditionalChinese, fixture.ClientSettings.ResourceLanguages);
        Assert.False(fixture.ClientSettings.SetResourceLanguage(ResourceLanguage.TraditionalChinese));
        Assert.True(fixture.ClientSettings.SetResourceLanguage("ja-JP"));
        Assert.Equal("ja-JP", resources.CurrentLanguage);
        Assert.Equal(ResourceLanguage.English, capturedPurple.CurrentLanguage);
        Assert.Equal(LanguageService.English, fixture.Language.CurrentLanguage);

        Assert.True(fixture.ClientSettings.SetClientVersion(ClientVersion.Purple));
        Assert.Equal(ResourceLanguage.English, resources.CurrentLanguage);
        Assert.Equal("purple", resources.Catalog.ResourceSet.Id);
    }

    [Fact]
    public void ExistingLanguagePreferenceInitializesResourceLanguageOnce()
    {
        using var fixture = new ResourceSettingsFixture();
        Assert.Equal(ResourceLanguage.TraditionalChinese, fixture.ClientSettings.ResourceLanguage);
        fixture.Settings.Update(settings => settings.Language = LanguageService.English);
        Assert.Equal(ResourceLanguage.TraditionalChinese, fixture.ClientSettings.ResourceLanguage);
        using var restored = new ClientSettingsService(new SettingsService(fixture.Settings.FilePath), fixture.Language);
        Assert.Equal(ResourceLanguage.TraditionalChinese, restored.ResourceLanguage);
        Assert.Equal(ResourceCatalog.GetResourceSet(ClientProfiles.Purple).Languages, restored.ResourceLanguages);
        Assert.False(restored.SetResourceLanguage("ja-JP"));
    }

    [Fact]
    public void ContextResourceViewsArePinnedAndReused()
    {
        using var fixture = new ResourceSettingsFixture();
        using var resources = new GameResourceService(fixture.ClientSettings);
        var context = SceneClientContext.Purple;
        var pinned = resources.GetForClient(context);
        Assert.Same(pinned, resources.GetForClient(context));
        Assert.Same(pinned, pinned.GetForClient(context));
        Assert.Same(context, pinned.ClientContext);
        Assert.Same(context.CombatResources, pinned.CombatResources);
        Assert.Same(context.Resources.Shared, pinned.Catalog.Shared);
    }

    [Fact]
    public void DistinctOriginCatalogsInSameResourceSetKeepTheirOwnResources()
    {
        using var fixture = new ResourceSettingsFixture();
        fixture.ClientSettings.SetResourceLanguage(ResourceLanguage.English);
        using var resources = new GameResourceService(fixture.ClientSettings);
        const int skillId = 999_999_991;
        var first = CombatResourceTestFixture.CreateContext(
            [new SkillDisplayEntry(skillId, "First", SkillCategory.Gladiator, SkillSourceType.PcSkill)],
            new Dictionary<int, NpcDisplayEntry>());
        var second = CombatResourceTestFixture.CreateContext(
            [new SkillDisplayEntry(skillId, "Second", SkillCategory.Gladiator, SkillSourceType.PcSkill)],
            new Dictionary<int, NpcDisplayEntry>());
        var firstView = resources.GetForClient(first);
        var secondView = resources.GetForClient(second);

        Assert.NotSame(firstView, secondView);
        Assert.Equal("First", firstView.ResolveSkillName(skillId));
        Assert.Equal("Second", secondView.ResolveSkillName(skillId));
        fixture.ClientSettings.SetResourceLanguage(ResourceLanguage.TraditionalChinese);
        Assert.Same(first.Resources.Shared, firstView.Catalog.Shared);
        Assert.Same(second.Resources.Shared, secondView.Catalog.Shared);
        Assert.Same(first.CombatResources, firstView.CombatResources);
        Assert.Same(second.CombatResources, secondView.CombatResources);
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
    [InlineData(17270040, "救援", "ICON_CL_SKILL_026.webp")]
    [InlineData(17270047, "救援", "ICON_CL_SKILL_026.webp")]
    [InlineData(17280010, "權能爆炸", "ICON_CL_SKILL_027.webp")]
    [InlineData(17290000, "免罪", "ICON_CL_SKILL_028.webp")]
    [InlineData(17420010, "尤斯迪埃權能", "ICON_CL_SKILL_042.webp")]
    [InlineData(1227237, "攻擊", "ICON_TE_SKILL_001.webp")]
    [InlineData(1227265, "亡靈迅殺", "ICON_TE_SKILL_001.webp")]
    [InlineData(1607415, "攻擊", "ICON_TE_SKILL_001.webp")]
    [InlineData(1607400, "攻擊", "ICON_TE_SKILL_001.webp")]
    public void GameResourceService_Resolves_Display_Names_And_Icons(int skillCode, string expectedName, string expectedIcon)
    {
        using var resources = new GameResourceService(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese);
        Assert.Equal(expectedName, resources.ResolveSkillName(skillCode));
        Assert.Equal($"purple/{expectedIcon}", resources.ResolveSkillIconAssetName(skillCode));
    }

    [Fact]
    public void GameResourceService_Resolves_RowBase_For_SameFamily_Skill()
    {
        using var resources = new GameResourceService(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese);
        Assert.Equal(13_160_000, resources.ResolveBaseSkillIdForCode(13_160_007));
        Assert.Equal(13_160_000, resources.ResolveBaseSkillIdForCode(13_160_000));
    }

    [Theory]
    [InlineData(11_250_000, true)]
    [InlineData(11_250_010, true)]
    [InlineData(2_210_103, false)]
    [InlineData(3_001_110, false)]
    public void GameResourceService_Identifies_Player_Profession_Skills(int skillCode, bool expected)
    {
        using var resources = new GameResourceService(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese);
        Assert.Equal(expected, resources.IsPlayerProfessionSkill(skillCode));
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
        using var resources = new GameResourceService(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese);
        Assert.Equal(expectedName, resources.ResolveSkillName(resourceId));
    }

    private sealed class ResourceSettingsFixture : IDisposable
    {
        public ResourceSettingsFixture()
        {
            var path = Path.Combine(Path.GetTempPath(), $"aion2flow-resources-{Guid.NewGuid():N}.json");
            Settings = new SettingsService(path);
            Settings.Update(settings => settings.Language = LanguageService.TraditionalChinese);
            Language = new LanguageService();
            Language.SetLanguage(LanguageService.English);
            ClientSettings = new ClientSettingsService(Settings, Language);
        }

        public SettingsService Settings { get; }
        public LanguageService Language { get; }
        public ClientSettingsService ClientSettings { get; }

        public void Dispose()
        {
            ClientSettings.Dispose();
            File.Delete(Settings.FilePath);
        }
    }
}
