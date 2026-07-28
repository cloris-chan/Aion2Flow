using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class Packet2F92Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out uint instanceId)
    {
        instanceId = 0;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _) || reader.Remaining != 6)
            return false;
        if (packet[reader.Offset] != 0x2F || packet[reader.Offset + 1] != 0x92)
            return false;
        reader.TryAdvance(2);

        instanceId = BinaryPrimitives.ReadUInt32LittleEndian(packet[reader.Offset..]);
        return instanceId != 0;
    }
}
