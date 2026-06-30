namespace Cloris.Aion2Flow.Resources.Catalog;

public static class ResourceLanguage
{
    public const string English = "en-US";
    public const string Korean = "ko-KR";
    public const string TraditionalChinese = "zh-TW";

    public static bool IsSupported(string language) => string.Equals(language, English, StringComparison.Ordinal) || string.Equals(language, Korean, StringComparison.Ordinal) || string.Equals(language, TraditionalChinese, StringComparison.Ordinal);
}
