using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class GameResourceServiceClientSwitchTests
{
    [Theory]
    [InlineData(ResourceLanguage.English)]
    [InlineData(ResourceLanguage.Korean)]
    [InlineData(ResourceLanguage.TraditionalChinese)]
    public void SelectedClientReloadsRootWhileHistoricalResourcesStayPinned(string resourceLanguage)
    {
        var path = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(value => value.ResourceLanguage = resourceLanguage);
            var language = new LanguageService();
            using var clients = new ClientSettingsService(settings, language);
            using var resources = new GameResourceService(clients);
            var original = resources.ClientContext;
            var historical = resources.GetForClient(original);
            var reloads = 0;
            resources.ResourcesChanged += (_, _) => reloads++;

            clients.SetClientVersion(ClientVersion.Steam);

            Assert.Equal(ClientProfiles.Steam, resources.Profile);
            Assert.Equal("steam", resources.Catalog.ResourceSet.Id);
            Assert.Equal(resourceLanguage == ResourceLanguage.TraditionalChinese ? ResourceLanguage.English : resourceLanguage, resources.CurrentLanguage);
            Assert.Equal(1, reloads);
            Assert.Equal(ClientProfiles.Purple, historical.Profile);
            Assert.Same(original, historical.ClientContext);
            Assert.Same(original.Resources.Shared, historical.Catalog.Shared);
            Assert.StartsWith("purple/", historical.ResolveSkillIconAssetName(12_240_010));
            Assert.StartsWith("steam/", resources.ResolveSkillIconAssetName(12_240_010));

            clients.SetClientVersion(ClientVersion.Purple);

            Assert.Equal(ClientProfiles.Purple, resources.Profile);
            Assert.Same(historical, resources.GetForClient(original));
            Assert.Same(original.Resources.Shared, historical.Catalog.Shared);
            Assert.Equal(2, reloads);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }
}
