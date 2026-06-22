using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet2933LegionMetadata(int EntityId, int OriginServerId, byte FactionCode, string LegionName, int TailOffset);

internal static class Packet2933LegionMetadataParser
{
    private static ReadOnlySpan<byte> HeaderAfterEntityId => [0xfa, 0x02, 0x00, 0x00, 0x00, 0x00];

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2933LegionMetadata result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        return TryParsePayload(packet[reader.Offset..], out result);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet2933LegionMetadata result)
    {
        result = default;

        var reader = new PacketSpanReader(payload);
        if (reader.Remaining < 3 || payload[reader.Offset] != 0x29 || payload[reader.Offset + 1] != 0x33 || payload[reader.Offset + 2] != 0x8a)
        {
            return false;
        }

        reader.TryAdvance(3);
        if (!reader.TryReadVarInt(out var entityId) || entityId <= 0)
        {
            return false;
        }

        var header = HeaderAfterEntityId;
        if (reader.Remaining < header.Length || !payload.Slice(reader.Offset, header.Length).SequenceEqual(header))
        {
            return false;
        }

        reader.TryAdvance(header.Length);
        if (!NicknameParserUtil.TryReadOriginServerIdLe16(payload, reader.Offset, out var originServerId))
        {
            return false;
        }

        reader.TryAdvance(sizeof(ushort));
        if (!NicknameParserUtil.TryReadLengthPrefixedIdentityText(payload, reader.Offset, strict: true, out var legionName, out _, out var tailOffset))
        {
            return false;
        }

        var factionCode = NicknameParserUtil.TryReadFactionCode(payload, tailOffset);
        if (factionCode == 0 || tailOffset + 2 >= payload.Length || payload[tailOffset + 1] != 0x00 || payload[tailOffset + 2] != 0x02)
        {
            return false;
        }

        result = new Packet2933LegionMetadata(entityId, originServerId, factionCode, legionName, tailOffset + 3);
        return true;
    }
}
