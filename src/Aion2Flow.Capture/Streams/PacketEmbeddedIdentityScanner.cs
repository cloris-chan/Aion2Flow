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
            else if (packet[offset] == 0x44 && packet[offset + 1] == 0x36)
            {
                if (TryParseOtherNicknameAt(packet, offset, ref context, out consumed))
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
        }

        PacketEmbeddedNicknameScanner.Scan(packet, ref context);
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
        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode));
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
        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode), PacketCharacterClassMapper.ToCharacterClass(parsed.ClassCode));
        return context.MarkParsed();
    }

    private static bool TryParseOtherNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet4436NicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = parsed.Delta + parsed.NicknameLength + 2;
        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode), PacketCharacterClassMapper.ToCharacterClass(parsed.ClassCode));
        return context.MarkParsed();
    }
}
