namespace Cloris.Aion2Flow.ViewModels;

internal static class PacketCooldownSourceFilter
{
    public static bool MatchesKnownLocalPlayer(int localPlayerEntityId, int sourceEntityId)
        => localPlayerEntityId > 0 && sourceEntityId == localPlayerEntityId;
}
