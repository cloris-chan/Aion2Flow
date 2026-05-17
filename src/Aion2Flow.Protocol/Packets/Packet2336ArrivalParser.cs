using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class Packet2336ArrivalParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x23 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        return reader.Remaining >= 20;
    }
}
