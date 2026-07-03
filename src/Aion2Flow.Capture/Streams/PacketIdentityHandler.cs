using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Observation;

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
        var source = context.CreateObservationSource(0x0D92, packet.Length);
        if (PacketPlayerGroupParser.TryParsePartyMember(packet, out var member))
        {
            AppendPlayerGroupMember(context, in source, in member);
            return context.MarkParsed();
        }

        if (!PacketPlayerGroupParser.TryParsePartyProfile(packet, out var profile))
        {
            return false;
        }

        AppendPartyGroupProfile(context, in source, in profile);
        return context.MarkParsed();
    }

    public static bool ParseForceMemberPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketPlayerGroupParser.TryParseForceMember(packet, out var parsed))
        {
            return false;
        }

        var source = context.CreateObservationSource(0x1D96, packet.Length);
        AppendPlayerGroupMember(context, in source, in parsed);
        return context.MarkParsed();
    }

    public static bool ParseForceProfilePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketPlayerGroupParser.TryParseForceProfile(packet, out var parsed))
        {
            return false;
        }

        var source = context.CreateObservationSource(0x1B96, packet.Length);
        AppendPlayerGroupMember(context, in source, in parsed);
        return context.MarkParsed();
    }

    public static bool ParsePartyMemberListPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var members = new PacketPlayerGroupMember[6];
        var count = PacketPlayerGroupParser.ParsePartyMemberList(packet, members);
        var profiles = new PacketPlayerGroupProfile[6];
        var profileCount = PacketPlayerGroupParser.ParsePartyProfileList(packet, profiles);
        if (count == 0 && profileCount == 0)
        {
            return false;
        }

        var source = context.CreateObservationSource(0x0092, packet.Length);
        for (var i = 0; i < count; i++)
        {
            AppendPlayerGroupMember(context, in source, in members[i]);
        }

        for (var i = 0; i < profileCount; i++)
        {
            AppendPartyGroupProfile(context, in source, in profiles[i]);
        }

        return context.MarkParsed();
    }

    public static bool ParseForceMemberListPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var members = new PacketPlayerGroupMember[32];
        var count = PacketPlayerGroupParser.ParseMemberList(packet, members);
        if (count == 0)
        {
            return false;
        }

        var source = context.CreateObservationSource(0x1E96, packet.Length);
        for (var i = 0; i < count; i++)
        {
            AppendPlayerGroupMember(context, in source, in members[i]);
        }

        return context.MarkParsed();
    }

    public static bool ParseNicknamePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (PacketPlayerGroupParser.TryParse048DPartyMember(packet, out var member))
        {
            var source = context.CreateObservationSource(0x048D, packet.Length);
            AppendPlayerGroupMember(context, in source, in member);
            return context.MarkParsed();
        }

        if (!Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(context.CreateObservationSource(0x048D, packet.Length), parsed.PlayerId, parsed.Nickname, PacketFactionMapper.ToFaction(parsed.FactionCode), originServerId: parsed.OriginServerId, legionName: parsed.LegionName);
        return context.MarkParsed();
    }

    private static void AppendPlayerGroupMember(PacketParseContext context, in PacketObservationSource source, in PacketPlayerGroupMember parsed)
    {
        if (parsed.HasIdentityProfile)
        {
            context.Sink.AppendNickname(in source, parsed.EntityId, parsed.Nickname, originServerId: parsed.OriginServerId);
        }

        var membership = parsed.Kind == PacketPlayerGroupKind.Force
            ? PlayerGroupMembership.Force(parsed.GroupId, parsed.SubPartyIndex, parsed.MemberSlotIndex)
            : PlayerGroupMembership.Party(parsed.MemberSlotIndex);
        context.Sink.AppendPlayerGroupMember(in source, parsed.EntityId, in membership);
    }

    private static void AppendPartyGroupProfile(PacketParseContext context, in PacketObservationSource source, in PacketPlayerGroupProfile profile)
    {
        context.Sink.AppendPlayerGroupProfile(in source, profile.OriginServerId, profile.Nickname, PlayerGroupMembership.Party(profile.MemberSlotIndex));
    }
}
