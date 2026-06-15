using System.Globalization;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Services;

public sealed class PlayerNamePrivacyService : IDisposable
{
    private readonly LocalizationService _localization;
    private bool _hidePlayerNames;

    public PlayerNamePrivacyService(SettingsService settingsService, LocalizationService localization)
    {
        _localization = localization;
        _hidePlayerNames = settingsService.Current.HidePlayerNames;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? DisplayChanged;

    public bool HidePlayerNames
    {
        get => _hidePlayerNames;
        set
        {
            if (_hidePlayerNames == value)
                return;

            _hidePlayerNames = value;
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FormatAnonymousName(CharacterClass? characterClass, int ordinal)
    {
        var normalizedOrdinal = Math.Max(1, ordinal);
        var label = characterClass is null or CharacterClass.None
            ? _localization["PlayerName_AnonymousPlayer"]
            : _localization[$"CharacterClass_{characterClass.Value}"];
        if (string.IsNullOrWhiteSpace(label))
            label = _localization["PlayerName_AnonymousPlayer"];

        return string.Create(CultureInfo.InvariantCulture, $"{label} {normalizedOrdinal}");
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_hidePlayerNames)
            DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}
