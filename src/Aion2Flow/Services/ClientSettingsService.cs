using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Services;

public sealed class ClientSettingsService : IDisposable
{
    private readonly SettingsService _settings;

    public ClientSettingsService(SettingsService settings, LanguageService language)
    {
        _settings = settings;
        CurrentProfile = ClientProfiles.Get(settings.Current.ClientVersion);
        var resourceSet = ResourceCatalog.GetResourceSet(CurrentProfile);
        ResourceLanguage = resourceSet.ResolveLanguage(settings.Current.ResourceLanguage ?? settings.Current.Language ?? language.CurrentLanguage);
        if (!string.Equals(settings.Current.ResourceLanguage, ResourceLanguage, StringComparison.Ordinal))
            settings.Update(value => value.ResourceLanguage = ResourceLanguage);
        _settings.Changed += OnSettingsChanged;
    }

    public ClientProfile CurrentProfile { get; private set; }
    public string ResourceLanguage { get; private set; }
    public IReadOnlyList<string> ResourceLanguages => ResourceCatalog.GetResourceSet(CurrentProfile).Languages;

    public event EventHandler<ClientProfile>? ClientChanged;
    public event EventHandler<string>? ResourceLanguageChanged;

    public bool SetClientVersion(ClientVersion version)
    {
        var profile = ClientProfiles.Get(version);
        if (profile == CurrentProfile)
            return false;
        var language = ResourceCatalog.GetResourceSet(profile).ResolveLanguage(ResourceLanguage);
        _settings.Update(value =>
        {
            value.ClientVersion = version;
            value.ResourceLanguage = language;
        });
        return true;
    }

    public bool SetResourceLanguage(string language)
    {
        if (!ResourceLanguages.Contains(language, StringComparer.Ordinal) ||
            string.Equals(ResourceLanguage, language, StringComparison.Ordinal))
            return false;
        _settings.Update(value => value.ResourceLanguage = language);
        return true;
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var profile = ClientProfiles.Get(_settings.Current.ClientVersion);
        var language = ResourceCatalog.GetResourceSet(profile).ResolveLanguage(_settings.Current.ResourceLanguage);
        var clientChanged = profile != CurrentProfile;
        var languageChanged = !string.Equals(language, ResourceLanguage, StringComparison.Ordinal);
        CurrentProfile = profile;
        ResourceLanguage = language;
        if (clientChanged)
            ClientChanged?.Invoke(this, profile);
        if (languageChanged)
            ResourceLanguageChanged?.Invoke(this, language);
    }
}
