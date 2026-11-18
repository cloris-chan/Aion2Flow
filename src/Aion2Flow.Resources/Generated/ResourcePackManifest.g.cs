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
        new("steam", "a47435409299b46edb9014884d2e2fe41c10c3d2be5c56a2aedd1fc56f90aed2", "en-US", 201,
            new("Cloris.Aion2Flow.Resources.Packs.steam.shared.bin", 2684081, 2331297009174430690UL),
            [
                new("de-DE", "Cloris.Aion2Flow.Resources.Packs.steam.de-DE.bin", 698894, 12701287539990566187UL),
                new("en-US", "Cloris.Aion2Flow.Resources.Packs.steam.en-US.bin", 640657, 291178280718273769UL),
                new("es-ES", "Cloris.Aion2Flow.Resources.Packs.steam.es-ES.bin", 745223, 5682321146723129664UL),
                new("fr-FR", "Cloris.Aion2Flow.Resources.Packs.steam.fr-FR.bin", 734982, 3790338697812161446UL),
                new("ja-JP", "Cloris.Aion2Flow.Resources.Packs.steam.ja-JP.bin", 721336, 9673376822106706419UL),
                new("ko-KR", "Cloris.Aion2Flow.Resources.Packs.steam.ko-KR.bin", 689912, 7610518329959747783UL),
                new("pt-BR", "Cloris.Aion2Flow.Resources.Packs.steam.pt-BR.bin", 733897, 7440546333847247335UL),
                new("ru-RU", "Cloris.Aion2Flow.Resources.Packs.steam.ru-RU.bin", 1051396, 7845029028626399823UL),
            ], SkillIconCatalog_steam.ResolveAssetName),
    ]);
}
