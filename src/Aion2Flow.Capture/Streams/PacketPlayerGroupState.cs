namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketPlayerGroupState
{
    private readonly HashSet<int> _observedPartyStatusMemberIds = [];

    public bool TryRegisterPartyStatusMember(int entityId) => entityId > 0 && _observedPartyStatusMemberIds.Add(entityId);
}
