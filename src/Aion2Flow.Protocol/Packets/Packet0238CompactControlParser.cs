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
    int TailThirdValue,
    int? AvailableCountAfterControl,
    int? CooldownMilliseconds);

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
        var tail = reader.RemainingSpan;
        ParseZeroPrefixedTail(tail, out var tailFirstValue, out var tailSecondValue, out var tailThirdValue);
        var hasCooldown = TryParseCooldownTail(
            tail,
            out var availableCountAfterControl,
            out var parsedCooldownMilliseconds);
        int? cooldownMilliseconds = hasCooldown ? parsedCooldownMilliseconds : null;
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
            tailThirdValue,
            hasCooldown ? availableCountAfterControl : null,
            cooldownMilliseconds);
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

    private static bool TryParseCooldownTail(
        ReadOnlySpan<byte> tail,
        out int? availableCountAfterControl,
        out int cooldownMilliseconds)
    {
        return TryParseCooldownTail(tail, 4, out availableCountAfterControl, out cooldownMilliseconds) ||
               TryParseCooldownTail(tail, 16, out availableCountAfterControl, out cooldownMilliseconds);
    }

    private static bool TryParseCooldownTail(
        ReadOnlySpan<byte> tail,
        int opaquePrefixLength,
        out int? availableCountAfterControl,
        out int cooldownMilliseconds)
    {
        availableCountAfterControl = null;
        cooldownMilliseconds = 0;
        if (tail.Length <= opaquePrefixLength)
            return false;

        var reader = new PacketSpanReader(tail);
        if (!reader.TryAdvance(opaquePrefixLength))
            return false;

        Span<int> values = stackalloc int[5];
        var valueCount = 0;
        while (reader.Remaining > 0 && valueCount < values.Length)
        {
            if (!TryReadCanonicalVarInt(ref reader, out values[valueCount]))
                return false;

            valueCount++;
        }

        if (reader.Remaining != 0 || valueCount is < 3 or > 5 || values[1] is not 1 and not 2)
        {
            return false;
        }

        if (valueCount > 3 && values[2] != 0)
            return false;

        if (valueCount == 5)
        {
            if (values[3] <= 0)
                return false;
            availableCountAfterControl = values[3];
        }

        cooldownMilliseconds = values[valueCount - 1];
        return true;
    }

    private static bool TryReadCanonicalVarInt(ref PacketSpanReader reader, out int value)
    {
        var offset = reader.Offset;
        if (!reader.TryReadVarInt(out value) || value < 0)
            return false;

        var encodedLength = reader.Offset - offset;
        var remaining = (uint)value;
        var canonicalLength = 1;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            canonicalLength++;
        }

        return encodedLength == canonicalLength;
    }
}
