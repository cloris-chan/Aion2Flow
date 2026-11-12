using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketMapHandler
{
    public static bool Apply(in PacketMapEvent mapEvent, ushort opcode, int packetLength, ref PacketParseContext context)
    {
        var source = context.CreateObservationSource(opcode, packetLength);
        var scopeStarted = false;
        switch (mapEvent.Kind)
        {
            case PacketMapEventKind.CurrentMap:
                scopeStarted = context.Sink.SetCurrentMap(in source, mapEvent.Id);
                break;
            case PacketMapEventKind.Candidate:
                scopeStarted = context.Sink.StageMapCandidate(in source, mapEvent.Id);
                break;
            case PacketMapEventKind.Arrival:
                scopeStarted = context.Sink.ConfirmDestinationMapArrival(in source);
                break;
            case PacketMapEventKind.InstanceRegistered:
                context.Sink.RegisterMapEvent(in source, mapEvent.Id);
                break;
            case PacketMapEventKind.InstanceUnregistered:
                context.Sink.UnregisterMapEvent(in source, mapEvent.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mapEvent));
        }
        if (scopeStarted) context.ResetPlayerGroupStatus();
        return context.MarkParsed();
    }
}
