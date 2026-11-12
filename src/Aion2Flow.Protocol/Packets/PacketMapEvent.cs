namespace Cloris.Aion2Flow.Protocol.Packets;

internal enum PacketMapEventKind : byte
{
    Candidate,
    Arrival,
    InstanceRegistered,
    InstanceUnregistered,
    CurrentMap
}

internal readonly record struct PacketMapEvent(PacketMapEventKind Kind, uint Id);
