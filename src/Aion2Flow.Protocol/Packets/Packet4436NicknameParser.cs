using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4436Nickname(int PlayerId, string Nickname, int NicknameLength, int Delta, int? ClassCode, int? OriginServerId, byte FactionCode);

internal static class Packet4436NicknameParser
{
    private const int MarkerSearchLimit = 12;

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4436Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        return TryParsePayload(packet[reader.Offset..], out result);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet4436Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(payload);
        if (reader.Remaining < 2) return false;
        if (payload[reader.Offset] != 0x44 || payload[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var playerId)) return false;

        var searchStart = reader.Offset;
        if (TryParseWithMarker(payload, playerId, searchStart, 0x17, out result))
        {
            return true;
        }

        return TryParseWithMarker(payload, playerId, searchStart, 0x07, out result);
    }

    private static bool TryParseWithMarker(ReadOnlySpan<byte> packet, int playerId, int searchStart, byte marker, out Packet4436Nickname result)
    {
        result = default;

        var searchEnd = Math.Min(packet.Length - 1, searchStart + MarkerSearchLimit);

        for (var markerOffset = searchStart; markerOffset < searchEnd; markerOffset++)
        {
            if (packet[markerOffset] != marker)
            {
                continue;
            }

            if (!NicknameParserUtil.TryReadLengthPrefixedNickname(packet, markerOffset + 1, strict: true, out var sanitizedName, out var nicknameLength, out var tailOffset))
            {
                continue;
            }

            var classCode = NicknameParserUtil.TryReadClassCode(packet, tailOffset);
            var factionCode = NicknameParserUtil.TryReadFactionCode(packet, tailOffset + sizeof(int));
            result = new Packet4436Nickname(playerId, sanitizedName, nicknameLength, markerOffset - searchStart, classCode, OriginServerId: null, factionCode);
            return true;
        }

        return false;
    }
}
