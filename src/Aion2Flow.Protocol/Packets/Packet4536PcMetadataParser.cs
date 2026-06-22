using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4536PcMetadata(int EntityId, string Nickname, int NicknameLength, int TailOffset, int ClassCode, int? OriginServerId, byte FactionCode);

internal static class Packet4536PcMetadataParser
{
    private const int MarkerSearchLimit = 16;

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

        var searchStart = reader.Offset;
        var searchEnd = Math.Min(payload.Length - 1, searchStart + MarkerSearchLimit);
        for (var markerOffset = searchStart; markerOffset < searchEnd; markerOffset++)
        {
            if (payload[markerOffset] != 0x07)
            {
                continue;
            }

            if (!NicknameParserUtil.TryReadLengthPrefixedNickname(payload, markerOffset + 1, strict: true, out var sanitizedName, out var nicknameLength, out var tailOffset))
            {
                continue;
            }

            var classCode = NicknameParserUtil.TryReadClassCode(payload, tailOffset);
            if (classCode is not { } code)
            {
                continue;
            }

            result = new Packet4536PcMetadata(entityId, sanitizedName, nicknameLength, tailOffset, code, OriginServerId: null, NicknameParserUtil.TryReadFactionCode(payload, tailOffset + sizeof(int)));
            return true;
        }

        return false;
    }
}
