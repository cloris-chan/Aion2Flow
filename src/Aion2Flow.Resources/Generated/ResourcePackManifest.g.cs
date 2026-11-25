using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Resources.Generated;

internal static class ResourcePackManifest
{
    public static IReadOnlyList<ResourceSetDescriptor> Sets { get; } = Array.AsReadOnly<ResourceSetDescriptor>(
    [
        new("purple", "634b606ed103cad6880c0ce7a858f4ab52804a07e4d02a15873a31409a8b5394", "en-US", 155,
            new("Cloris.Aion2Flow.Resources.Packs.purple.shared.bin", 2970793, 7690902690687704245UL),
            [
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.purple.en-US.bin", 703456, 688825741432346786UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.purple.ko-KR.bin", 760802, 13230561855237026055UL),
                new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.purple.zh-TW.bin", 711727, 9471981963598179052UL),
            ], SkillIconCatalog_purple.ResolveAssetName),
        new("steam", "b9fe23740cf0cb4b7c9c4ee04b90a20d44146829fc62429a41af65b520d5797a", "en-US", 61,
            new("Cloris.Aion2Flow.Resources.Packs.steam.shared.bin", 2684735, 11904252090966021011UL),
            [
                new("de-DE", "Cloris.Aion2Flow.Resources.Packs.steam.de-DE.bin", 700076, 13891114835095129965UL),
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.steam.en-US.bin", 642034, 6550164382956753580UL),
                new("es-ES", "Cloris.Aion2Flow.Resources.Packs.steam.es-ES.bin", 746524, 2929739120274862958UL),
                new("fr-FR", "Cloris.Aion2Flow.Resources.Packs.steam.fr-FR.bin", 736275, 11446176502190321603UL),
                new("ja-JP", "Cloris.Aion2Flow.Resources.Packs.steam.ja-JP.bin", 722819, 12250105504840991216UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.steam.ko-KR.bin", 691329, 3237232187220539753UL),
                new("pt-BR", "Cloris.Aion2Flow.Resources.Packs.steam.pt-BR.bin", 735169, 1166303724007065329UL),
                new("ru-RU", "Cloris.Aion2Flow.Resources.Packs.steam.ru-RU.bin", 1053170, 1335427454198376516UL),
            ], SkillIconCatalog_steam.ResolveAssetName),
    ]);
}
