using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4536PcMetadata(int EntityId, string Nickname, int NicknameLength, int TailOffset, int ClassCode, byte FactionCode, int OriginServerId, string LegionName);

internal static class Packet4536PcMetadataParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4536PcMetadata result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        return TryParsePayload(packet[reader.Offset..], out result);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet4536PcMetadata result)
    {
        result = default;

        var reader = new PacketSpanReader(payload);
        if (reader.Remaining < 2) return false;
        if (payload[reader.Offset] != 0x45 || payload[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var entityId)) return false;

        if (!TryReadNamePrefix(payload, ref reader))
        {
            return false;
        }

        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(payload, reader.Offset, out var sanitizedName, out var nicknameLength, out var tailOffset))
        {
            return false;
        }

        var classCode = NicknameParserUtil.TryReadClassCode(payload, tailOffset);
        if (classCode is not { } code)
        {
            return false;
        }

        var factionCode = NicknameParserUtil.TryReadFactionCode(payload, tailOffset + sizeof(int));
        var postClassOffset = tailOffset + sizeof(int) + 1;
        TryReadIdentity(payload, postClassOffset, out var originServerId, out var legionName, out var identityTailOffset);
        result = new Packet4536PcMetadata(entityId, sanitizedName, nicknameLength, identityTailOffset, code, factionCode, originServerId, legionName);
        return true;
    }

    private static bool TryReadNamePrefix(ReadOnlySpan<byte> payload, ref PacketSpanReader reader)
    {
        while (reader.Remaining > 0)
        {
            if (reader.RemainingSpan[0] is 0x07 or 0x17 &&
                NicknameParserUtil.TryReadLengthPrefixedNickname(payload, reader.Offset + 1, out _, out _, out var tailOffset) &&
                NicknameParserUtil.TryReadClassCode(payload, tailOffset) is not null)
            {
                return reader.TryAdvance(1);
            }

            if (!reader.TryReadVarInt(out _))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadIdentity(ReadOnlySpan<byte> payload, int postClassOffset, out int originServerId, out string legionName, out int identityTailOffset)
    {
        originServerId = 0;
        legionName = string.Empty;
        identityTailOffset = postClassOffset;

        var reader = new PacketSpanReader(payload[postClassOffset..]);
        if (!TryReadProfileStateHeader(ref reader))
            return false;

        while (reader.Remaining > 0)
        {
            var offset = postClassOffset + reader.Offset;
            if (TryReadLegionIdentity(payload, offset, out originServerId, out legionName, out identityTailOffset))
            {
                return true;
            }

            if (TryReadServerOnlyIdentity(payload, offset, out originServerId, out identityTailOffset))
            {
                return true;
            }

            if (TryReadServerOnlyMarker(payload, offset, out _))
            {
                return false;
            }

            if (!TryReadVarIntWide(ref reader, out _))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadProfileStateHeader(ref PacketSpanReader reader)
    {
        if (!reader.TryReadByte(out var kind) || kind is not (1 or 2))
        {
            return false;
        }

        if (!reader.TryReadByte(out var flags))
        {
            return false;
        }

        if (!TryReadProfileStateHeaderBody(ref reader))
        {
            return false;
        }

        return ((ProfileStateFlags)flags & ProfileStateFlags.HeaderExtension) == 0 || TryReadProfileStateHeaderExtension(ref reader);
    }

    private static bool TryReadProfileStateHeaderBody(ref PacketSpanReader reader)
    {
        return reader.TryReadByte(out _) &&
            reader.TryReadUInt32Le(out _) &&
            reader.TryReadUInt32Le(out _) &&
            reader.TryReadUInt32Le(out _) &&
            reader.TryReadUInt32Le(out _);
    }

    private static bool TryReadProfileStateHeaderExtension(ref PacketSpanReader reader)
    {
        return reader.TryReadUInt32Le(out _) &&
            reader.TryReadUInt32Le(out _) &&
            reader.TryReadUInt32Le(out _);
    }

    private static bool TryReadLegionIdentity(ReadOnlySpan<byte> payload, int offset, out int originServerId, out string legionName, out int identityTailOffset)
    {
        originServerId = 0;
        legionName = string.Empty;
        identityTailOffset = 0;

        var reader = new PacketSpanReader(payload[offset..]);
        if (!reader.TryReadUInt16Le(out var serverId) || !IsOriginServerId(serverId))
        {
            return false;
        }

        if (!reader.TryReadUInt32Le(out _) || !reader.TryReadUInt16Le(out _))
        {
            return false;
        }

        if (!reader.TryReadUInt16Le(out var repeatedServerId) || repeatedServerId != serverId)
        {
            return false;
        }

        if (!NicknameParserUtil.TryReadLengthPrefixedIdentityText(payload, offset + reader.Offset, out var parsedLegionName, out _, out var textTailOffset))
        {
            return false;
        }

        if (!TryReadIdentityTextTrailer(payload, textTailOffset, out identityTailOffset))
        {
            return false;
        }

        originServerId = serverId;
        legionName = parsedLegionName;
        return true;
    }

    private static bool TryReadIdentityTextTrailer(ReadOnlySpan<byte> payload, int offset, out int tailOffset)
    {
        tailOffset = 0;
        if ((uint)offset >= (uint)payload.Length || payload[offset] is not (1 or 2))
        {
            return false;
        }

        if (offset + 4 <= payload.Length && payload[offset + 1] == 0x00 && payload[offset + 2] <= 0x02 && payload[offset + 3] == 0x00)
        {
            tailOffset = offset + 4;
            return true;
        }

        if (offset + 6 <= payload.Length && payload[offset + 2] == 0x00 && payload[offset + 3] == 0x08 && payload[offset + 4] == 0x02 && payload[offset + 5] == 0x00)
        {
            tailOffset = offset + 6;
            return true;
        }

        if (offset + 8 <= payload.Length && payload[offset + 1] == 0x00 && payload[offset + 2] == 0x02 && payload[offset + 4] == 0x00 && payload[offset + 5] == 0x06 && payload[offset + 6] == 0x02 && payload[offset + 7] == 0x00)
        {
            tailOffset = offset + 8;
            return true;
        }

        return false;
    }

    private static bool TryReadServerOnlyIdentity(ReadOnlySpan<byte> payload, int offset, out int originServerId, out int identityTailOffset)
    {
        originServerId = 0;
        identityTailOffset = 0;

        var reader = new PacketSpanReader(payload[offset..]);
        if (!reader.TryReadUInt16Le(out var serverId) || !IsOriginServerId(serverId))
        {
            return false;
        }

        if (!TryReadServerOnlyTrailer(payload, offset + reader.Offset, out identityTailOffset))
        {
            return false;
        }

        originServerId = serverId;
        return true;
    }

    private static bool TryReadServerOnlyTrailer(ReadOnlySpan<byte> payload, int offset, out int tailOffset)
    {
        tailOffset = 0;
        var reader = new PacketSpanReader(payload[offset..]);
        var secondMode = 0;
        var index = 0;

        while (reader.Remaining > 0)
        {
            var readerOffset = offset + reader.Offset;
            if (TryReadServerOnlyMarker(payload, readerOffset, out tailOffset))
            {
                return index >= 3;
            }

            if (!TryReadVarIntWide(ref reader, out var value))
            {
                return false;
            }

            if (index == 0 && (value is < 3 or > 60))
            {
                return false;
            }

            if (index == 1)
            {
                if (value == 1)
                {
                    secondMode = 1;
                }
                else if (value is >= 0x10 and <= 0x13)
                {
                    secondMode = 2;
                }
                else
                {
                    return false;
                }
            }

            if (index == 2 && secondMode == 1 && value != 3)
            {
                return false;
            }

            index++;
        }

        return false;
    }

    private static bool TryReadServerOnlyMarker(ReadOnlySpan<byte> payload, int offset, out int tailOffset)
    {
        tailOffset = 0;
        if (offset + 16 <= payload.Length &&
            payload[offset] == 0xff &&
            payload[offset + 1] == 0xff &&
            payload[offset + 2] == 0xff &&
            payload[offset + 3] == 0xff &&
            payload[offset + 4] == 0xff &&
            payload[offset + 5] == 0xff &&
            payload[offset + 6] == 0xff &&
            payload[offset + 7] == 0xff &&
            payload[offset + 8] == 0x80 &&
            payload[offset + 9] == 0x75 &&
            payload[offset + 10] == 0xd5 &&
            payload[offset + 11] == 0x2a &&
            payload[offset + 12] == 0xbb &&
            payload[offset + 13] == 0x03 &&
            payload[offset + 14] == 0x00 &&
            payload[offset + 15] == 0x00)
        {
            tailOffset = offset + 16;
            return true;
        }

        if (offset + 5 <= payload.Length &&
            payload[offset] == 0xff &&
            payload[offset + 1] == 0x01 &&
            payload[offset + 2] == 0x00 &&
            TryReadVarIntWide(payload, offset + 3, out _, out var trailerByteCount))
        {
            var markerOffset = offset + 3 + trailerByteCount;
            if (markerOffset + 8 <= payload.Length &&
                payload[markerOffset] == 0x80 &&
                payload[markerOffset + 1] == 0x75 &&
                payload[markerOffset + 2] == 0xd5 &&
                payload[markerOffset + 3] == 0x2a &&
                payload[markerOffset + 4] == 0xbb &&
                payload[markerOffset + 5] == 0x03 &&
                payload[markerOffset + 6] == 0x00 &&
                payload[markerOffset + 7] == 0x00)
            {
                tailOffset = markerOffset + 8;
                return true;
            }
        }

        return false;
    }

    private static bool IsOriginServerId(int value) => value is >= 1000 and < 3000;

    private static bool TryReadVarIntWide(ref PacketSpanReader reader, out int value)
    {
        value = 0;
        ulong wideValue = 0;
        var shift = 0;
        while (reader.Remaining > 0 && shift < sizeof(ulong) * 8)
        {
            if (!reader.TryReadByte(out var byteValue))
            {
                return false;
            }

            wideValue |= (ulong)(byteValue & 0x7f) << shift;
            if ((byteValue & 0x80) == 0)
            {
                value = wideValue > int.MaxValue ? int.MaxValue : (int)wideValue;
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    private static bool TryReadVarIntWide(ReadOnlySpan<byte> payload, int offset, out int value, out int byteCount)
    {
        value = 0;
        byteCount = 0;
        if ((uint)offset >= (uint)payload.Length)
        {
            return false;
        }

        ulong wideValue = 0;
        var shift = 0;
        while (byteCount < 10)
        {
            if (offset + byteCount >= payload.Length)
            {
                return false;
            }

            var byteValue = payload[offset + byteCount];
            wideValue |= (ulong)(byteValue & 0x7f) << shift;
            byteCount++;
            if ((byteValue & 0x80) == 0)
            {
                value = wideValue > int.MaxValue ? int.MaxValue : (int)wideValue;
                return true;
            }

            shift += 7;
        }

        value = 0;
        byteCount = 0;
        return false;
    }

    [Flags]
    private enum ProfileStateFlags : byte
    {
        HeaderExtension = 1 << 3
    }
}
