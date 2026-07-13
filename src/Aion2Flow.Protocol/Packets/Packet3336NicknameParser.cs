using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet3336Nickname(int PlayerId, string Nickname, int NicknameLength, int TailOffset, int? OriginServerId, byte FactionCode, string LegionName);

internal static class Packet3336NicknameParser
{
    private const byte CurrentNamePrefix = 0x5f;
    private const byte CurrentContinuedNamePrefix = 0xdf;
    private const byte CurrentExtendedNamePrefix = 0x5e;
    private const byte CurrentNameMarker = 0x37;
    private const byte CurrentCrossServerNameMarker = 0x3f;

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
        if (!TryReadNamePreamble(ref reader)) return false;
        if (!NicknameParserUtil.TryReadLengthPrefixedNickname(payload, reader.Offset, out var sanitizedName, out var nicknameLength, out var tailOffset)) return false;
        if (!NicknameParserUtil.TryReadOriginServerIdLe16(payload, tailOffset, out var originServerId)) return false;

        var postServerOffset = tailOffset + sizeof(ushort);
        if (postServerOffset + sizeof(int) + 1 > payload.Length) return false;

        var directFactionCode = NicknameParserUtil.TryReadFactionCode(payload, postServerOffset + sizeof(int));
        var factionCode = NicknameParserUtil.SelectFactionCode(directFactionCode, originServerId);
        var identityTailOffset = postServerOffset + sizeof(int) + 1;
        result = new Packet3336Nickname(playerId, sanitizedName, nicknameLength, identityTailOffset, originServerId, factionCode, string.Empty);
        return true;
    }

    private static bool TryReadNamePreamble(ref PacketSpanReader reader)
    {
        if (!reader.TryReadByte(out var prefix))
        {
            return false;
        }

        switch (prefix)
        {
            case CurrentNamePrefix:
            case CurrentContinuedNamePrefix:
                break;
            case CurrentExtendedNamePrefix:
                if (!reader.TryReadByte(out _))
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        return reader.TryReadVarInt(out _) &&
            reader.TryReadByte(out var marker) &&
            marker is CurrentNameMarker or CurrentCrossServerNameMarker;
    }
}
