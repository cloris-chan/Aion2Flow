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
