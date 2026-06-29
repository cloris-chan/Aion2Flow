using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet0238CompactControl(
    int SourceId,
    int Mode,
    uint BodyCodeRaw,
    int Marker,
    int Flag,
    int EchoSourceId,
    int TailLength,
    int TailFirstValue,
    int TailSecondValue,
    int TailThirdValue);

internal static class Packet0238CompactControlParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0238CompactControl result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length != packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x02 || packet[reader.Offset + 1] != 0x38) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadUInt32Le(out var bodyCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadByte(out var flag)) return false;
        if (!reader.TryReadVarInt(out var echoSourceId)) return false;
        var tailLength = reader.Remaining;
        ParseZeroPrefixedTail(reader.RemainingSpan, out var tailFirstValue, out var tailSecondValue, out var tailThirdValue);
        if (!reader.TryAdvance(reader.Remaining)) return false;

        result = new Packet0238CompactControl(
            sourceId,
            mode,
            unchecked((uint)bodyCodeRaw),
            marker,
            flag,
            echoSourceId,
            tailLength,
            tailFirstValue,
            tailSecondValue,
            tailThirdValue);
        return true;
    }

    public static int ResolveZeroPrefixedTailLength(ReadOnlySpan<byte> tail)
    {
        if (!TryParseZeroPrefixedTail(tail, requireFullConsumption: false, out var length, out _, out _, out _))
            return 0;

        return length;
    }

    private static void ParseZeroPrefixedTail(ReadOnlySpan<byte> tail, out int firstValue, out int secondValue, out int thirdValue)
    {
        if (!TryParseZeroPrefixedTail(tail, requireFullConsumption: true, out _, out firstValue, out secondValue, out thirdValue))
        {
            firstValue = 0;
            secondValue = 0;
            thirdValue = 0;
        }
    }

    private static bool TryParseZeroPrefixedTail(
        ReadOnlySpan<byte> tail,
        bool requireFullConsumption,
        out int consumed,
        out int firstValue,
        out int secondValue,
        out int thirdValue)
    {
        consumed = 0;
        firstValue = 0;
        secondValue = 0;
        thirdValue = 0;

        var reader = new PacketSpanReader(tail);
        if (!reader.TryReadUInt32Le(out var zeroValue) || zeroValue != 0)
            return false;

        if (!reader.TryReadVarInt(out firstValue) || !reader.TryReadVarInt(out secondValue) || !reader.TryReadVarInt(out thirdValue))
            return false;

        if (requireFullConsumption && reader.Remaining != 0)
            return false;

        consumed = reader.Offset;
        return true;
    }
}
