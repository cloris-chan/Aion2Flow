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
        new("steam", "f6ddff5112f6966c5646bd0228151c71cd544229d50b1b6fb7b6e14deb31bde5", "en-US", 143,
            new("Cloris.Aion2Flow.Resources.Packs.steam.shared.bin", 2684195, 5949163530752889943UL),
            [
                new("de-DE", "Cloris.Aion2Flow.Resources.Packs.steam.de-DE.bin", 699537, 16715665160837909140UL),
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.steam.en-US.bin", 641242, 16320890359623512680UL),
                new("es-ES", "Cloris.Aion2Flow.Resources.Packs.steam.es-ES.bin", 745896, 15182190349088993295UL),
                new("fr-FR", "Cloris.Aion2Flow.Resources.Packs.steam.fr-FR.bin", 735672, 13340742565212791169UL),
                new("ja-JP", "Cloris.Aion2Flow.Resources.Packs.steam.ja-JP.bin", 721970, 7547429948684154685UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.steam.ko-KR.bin", 690530, 6776757211987039980UL),
                new("pt-BR", "Cloris.Aion2Flow.Resources.Packs.steam.pt-BR.bin", 734530, 9906324940967275287UL),
                new("ru-RU", "Cloris.Aion2Flow.Resources.Packs.steam.ru-RU.bin", 1052359, 15056047206501526225UL),
            ], SkillIconCatalog_steam.ResolveAssetName),
    ]);
}
