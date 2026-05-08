using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4536State(
    int SourceId,
    byte Value0,
    int TailLength);

internal static class Packet4536Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4536State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x45 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadByte(out var value0)) return false;

        result = new Packet4536State(sourceId, value0, reader.Remaining);
        return true;
    }
}
