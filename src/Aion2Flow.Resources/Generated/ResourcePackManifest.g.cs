using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Resources.Generated;

internal static class ResourcePackManifest
{
    public static IReadOnlyList<ResourceSetDescriptor> Sets { get; } = Array.AsReadOnly<ResourceSetDescriptor>(
    [
        new("purple", "dcce4d5c167fad9b7008d5b8e52b1d671aeab25c8240b4c9d0234c888e98e808", "en-US", 134,
            new("Cloris.Aion2Flow.Resources.Packs.purple.shared.bin", 2972102, 3022348674186929164UL),
            [
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.purple.en-US.bin", 703845, 17797356794344768073UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.purple.ko-KR.bin", 761236, 573576621319511228UL),
                new("zh-TW", "Cloris.Aion2Flow.Resources.Packs.purple.zh-TW.bin", 712155, 17858659017264951939UL),
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
