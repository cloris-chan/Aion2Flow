using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct PacketMapEventState(uint MapId);

internal static class PacketMapEventParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketMapEventState result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;

        var opcode0 = packet[reader.Offset];
        var opcode1 = packet[reader.Offset + 1];
        reader.TryAdvance(2);

        var body = packet[reader.Offset..];
        var mapIdOffset = (opcode0, opcode1) switch
        {
            (0x00, 0x61) => 0,
            (0x01, 0x61) => 1,
            _ => -1
        };

        if (mapIdOffset < 0 || body.Length < mapIdOffset + 4)
        {
            return false;
        }

        var mapId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(mapIdOffset, 4));
        if (mapId == 0)
        {
            return false;
        }

        result = new PacketMapEventState(mapId);
        return true;
    }
}
