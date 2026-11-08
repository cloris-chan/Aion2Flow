using System.Globalization;

namespace Cloris.Aion2Flow.Services;

public sealed class LanguageService
{
    public const string English = "en-US";
    public const string Korean = "ko-KR";
    public const string TraditionalChinese = "zh-TW";

    public static readonly IReadOnlyList<string> SupportedLanguages =
    [
        TraditionalChinese,
        English,
        Korean,
        "de-DE",
        "es-ES",
        "fr-FR",
        "ja-JP",
        "pt-BR",
        "ru-RU",
    ];

    private CultureInfo _currentCulture = ResolveDefaultCulture();

    public LanguageService()
    {
        ApplyToCurrentThread(_currentCulture);
    }

    public event EventHandler<string>? LanguageChanged;

    public string CurrentLanguage => _currentCulture.Name;

    public CultureInfo CurrentCulture => _currentCulture;

    public bool SetLanguage(string language)
    {
        if (!SupportedLanguages.Contains(language, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(_currentCulture.Name, language, StringComparison.Ordinal))
        {
            return false;
        }

        _currentCulture = CultureInfo.GetCultureInfo(language);
        ApplyToCurrentThread(_currentCulture);
        LanguageChanged?.Invoke(this, language);
        return true;
    }

    private static CultureInfo ResolveDefaultCulture()
    {
        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        foreach (var language in SupportedLanguages)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            if (culture.TwoLetterISOLanguageName == systemLanguage)
                return culture;
        }

        return CultureInfo.GetCultureInfo(English);
    }

    private static void ApplyToCurrentThread(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
