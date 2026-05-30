using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet3336Nickname(int PlayerId, string Nickname, int NicknameLength, int TailOffset, int? OriginServerId, int? ClassCode, byte FactionCode);

internal static class Packet3336NicknameParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet3336Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        return TryParsePayload(packet[reader.Offset..], out result);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet3336Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(payload);
        if (reader.Remaining < 2) return false;
        if (payload[reader.Offset] != 0x33 || payload[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var playerId)) return false;
        var searchEnd = Math.Min(reader.Offset + 10, payload.Length - 1);
        for (var markerOffset = reader.Offset; markerOffset < searchEnd; markerOffset++)
        {
            if (payload[markerOffset] is not (0x07 or 0x0f))
            {
                continue;
            }

            if (!NicknameParserUtil.TryReadLengthPrefixedNickname(payload, markerOffset + 1, strict: true, out var sanitizedName, out var nicknameLength, out var tailOffset))
            {
                continue;
            }

            var originServerLength = NicknameParserUtil.TryReadPossibleOriginServerAt(payload, tailOffset, out var originServerId, out var originLength)
                ? originLength
                : 0;
            var classCode = NicknameParserUtil.TryReadClassCode(payload, tailOffset + originServerLength);
            var factionCode = NicknameParserUtil.TryReadFactionCode(payload, tailOffset + originServerLength + 4);
            result = new Packet3336Nickname(playerId, sanitizedName, nicknameLength, tailOffset, originServerLength == 0 ? null : originServerId, classCode, factionCode);
            return true;
        }

        return false;
    }
}
