using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Identity;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketIdentityHandler
{
    public static bool ParseOwnNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet3336NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x3336, packet.Length), parsed.PlayerId, parsed.Nickname, PacketFactionMapper.ToFaction(parsed.FactionCode), isLocalPlayer: true, originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
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

    public static bool ParseLegionMetadataPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2933LegionMetadataParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x2933, packet.Length), parsed.EntityId, string.Empty, PacketFactionMapper.ToFaction(parsed.FactionCode), originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }

    public static bool ParsePartyProfilePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketPlayerGroupParser.TryParsePartyProfile(packet, out var parsed))
        {
            return false;
        }

        var source = context.CreateObservationSource(0x0D92, packet.Length);
        context.Sink.AppendPlayerGroupMember(in source, parsed.EntityId, PlayerGroupMembership.Party(parsed.MemberSlotIndex));
        return context.MarkParsed();
    }

    public static bool ParseForceMemberPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketPlayerGroupParser.TryParseForceMember(packet, out var parsed))
        {
            return false;
        }

        var source = context.CreateObservationSource(0x1D96, packet.Length);
        context.Sink.AppendPlayerGroupMember(in source, parsed.EntityId, PlayerGroupMembership.Force(parsed.GroupId, parsed.SubPartyIndex, parsed.MemberSlotIndex));
        return context.MarkParsed();
    }

    public static bool ParseForceMemberListPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        Span<PacketPlayerGroupMember> members = stackalloc PacketPlayerGroupMember[32];
        var count = PacketPlayerGroupParser.ParseMemberList(packet, members);
        if (count == 0)
        {
            return false;
        }

        var source = context.CreateObservationSource(0x1E96, packet.Length);
        for (var i = 0; i < count; i++)
        {
            var parsed = members[i];
            var membership = parsed.Kind == PacketPlayerGroupKind.Force
                ? PlayerGroupMembership.Force(parsed.GroupId, parsed.SubPartyIndex, parsed.MemberSlotIndex)
                : PlayerGroupMembership.Party(parsed.MemberSlotIndex);
            context.Sink.AppendPlayerGroupMember(in source, parsed.EntityId, in membership);
        }

        return context.MarkParsed();
    }

    public static bool ParseNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x048D, packet.Length), parsed.PlayerId, parsed.Nickname, PacketFactionMapper.ToFaction(parsed.FactionCode), originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }
}
