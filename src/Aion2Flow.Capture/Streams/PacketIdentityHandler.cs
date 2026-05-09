using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketIdentityHandler
{
    public static bool ParseOwnNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet3336NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _))
        {
            return false;
        }

        var tailOffset = Math.Min(packet.Length, reader.Offset + parsed.TailOffset);
        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
        context.Sink.MarkSceneArrival();
        RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|kind=own|len={parsed.NicknameLength}{PacketDiagnosticFormatter.OriginServerHint(parsed.OriginServerId)}", packet[..tailOffset]);
        return context.MarkParsed();
    }

    public static bool ParseOtherNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (Packet4436NicknameParser.TryParse(packet, out var parsed))
        {
            context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
            RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|kind=other|len={parsed.NicknameLength}|delta={parsed.Delta}{PacketDiagnosticFormatter.OriginServerHint(parsed.OriginServerId)}", packet);
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

        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname);
        RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|kind=roster|len={parsed.NicknameLength}", packet[..parsed.TailOffset]);
        return context.MarkParsed();
    }

    public static bool ParseNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
        RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|len={parsed.NicknameLength}{PacketDiagnosticFormatter.OriginServerHint(parsed.OriginServerId)}", packet[..parsed.TailOffset]);
        return context.MarkParsed();
    }
}
