using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Resources.Generated;

internal static class ResourcePackManifest
{
    public static IReadOnlyList<ResourceSetDescriptor> Sets { get; } = Array.AsReadOnly<ResourceSetDescriptor>(
    [
        new("purple", "afd17cbbde24ac03b2edad1c69a39e85a6f9fffd0b53b01e4528992f7499e9df", "en-US", 93,
            new("Cloris.Aion2Flow.Resources.Packs.purple.shared.bin", 2982487, 8420135885003519887UL),
            [
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.purple.en-US.bin", 705829, 184102205626138821UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.purple.ko-KR.bin", 763137, 8853120138263115137UL),
                new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.purple.zh-TW.bin", 713968, 15178002117978410775UL),
            ], SkillIconCatalog_purple.ResolveAssetName),
        new("steam", "c2ff19e30dbe3b2cdcc89aa57d6761f8786dd4451375ac95927a8c2761a9a3cc", "en-US", 200,
            new("Cloris.Aion2Flow.Resources.Packs.steam.shared.bin", 2684081, 9933806750674850944UL),
            [
                new("de-DE", "Cloris.Aion2Flow.Resources.Packs.steam.de-DE.bin", 683225, 6080167658920952354UL),
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.steam.en-US.bin", 625017, 5997326108423652998UL),
                new("es-ES", "Cloris.Aion2Flow.Resources.Packs.steam.es-ES.bin", 729458, 17849489414397126476UL),
                new("fr-FR", "Cloris.Aion2Flow.Resources.Packs.steam.fr-FR.bin", 719206, 16879772588807178837UL),
                new("ja-JP", "Cloris.Aion2Flow.Resources.Packs.steam.ja-JP.bin", 720702, 16685647891168699518UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.steam.ko-KR.bin", 689342, 11420875558546899404UL),
                new("pt-BR", "Cloris.Aion2Flow.Resources.Packs.steam.pt-BR.bin", 718173, 15385975043107363724UL),
                new("ru-RU", "Cloris.Aion2Flow.Resources.Packs.steam.ru-RU.bin", 1035424, 18436167878154348302UL),
            ], SkillIconCatalog_steam.ResolveAssetName),
    ]);
}
