using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.Tests.SceneRuntime;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class PlayerNameDisplayServiceTests
{
    [Fact]
    public void FormatAnonymousName_UsesLocalizedClassAndOrdinal()
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        var settings = CreateSettings();
        using var display = new PlayerNameDisplayService(settings, localization);

        Assert.Equal("精靈星 2", display.FormatAnonymousName(CharacterClass.Elementalist, 2));
        Assert.Equal("玩家 1", display.FormatAnonymousName(null, 1));
    }

    [Fact]
    public void DisplayOptions_RaiseDisplayChanged()
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        var settings = CreateSettings();
        using var display = new PlayerNameDisplayService(settings, localization);
        var count = 0;
        display.DisplayChanged += (_, _) => count++;

        display.ShowPlayerNames = false;
        display.ShowPlayerNames = false;
        display.ShowShortServerName = true;
        display.ShowLegionName = true;
        display.SelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode.Always;
        display.TintPlayerNamesByFaction = false;
        display.TintPlayerNamesByFaction = true;
        language.SetLanguage(LanguageService.English);

        Assert.Equal(7, count);
    }

    [Fact]
    public void FormatPcName_AppendsConfiguredMetadata_WhenNamesAreVisible()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Perigee", Faction.Light, CharacterClass.Elementalist, IsLocalPlayer: true, OriginServerId: 1001, LegionName: "Aether"));
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), null, null, resources, "Unknown");
        var settings = CreateSettings();
        using var display = new PlayerNameDisplayService(settings, localization)
        {
            ShowShortServerName = true,
            ShowLegionName = true,
            SelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode.Always
        };

        Assert.Equal("⭐Perigee[希埃]<Aether>", display.FormatPcName(context, 100));
    }

    [Fact]
    public void FormatPcName_HidesIdentityMetadata_WhenNamesAreNotShown()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Perigee", Faction.Light, CharacterClass.Elementalist, IsLocalPlayer: true, OriginServerId: 1001, LegionName: "Aether"));
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), null, null, resources, "Unknown");
        var settings = CreateSettings();
        using var display = new PlayerNameDisplayService(settings, localization)
        {
            ShowPlayerNames = false,
            ShowShortServerName = true,
            ShowLegionName = true,
            SelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode.WhenNamesHidden
        };

        Assert.Equal("⭐精靈星 1", display.FormatPcName(context, 100));
    }

    [Fact]
    public void SceneDisplayContext_AnonymousOrdinal_GroupsByClassAndEntityOrder()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(300, "A", CharacterClass: CharacterClass.Elementalist));
        builder.AddPcMetadata(new PcMetadata(100, "B", CharacterClass: CharacterClass.Elementalist));
        builder.AddPcMetadata(new PcMetadata(200, "C", CharacterClass: CharacterClass.Cleric));
        var language = new LanguageService();
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), null, null, resources, "Unknown");

        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(100));
        Assert.Equal(2, context.ResolvePcAnonymousOrdinal(300));
        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(200));
    }

    [Fact]
    public void SceneDisplayContext_AnonymousOrdinal_MergesScopeRegistryAndVisibleCombatants()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Scoped Elementalist", CharacterClass: CharacterClass.Elementalist));
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(50, "Registry Elementalist", characterClass: CharacterClass.Elementalist);
        registry.UpsertPcMetadata(200, "Registry Cleric", characterClass: CharacterClass.Cleric);
        registry.UpsertPcMetadata(300, "Registry Elementalist", characterClass: CharacterClass.Elementalist);
        var snapshot = SceneSnapshotTestFactory.Create(
            combatants:
            [
                SceneSnapshotTestFactory.Combatant(150, SceneSnapshotTestFactory.VisibleMetrics(CharacterClass.Cleric)),
                SceneSnapshotTestFactory.Combatant(250, SceneSnapshotTestFactory.VisibleMetrics(CharacterClass.Elementalist)),
                SceneSnapshotTestFactory.Combatant(400, SceneSnapshotTestFactory.VisibleMetrics(CharacterClass.Elementalist))
            ]);
        var language = new LanguageService();
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), registry, snapshot, resources, "Unknown");

        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(50));
        Assert.Equal(2, context.ResolvePcAnonymousOrdinal(100));
        Assert.Equal(3, context.ResolvePcAnonymousOrdinal(250));
        Assert.Equal(4, context.ResolvePcAnonymousOrdinal(300));
        Assert.Equal(5, context.ResolvePcAnonymousOrdinal(400));
        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(150));
        Assert.Equal(2, context.ResolvePcAnonymousOrdinal(200));
    }

    [Fact]
    public void SceneDisplayContext_ExposesLocalPlayerMetadata()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Perigee", CharacterClass: CharacterClass.Elementalist, IsLocalPlayer: true));
        var language = new LanguageService();
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), null, null, resources, "Unknown");

        Assert.True(context.IsLocalPlayer(100));
        Assert.False(context.IsLocalPlayer(200));
    }

    [Fact]
    public void SceneDisplayContext_MergesIncompleteScopedMetadataWithLiveRegistry()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Perigee", Faction.Light, CharacterClass.Elementalist));
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Perigee", originServerId: 1001, legionName: "Aether");
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), registry, null, resources, "Unknown");
        using var localization = new LocalizationService(language);
        var settings = CreateSettings();
        using var display = new PlayerNameDisplayService(settings, localization)
        {
            ShowShortServerName = true,
            ShowLegionName = true
        };

        Assert.True(context.TryResolvePcMetadata(100, out var metadata));
        Assert.Equal(1001, metadata.OriginServerId);
        Assert.Equal("Aether", metadata.LegionName);
        Assert.Equal("Perigee[希埃]<Aether>", display.FormatPcName(context, 100));
    }

    [Fact]
    public void SceneDisplayContext_ResolvesShortServerName()
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(SceneIdentityScope.Empty, null, null, resources, "Unknown");

        Assert.Equal("希埃", context.ResolveShortServerName(1001));
    }

    [Fact]
    public void SettingsService_PersistsPlayerNameDisplayOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        var settings = new SettingsService(path);

        settings.Update(s =>
        {
            s.ShowPlayerNames = false;
            s.PlayerSelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode.Always;
            s.ShowPlayerShortServerName = true;
            s.ShowPlayerLegionName = true;
            s.TintPlayerNamesByFaction = true;
        });

        var loaded = new SettingsService(path).Current;

        Assert.False(loaded.ShowPlayerNames);
        Assert.Equal(PlayerSelfMarkerDisplayMode.Always, loaded.PlayerSelfMarkerDisplayMode);
        Assert.True(loaded.ShowPlayerShortServerName);
        Assert.True(loaded.ShowPlayerLegionName);
        Assert.True(loaded.TintPlayerNamesByFaction);
    }

    [Fact]
    public void SettingsService_PersistsCombatantStatisticsScope()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        var settings = new SettingsService(path);

        settings.Update(static s => s.CombatantStatisticsScope = CombatantStatisticsScope.Force);

        Assert.Equal(CombatantStatisticsScope.Force, new SettingsService(path).Current.CombatantStatisticsScope);
    }

    [Fact]
    public void SettingsService_PersistsMainMetricVisibility()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        var settings = new SettingsService(path);

        Assert.True(settings.Current.ShowDamagePerSecondColumn);
        Assert.True(settings.Current.ShowDamageColumn);
        Assert.True(settings.Current.ShowTotalDamagePerSecond);

        settings.Update(static s =>
        {
            s.ShowDamagePerSecondColumn = false;
            s.ShowDamageColumn = false;
            s.ShowTotalDamagePerSecond = false;
        });
        settings.Update(static s => s.ShowFocusStatusBar = false);

        var loaded = new SettingsService(path).Current;
        Assert.False(loaded.ShowDamagePerSecondColumn);
        Assert.False(loaded.ShowDamageColumn);
        Assert.False(loaded.ShowTotalDamagePerSecond);
    }

    [Fact]
    public void SettingsService_PersistsAndClampsUiScalePercent()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        var settings = new SettingsService(path);

        Assert.Equal(100, settings.Current.UiScalePercent);

        settings.Update(s => s.UiScalePercent = 225);
        Assert.Equal(200, settings.Current.UiScalePercent);
        Assert.Equal(200, new SettingsService(path).Current.UiScalePercent);

        settings.Update(s => s.UiScalePercent = 25);
        Assert.Equal(50, settings.Current.UiScalePercent);
        Assert.Equal(50, new SettingsService(path).Current.UiScalePercent);
    }

    private static SettingsService CreateSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        return new SettingsService(path);
    }
}
