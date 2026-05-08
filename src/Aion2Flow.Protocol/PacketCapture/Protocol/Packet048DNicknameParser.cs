using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet048DNickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int TailOffset,
    int? OriginServerId);

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

        var fieldOffset = reader.Offset;
        if (TryParseOriginPrefixedNickname(payload, playerId, fieldOffset, out result))
        {
            return true;
        }

        return TryParseDirectNickname(payload, playerId, fieldOffset, out result);
    }

    private static bool TryParseOriginPrefixedNickname(
        ReadOnlySpan<byte> payload,
        int playerId,
        int originOffset,
        out Packet048DNickname result)
    {
        result = default;

        if (!NicknameParserUtil.TryReadPossibleOriginServerAt(payload, originOffset, out var originServerId, out var originLength))
        {
            return false;
        }

        var nameLengthOffset = originOffset + originLength;
        if (!TryReadValidatedNickname(payload, nameLengthOffset, out var nickname, out var nicknameLength, out var tailOffset))
        {
            return false;
        }

        result = new Packet048DNickname(playerId, nickname, nicknameLength, tailOffset, originServerId);
        return true;
    }

    private static bool TryParseDirectNickname(
        ReadOnlySpan<byte> payload,
        int playerId,
        int nameLengthOffset,
        out Packet048DNickname result)
    {
        result = default;

        if (!TryReadValidatedNickname(payload, nameLengthOffset, out var nickname, out var nicknameLength, out var tailOffset))
        {
            return false;
        }

        result = new Packet048DNickname(playerId, nickname, nicknameLength, tailOffset, OriginServerId: null);
        return true;
    }

    private static bool TryReadValidatedNickname(
        ReadOnlySpan<byte> payload,
        int nameLengthOffset,
        out string nickname,
        out int nicknameLength,
        out int tailOffset)
    {
        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(
                payload,
                nameLengthOffset,
                strict: true,
                out nickname,
                out nicknameLength,
                out var nicknameTailOffset))
        {
            tailOffset = 0;
            return false;
        }

        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(
            payload,
            nicknameTailOffset,
            strict: true,
            out _,
            out _,
            out tailOffset))
        {
            tailOffset = 0;
            return false;
        }

        return true;
    }
}
