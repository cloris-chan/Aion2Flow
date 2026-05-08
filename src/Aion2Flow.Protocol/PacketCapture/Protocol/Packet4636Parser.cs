using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4636State(
    int SourceId,
    byte State0,
    byte State1,
    int TailLength);

internal static class Packet4636Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4636State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x46 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadByte(out var state0)) return false;
        if (!reader.TryReadByte(out var state1)) return false;

        result = new Packet4636State(sourceId, state0, state1, reader.Remaining);
        return true;
    }
}
