using System.Globalization;

namespace Cloris.Aion2Flow.Services;

public sealed class LanguageService
{
    public const string English = "en-US";
    public const string Korean = "ko-KR";
    public const string TraditionalChinese = "zh-TW";

    private string _currentLanguage = CultureInfo.CurrentCulture.TwoLetterISOLanguageName switch
    {
        "ko" => Korean,
        "zh" => TraditionalChinese,
        _ => English
    };

    public event EventHandler<string>? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<string> SupportedLanguages { get; } =
    [
        English,
        Korean,
        TraditionalChinese
    ];

    public bool SetLanguage(string language)
    {
        if (!SupportedLanguages.Contains(language, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(_currentLanguage, language, StringComparison.Ordinal))
        {
            return false;
        }

        _currentLanguage = language;
        LanguageChanged?.Invoke(this, language);
        return true;
    }
}
