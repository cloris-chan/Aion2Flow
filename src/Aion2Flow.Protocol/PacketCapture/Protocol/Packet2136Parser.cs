using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2136State(
    uint Sequence,
    uint Value0,
    uint Value1,
    uint Value2,
    uint Value3,
    uint Value4,
    uint Value5,
    uint Value6,
    uint Value7,
    ushort TailMarker,
    int TailLength);

internal static class Packet2136Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2136State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x21 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        var body = packet[reader.Offset..];
        if (body.Length < 30) return false;

        result = new Packet2136State(
            ReadUInt32(body, 0),
            ReadUInt32(body, 4),
            ReadUInt32(body, 8),
            ReadUInt32(body, 12),
            ReadUInt32(body, 16),
            ReadUInt32(body, 20),
            ReadUInt32(body, 24),
            ReadUInt32(body, 28),
            ReadUInt32(body, 32),
            ReadUInt16(body, Math.Max(body.Length - 2, 0)),
            Math.Max(body.Length - 38, 0));
        return true;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset)
    {
        if ((uint)offset > buffer.Length - 4u)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..(offset + 4)]);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset)
    {
        if ((uint)offset > buffer.Length - 2u)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..(offset + 2)]);
    }
}
