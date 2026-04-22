using System.Buffers.Binary;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4036State(
    Packet4036Kind Kind,
    Packet4036LayoutKind LayoutKind,
    int PayloadLength,
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

        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);
        var layoutKind = Packet4036Descriptors.ClassifyLayout(kind, body.Length, body[0], body[1], body[2]);
        var useState97Layout = kind == Packet4036Kind.State97;
        var sharedOffset = GetShared852100Offset(layoutKind);
        var heavyOffset = GetHeavy852100Offset(layoutKind);
        var linkedValue = useState97Layout ? ReadRepeatedVarInt(body, 28) : 0;
        var gauge0 = useState97Layout ? ReadUInt32Le(body, 34) : 0;
        var gauge1 = useState97Layout ? ReadUInt32Le(body, 38) : 0;
        var tailOffset = useState97Layout ? Math.Max(body.Length - 15, 0) : -1;
        result = new Packet4036State(
            kind,
            layoutKind,
            packet.Length,
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

    private static int GetShared852100Offset(Packet4036LayoutKind layoutKind)
    {
        return layoutKind switch
        {
            Packet4036LayoutKind.State97Outlier852100 => 27,
            Packet4036LayoutKind.State120Main852100 => 39,
            _ => -1
        };
    }

    private static int GetHeavy852100Offset(Packet4036LayoutKind layoutKind)
    {
        return layoutKind switch
        {
            Packet4036LayoutKind.State152Main852100 => 27,
            _ => -1
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
