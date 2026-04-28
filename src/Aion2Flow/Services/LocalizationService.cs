using Cloris.Aion2Flow.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.Services;

public sealed class LocalizationService : ObservableObject, IDisposable
{
    private readonly LanguageService _languageService;

    public LocalizationService(LanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _languageService.CurrentLanguage;

    public string this[string key] => Get(key);

    public string Get(string key) => Strings.ResourceManager.GetString(key, _languageService.CurrentCulture) ?? string.Empty;

    public void Dispose()
    {
        _languageService.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(CurrentLanguage));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
