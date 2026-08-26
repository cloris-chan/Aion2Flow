namespace Cloris.Aion2Flow.Resources.Generated;

internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);

internal static class ResourcePackManifest
{
    public const string SharedResourceName = "Cloris.Aion2Flow.Resources.Packs.shared.bin";
    public const int SharedUncompressedLength = 2744250;
    public const ulong SharedChecksum = 2412288865460320844UL;

    public static IReadOnlyList<ResourceLocalePackEntry> Locales { get; } =
    [
        new("en-US", "Cloris.Aion2Flow.Resources.Packs.en-US.bin", 645963, 10602304663408216921UL),
        new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.ko-KR.bin", 695388, 15851645936716819218UL),
        new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.zh-TW.bin", 650883, 9333035740393254877UL)
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
