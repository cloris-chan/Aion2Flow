using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class PlayerNamePrivacyServiceTests
{
    [Fact]
    public void FormatAnonymousName_UsesLocalizedClassAndOrdinal()
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        var settings = CreateSettings();
        using var privacy = new PlayerNamePrivacyService(settings, localization);

        Assert.Equal("精靈星 2", privacy.FormatAnonymousName(CharacterClass.Elementalist, 2));
        Assert.Equal("玩家 1", privacy.FormatAnonymousName(null, 1));
    }

    [Fact]
    public void HidePlayerNames_RaisesDisplayChanged()
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.TraditionalChinese);
        using var localization = new LocalizationService(language);
        var settings = CreateSettings();
        using var privacy = new PlayerNamePrivacyService(settings, localization);
        var count = 0;
        privacy.DisplayChanged += (_, _) => count++;

        privacy.HidePlayerNames = true;
        privacy.HidePlayerNames = true;
        language.SetLanguage(LanguageService.English);

        Assert.Equal(2, count);
    }

    [Fact]
    public void SceneDisplayContext_AnonymousOrdinal_GroupsByClassAndEntityOrder()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(300, "A", null, CharacterClass: CharacterClass.Elementalist));
        builder.AddPcMetadata(new PcMetadata(100, "B", null, CharacterClass: CharacterClass.Elementalist));
        builder.AddPcMetadata(new PcMetadata(200, "C", null, CharacterClass: CharacterClass.Cleric));
        var language = new LanguageService();
        using var resources = new GameResourceService(language);
        var context = new SceneDisplayContext(builder.ToScope(), null, null, resources, "Unknown");

        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(100));
        Assert.Equal(2, context.ResolvePcAnonymousOrdinal(300));
        Assert.Equal(1, context.ResolvePcAnonymousOrdinal(200));
    }

    private static SettingsService CreateSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        return new SettingsService(path);
    }
}
