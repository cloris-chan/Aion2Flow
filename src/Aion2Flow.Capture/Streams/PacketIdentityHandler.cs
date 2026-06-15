using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketIdentityHandler
{
    public static bool ParseOwnNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet3336NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x3336, packet.Length), parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode), PacketCharacterClassMapper.ToCharacterClass(parsed.ClassCode), isLocalPlayer: true);
        return context.MarkParsed();
    }

    public static bool ParseOtherNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (Packet4436NicknameParser.TryParse(packet, out var parsed))
        {
            context.Sink.AppendNickname(context.CreateObservationSource(0x4436, packet.Length), parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode), PacketCharacterClassMapper.ToCharacterClass(parsed.ClassCode));
            return context.MarkParsed();
        }

        return false;
    }

    public static bool Parse0994NicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0994NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x0994, packet.Length), parsed.PlayerId, parsed.Nickname);
        return context.MarkParsed();
    }

    public static bool ParseNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x048D, packet.Length), parsed.PlayerId, parsed.Nickname, parsed.OriginServerId, PacketFactionMapper.ToFaction(parsed.FactionCode));
        return context.MarkParsed();
    }
}
