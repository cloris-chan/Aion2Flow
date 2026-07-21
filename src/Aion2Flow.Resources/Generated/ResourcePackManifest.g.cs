namespace Cloris.Aion2Flow.Resources.Generated;

internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);

internal static class ResourcePackManifest
{
    public const string SharedResourceName = "Cloris.Aion2Flow.Resources.Packs.shared.bin";
    public const int SharedUncompressedLength = 2596809;
    public const ulong SharedChecksum = 17738568802598390121UL;

    public static IReadOnlyList<ResourceLocalePackEntry> Locales { get; } =
    [
        new("en-US", "Cloris.Aion2Flow.Resources.Packs.en-US.bin", 626099, 13721837918953016374UL),
        new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.ko-KR.bin", 671337, 4327749126226496516UL),
        new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.zh-TW.bin", 627381, 16762454998959224933UL)
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
