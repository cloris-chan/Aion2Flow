using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet8456Envelope(
    byte Prefix0,
    byte Prefix1,
    byte Prefix2,
    ushort InnerOpcode,
    string InnerFamily,
    uint InnerValue,
    ulong Stamp,
    byte Trailer,
    int TailLength);

internal static class Packet8456EnvelopeParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet8456Envelope result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x84 || packet[reader.Offset + 1] != 0x56) return false;
        if (!reader.TryAdvance(2)) return false;
        if (reader.Remaining < 17) return false;

        if (!reader.TryReadByte(out var prefix0)) return false;
        if (!reader.TryReadByte(out var prefix1)) return false;
        if (!reader.TryReadByte(out var prefix2)) return false;

        var innerOpcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(reader.Offset, 2));
        if (!reader.TryAdvance(2)) return false;

        var innerValue = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(reader.Offset, 4));
        if (!reader.TryAdvance(4)) return false;

        var stamp = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(reader.Offset, 8));
        if (!reader.TryAdvance(8)) return false;

        if (!reader.TryReadByte(out var trailer)) return false;

        result = new Packet8456Envelope(
            prefix0,
            prefix1,
            prefix2,
            innerOpcode,
            ClassifyInner(innerOpcode),
            innerValue,
            stamp,
            trailer,
            reader.Remaining);
        return true;
    }

    private static string ClassifyInner(ushort innerOpcode)
    {
        return innerOpcode switch
        {
            0x3640 => "4036",
            0x3641 => "4136",
            _ => $"0x{innerOpcode:x4}"
        };
    }
}
