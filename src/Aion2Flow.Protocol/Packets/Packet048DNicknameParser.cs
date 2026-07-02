using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet048DNickname(int PlayerId, string Nickname, int NicknameLength, int TailOffset, int? OriginServerId, byte FactionCode, string LegionName);

internal static class Packet048DNicknameParser
{
    private const int HeaderLengthBeforePlayerId = 9;

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet048DNickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        var payloadOffset = reader.Offset;

        if (!TryParsePayload(packet[payloadOffset..], out var payloadResult))
        {
            return false;
        }

        result = payloadResult with { TailOffset = payloadOffset + payloadResult.TailOffset };
        return true;
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet048DNickname result)
    {
        result = default;

        if (payload.Length < 11 || payload[0] != 0x04 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(HeaderLengthBeforePlayerId)) return false;
        if (!reader.TryReadVarInt(out var playerId)) return false;

        if (NicknameParserUtil.TryReadOriginServerIdLe16(payload, reader.Offset, out var serverId) &&
            TryParseNicknameAt(payload, reader.Offset + sizeof(ushort), serverId, playerId, out result))
        {
            return true;
        }

        return TryParseNicknameAt(payload, reader.Offset, originServerId: null, playerId, out result);
    }

    private static bool TryParseNicknameAt(ReadOnlySpan<byte> payload, int nameLengthOffset, int? originServerId, int playerId, out Packet048DNickname result)
    {
        result = default;
        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(payload, nameLengthOffset, out var nickname, out var nicknameLength, out var tailOffset))
        {
            return false;
        }

        var directFactionCode = NicknameParserUtil.TryReadFactionCode(payload, tailOffset + 7);
        var legionName = string.Empty;
        var identityTailOffset = tailOffset;
        if (PacketIdentityTrailerParser.TryReadNicknameAdjacentLegionBlock(payload, tailOffset, out var parsedLegionName, out var parsedFactionCode, out var legionTailOffset))
        {
            legionName = parsedLegionName;
            directFactionCode = parsedFactionCode;
            identityTailOffset = legionTailOffset;
        }

        result = new Packet048DNickname(playerId, nickname, nicknameLength, identityTailOffset, originServerId, NicknameParserUtil.SelectFactionCode(directFactionCode, originServerId), legionName);
        return true;
    }
}
