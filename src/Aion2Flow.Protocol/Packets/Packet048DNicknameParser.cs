using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet048DNickname(int PlayerId, string Nickname, int NicknameLength, int TailOffset, int? OriginServerId, byte FactionCode);

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

        var nameLengthOffset = reader.Offset;
        int? originServerId = null;
        if (NicknameParserUtil.TryReadPossibleOriginServerAt(payload, nameLengthOffset, out var parsedOriginServerId, out var originLength))
        {
            originServerId = parsedOriginServerId;
            nameLengthOffset += originLength;
        }

        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(
                payload,
                nameLengthOffset,
                strict: true,
                out var nickname,
                out var nicknameLength,
                out var tailOffset))
        {
            return false;
        }

        var factionCode = NicknameParserUtil.TryReadFactionCode(payload, tailOffset + 7);
        result = new Packet048DNickname(playerId, nickname, nicknameLength, tailOffset, originServerId, factionCode);
        return true;
    }
}
