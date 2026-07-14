namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketPlayerGroupState
{
    private readonly HashSet<int> _observedPartyStatusMemberIds = [];
    private readonly HashSet<int> _observedForceStatusMemberIds = [];

    public bool TryRegisterPartyStatusMember(int entityId) => entityId > 0 && _observedPartyStatusMemberIds.Add(entityId);

    public bool TryRegisterForceStatusMember(int entityId) => entityId > 0 && _observedForceStatusMemberIds.Add(entityId);
}
