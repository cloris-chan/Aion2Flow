namespace Cloris.Aion2Flow.Resources.Generated;

internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);

internal static class ResourcePackManifest
{
    public const string SharedResourceName = "Cloris.Aion2Flow.Resources.Packs.shared.bin";
    public const int SharedUncompressedLength = 2856629;
    public const ulong SharedChecksum = 13350508946137877159UL;

    public static IReadOnlyList<ResourceLocalePackEntry> Locales { get; } =
    [
        new("en-US", "Cloris.Aion2Flow.Resources.Packs.en-US.bin", 659507, 10987845500385301818UL),
        new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.ko-KR.bin", 710591, 519477791636767965UL),
        new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.zh-TW.bin", 664848, 10220167480739325842UL)
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
