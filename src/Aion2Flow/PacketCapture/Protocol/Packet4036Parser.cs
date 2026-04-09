using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4036State(
    string Family,
    string Layout,
    int SourceId,
    byte Mode0,
    byte Mode1,
    byte Mode2,
    uint Seed,
    ushort Tag,
    uint P0,
    uint P1,
    uint P2,
    uint Marker,
    int Repeat0,
    int Repeat1,
    int LinkedValue,
    uint Gauge0,
    uint Gauge1,
    uint Tail0,
    uint Tail1,
    byte TailMode,
    byte TailState,
    byte TailFlag0,
    byte TailFlag1,
    uint TailValue,
    uint TailHash,
    byte TailTerminator,
    uint SharedTag,
    uint SharedGauge0,
    uint SharedGauge1,
    uint SharedGauge2,
    uint SharedGauge3,
    uint SharedFlag,
    uint SharedMini0,
    uint SharedMini1,
    uint HeavyGauge0,
    uint HeavyGauge1,
    uint HeavyValue0,
    uint HeavyValue1,
    uint HeavyFlag,
    uint HeavyMini0,
    uint HeavySentinel0,
    uint HeavySentinel1,
    uint HeavyTrailer0,
    uint HeavyTrailer1,
    int BodyLength);

internal static class Packet4036Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4036State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x40 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        var body = packet[reader.Offset..];
        if (body.Length < 33) return false;

        var family = ClassifyFamily(packet.Length);
        var layout = ClassifyLayout(family, body.Length, body[0], body[1], body[2]);
        var useState97Layout = string.Equals(family, "state-97", StringComparison.Ordinal);
        var sharedOffset = GetShared852100Offset(layout);
        var heavyOffset = GetHeavy852100Offset(layout);
        var linkedValue = useState97Layout ? ReadRepeatedVarInt(body, 28) : 0;
        var gauge0 = useState97Layout ? ReadUInt32Le(body, 34) : 0;
        var gauge1 = useState97Layout ? ReadUInt32Le(body, 38) : 0;
        var tailOffset = useState97Layout ? Math.Max(body.Length - 15, 0) : -1;
        result = new Packet4036State(
            family,
            layout,
            sourceId,
            body[0],
            body[1],
            body[2],
            ReadUInt32Le(body, 3),
            ReadUInt16Le(body, 7),
            ReadUInt32Le(body, 9),
            ReadUInt32Le(body, 13),
            ReadUInt32Le(body, 17),
            ReadUInt32Le(body, 21),
            ReadVarIntAt(body, 25),
            ReadVarIntAt(body, 28),
            linkedValue,
            gauge0,
            gauge1,
            ReadUInt32Le(body, Math.Max(body.Length - 8, 0)),
            ReadUInt32Le(body, Math.Max(body.Length - 4, 0)),
            useState97Layout ? ReadByteAt(body, tailOffset) : (byte)0,
            useState97Layout ? ReadByteAt(body, tailOffset + 2) : (byte)0,
            useState97Layout ? ReadByteAt(body, tailOffset + 3) : (byte)0,
            useState97Layout ? ReadByteAt(body, tailOffset + 4) : (byte)0,
            useState97Layout ? ReadUInt32Le(body, tailOffset + 6) : 0,
            useState97Layout ? ReadUInt32Le(body, tailOffset + 10) : 0,
            useState97Layout ? ReadByteAt(body, tailOffset + 14) : (byte)0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 1) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 5) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 9) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 21) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 25) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 29) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 49) : 0,
            sharedOffset >= 0 ? ReadUInt32Le(body, sharedOffset + 53) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 7) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 11) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 23) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 27) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 31) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 51) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 59) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 63) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 67) : 0,
            heavyOffset >= 0 ? ReadUInt32Le(body, heavyOffset + 71) : 0,
            body.Length);
        return true;
    }

    private static int GetShared852100Offset(string layout)
    {
        return layout switch
        {
            "state97-outlier-852100" => 27,
            "state120-main-852100" => 39,
            _ => -1
        };
    }

    private static int GetHeavy852100Offset(string layout)
    {
        return layout switch
        {
            "state152-main-852100" => 27,
            _ => -1
        };
    }

    private static string ClassifyFamily(int payloadLength)
    {
        return payloadLength switch
        {
            >= 175 => "create-177",
            >= 150 => "state-152",
            >= 135 => "state-137",
            >= 118 => "state-120",
            >= 110 => "state-113",
            >= 95 => "state-97",
            _ => $"state-{payloadLength}"
        };
    }

    private static string ClassifyLayout(string family, int bodyLength, byte mode0, byte mode1, byte mode2)
    {
        return (family, bodyLength, mode0, mode1, mode2) switch
        {
            ("state-97", 92, 0x0C, 0x20, 0x00) => "state97-main-0c2000",
            ("state-97", 93, 0x0C, 0x20, 0x00) => "state97-main-0c2000",
            ("state-97", 93, 0x0D, 0x20, 0x00) => "state97-main-0d2000",
            ("state-97", 94, 0x0D, 0x20, 0x00) => "state97-variant-0d2000",
            ("state-97", 94, 0x0F, 0x20, 0x00) => "state97-variant-0f2000",
            ("state-97", 102, 0x85, 0x21, 0x00) => "state97-outlier-852100",
            ("state-113", 105, 0x0D, 0x20, 0x00) => "state113-main-0d2000",
            ("state-120", 114, 0x85, 0x21, 0x00) => "state120-main-852100",
            ("state-120", 114, 0x0C, 0x20, 0x00) => "state120-main-0c2000",
            ("state-120", 126, 0x0C, 0x20, 0x00) => "state120-wide-0c2000",
            ("state-120", 126, 0x85, 0x21, 0x00) => "state120-wide-852100",
            ("state-137", 128, 0x0F, 0x20, 0x00) => "state137-main-0f2000",
            ("state-137", 130, 0x0C, 0x22, 0x00) => "state137-main-0c2200",
            ("state-137", 130, 0x0C, 0x20, 0x00) => "state137-main-0c2000",
            ("state-137", 130, 0x0C, 0x30, 0x00) => "state137-variant-0c3000",
            ("state-137", 131, 0x0D, 0x20, 0x00) => "state137-variant-0d2000",
            ("state-137", 132, 0x07, 0x20, 0x00) => "state137-main-072000",
            ("state-137", 142, 0x0C, 0x22, 0x00) => "state137-wide-0c2200",
            ("state-152", 143, 0x85, 0x21, 0x00) => "state152-main-852100",
            ("state-152", 148, 0x1F, 0x10, 0x00) => "state152-main-1f1000",
            ("state-152", 153, 0x1F, 0x10, 0x00) => "state152-wide-1f1000",
            _ => $"{family}-body{bodyLength}-{mode0:x2}{mode1:x2}{mode2:x2}"
        };
    }

    private static uint ReadUInt32Le(ReadOnlySpan<byte> buffer, int offset)
    {
        if ((uint)offset > buffer.Length - 4u)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..(offset + 4)]);
    }

    private static ushort ReadUInt16Le(ReadOnlySpan<byte> buffer, int offset)
    {
        if ((uint)offset > buffer.Length - 2u)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..(offset + 2)]);
    }

    private static int ReadVarIntAt(ReadOnlySpan<byte> buffer, int offset)
    {
        if (!TryReadVarInt(buffer, offset, out var value))
        {
            return 0;
        }

        return value;
    }

    private static int ReadRepeatedVarInt(ReadOnlySpan<byte> buffer, int offset)
    {
        if (!TryReadVarInt(buffer, offset, out var first, out var firstLength))
        {
            return 0;
        }

        if (!TryReadVarInt(buffer, offset + firstLength, out var second, out _))
        {
            return 0;
        }

        return first == second ? first : 0;
    }

    private static byte ReadByteAt(ReadOnlySpan<byte> buffer, int offset)
    {
        if ((uint)offset >= (uint)buffer.Length)
        {
            return 0;
        }

        return buffer[offset];
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> buffer, int offset, out int value)
        => TryReadVarInt(buffer, offset, out value, out _);

    private static bool TryReadVarInt(ReadOnlySpan<byte> buffer, int offset, out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        var shift = 0;

        for (var i = 0; i < 5; i++)
        {
            var index = offset + i;
            if ((uint)index >= (uint)buffer.Length)
            {
                return false;
            }

            var current = buffer[index];
            value |= (current & 0x7f) << shift;
            consumed = i + 1;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        consumed = 0;
        return false;
    }
}
