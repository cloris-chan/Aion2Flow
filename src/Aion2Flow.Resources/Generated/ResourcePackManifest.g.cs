namespace Cloris.Aion2Flow.Resources.Generated;

internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);

internal static class ResourcePackManifest
{
    public const string SharedResourceName = "Cloris.Aion2Flow.Resources.Packs.shared.bin";
    public const int SharedUncompressedLength = 2555839;
    public const ulong SharedChecksum = 10427464847642535155UL;

    public static IReadOnlyList<ResourceLocalePackEntry> Locales { get; } =
    [
        new("en-US", "Cloris.Aion2Flow.Resources.Packs.en-US.bin", 621546, 2919485749348982302UL),
        new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.ko-KR.bin", 666186, 15287865173692344129UL),
        new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.zh-TW.bin", 622405, 10143989215006296268UL)
    ];

    public static ResourceLocalePackEntry GetLocale(string language)
    {
        foreach (var entry in Locales)
        {
            if (string.Equals(entry.Language, language, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported resource language.");
    }
}
