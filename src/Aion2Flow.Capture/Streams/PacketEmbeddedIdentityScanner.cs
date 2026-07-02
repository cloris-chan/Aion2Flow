using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketEmbeddedIdentityScanner
{
    public static bool Scan(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var parsed = false;

        for (var offset = 0; offset + 1 < packet.Length; offset++)
        {
            int consumed;
            if (packet[offset] == 0x33 && packet[offset + 1] == 0x36)
            {
                if (TryParseOwnNicknameAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x45 && packet[offset + 1] == 0x36)
            {
                if (TryParsePcMetadataAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x04 && packet[offset + 1] == 0x8d)
            {
                if (TryParseNicknameAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x29 && packet[offset + 1] == 0x33)
            {
                if (TryParseLegionMetadataAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
        }

        return parsed;
    }

    private static bool TryParseNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet048DNicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = parsed.TailOffset;
        context.Sink.AppendNickname(context.CreateObservationSource(0x048D, consumed), parsed.PlayerId, parsed.Nickname, PacketFactionMapper.ToFaction(parsed.FactionCode), originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }

    private static bool TryParseLegionMetadataAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet2933LegionMetadataParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = parsed.TailOffset;
        context.Sink.AppendNickname(context.CreateObservationSource(0x2933, consumed), parsed.EntityId, string.Empty, PacketFactionMapper.ToFaction(parsed.FactionCode), originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }

    private static bool TryParseOwnNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet3336NicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = parsed.TailOffset;
        context.Sink.AppendNickname(context.CreateObservationSource(0x3336, consumed), parsed.PlayerId, parsed.Nickname, PacketFactionMapper.ToFaction(parsed.FactionCode), isLocalPlayer: true, originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }

    private static bool TryParsePcMetadataAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet4536PcMetadataParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = Math.Max(parsed.TailOffset, parsed.NicknameLength + 2);
        context.Sink.AppendNickname(
            context.CreateObservationSource(0x4536, consumed),
            parsed.EntityId,
            parsed.Nickname,
            PacketFactionMapper.ToFaction(parsed.FactionCode),
            PacketCharacterClassMapper.ToCharacterClass(parsed.ClassCode),
            originServerId: parsed.OriginServerId > 0 ? parsed.OriginServerId : null,
            legionName: parsed.LegionName);
        return context.MarkParsed();
    }
}
