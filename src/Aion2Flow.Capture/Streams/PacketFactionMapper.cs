using Cloris.Aion2Flow.SceneRuntime.Identity;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketFactionMapper
{
    public static Faction ToFaction(byte code)
        => code switch
        {
            1 => Faction.Light,
            2 => Faction.Dark,
            _ => Faction.Unknown
        };
}
