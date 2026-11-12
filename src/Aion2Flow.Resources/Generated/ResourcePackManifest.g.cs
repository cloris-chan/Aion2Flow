using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Resources.Generated;

internal static class ResourcePackManifest
{
    public static IReadOnlyList<ResourceSetDescriptor> Sets { get; } = Array.AsReadOnly<ResourceSetDescriptor>(
    [
        new("purple", "21b9a2574dc1b915e1fef87e322de2d2415d5561f0ab8f5bfc4495544b36f056", "en-US", 164,
            new("Cloris.Aion2Flow.Resources.Packs.purple.shared.bin", 2982487, 6895710745890810211UL),
            [
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.purple.en-US.bin", 705231, 4643195002797143478UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.purple.ko-KR.bin", 762511, 14664516227839058362UL),
                new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.purple.zh-TW.bin", 713400, 11495344751303620258UL),
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
