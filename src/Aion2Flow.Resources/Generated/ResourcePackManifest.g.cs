namespace Cloris.Aion2Flow.Resources.Generated;

internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);

internal static class ResourcePackManifest
{
    public const string SharedResourceName = "Cloris.Aion2Flow.Resources.Packs.shared.bin";
    public const int SharedUncompressedLength = 4625109;
    public const ulong SharedChecksum = 8400395695865621805UL;

    public static IReadOnlyList<ResourceLocalePackEntry> Locales { get; } =
    [
        new("en-US", "Cloris.Aion2Flow.Resources.Packs.en-US.bin", 928425, 12927534192680688913UL),
        new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.ko-KR.bin", 994550, 8094909676896355751UL),
        new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.zh-TW.bin", 937054, 5235697984185475728UL)
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
