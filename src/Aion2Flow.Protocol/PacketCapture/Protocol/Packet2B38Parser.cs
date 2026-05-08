using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2B38Aux(
    int SourceId,
    int SourceIdCopy,
    int Phase,
    int Marker,
    int ActionCode,
    int Sequence,
    int StateValue,
    int DetailValue,
    int TailLength);

internal static class Packet2B38Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2B38Aux result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x2b || packet[reader.Offset + 1] != 0x38) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var phase)) return false;
        if (!reader.TryReadVarInt(out var marker)) return false;
        if (!reader.TryReadUInt32Le(out var actionCode)) return false;
        if (!reader.TryAdvance(8)) return false;
        if (!reader.TryReadUInt32Le(out var sequence)) return false;
        if (!reader.TryReadVarInt(out var sourceIdCopy)) return false;
        if (!reader.TryReadVarInt(out var stateValue)) return false;
        if (!reader.TryReadVarInt(out var detailValue)) return false;

        result = new Packet2B38Aux(
            sourceId,
            sourceIdCopy,
            phase,
            marker,
            actionCode,
            sequence,
            stateValue,
            detailValue,
            reader.Remaining);
        return true;
    }
}
