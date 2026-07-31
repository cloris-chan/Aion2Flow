using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketPlayerGroupParserTests
{
    [Fact]
    public void PartyProfile_UsesPostUuidRepeatedServerWhenLegacyPrefixDiffers()
    {
        var packet = BuildPacket(
            0x0D,
            0x92,
            BuildPartyIdentityRow(
                rowHeader: 0x00,
                entityId: 0,
                memberSlotIndex: 2,
                originServerId: 1001,
                legacyServerId: 2002,
                nickname: "Profile"));

        Assert.True(PacketPlayerGroupParser.TryParsePartyProfile(packet, out var profile));
        Assert.Equal("Profile", profile.Nickname);
        Assert.Equal(1001, profile.OriginServerId);
    }

    [Fact]
    public void PartyMember_UsesPostUuidRepeatedServerWhenLegacyPrefixDiffers()
    {
        var packet = BuildPacket(
            0x0D,
            0x92,
            BuildPartyIdentityRow(
                rowHeader: 0x03,
                entityId: 42,
                memberSlotIndex: 2,
                originServerId: 1001,
                legacyServerId: 2002,
                nickname: "Member"));

        Assert.True(PacketPlayerGroupParser.TryParsePartyMember(packet, out var member));
        Assert.Equal(42, member.EntityId);
        Assert.Equal("Member", member.Nickname);
        Assert.Equal(1001, member.OriginServerId);
    }

    [Fact]
    public void PartyMemberList_SkipsMalformedRowAndAcceptsHeaderFive()
    {
        const int validRowOffset = 90;
        var body = new byte[validRowOffset + 56 + 1 + 5];
        body[25] = 0x05;

        var validRow = BuildProfileListRow(0x05, entityId: 77, memberSlotIndex: 3, originServerId: 1001, nickname: "Later");
        validRow.CopyTo(body.AsSpan(validRowOffset));
        var packet = BuildPacket(0x00, 0x92, body);
        var members = new PacketPlayerGroupMember[6];

        var count = PacketPlayerGroupParser.ParsePartyMemberList(packet, members);

        Assert.Equal(1, count);
        Assert.Equal(77, members[0].EntityId);
        Assert.Equal(3, members[0].MemberSlotIndex);
        Assert.Equal("Later", members[0].Nickname);
    }

    private static byte[] BuildPartyIdentityRow(
        byte rowHeader,
        int entityId,
        byte memberSlotIndex,
        ushort originServerId,
        ushort legacyServerId,
        string nickname)
    {
        var nameBytes = Encoding.UTF8.GetBytes(nickname);
        var uuid = Encoding.ASCII.GetBytes("01234567-89ab-cdef-0123-456789abcdef");
        var nameLengthOffset = 11 + uuid.Length + 6 + sizeof(ushort);
        var body = new byte[nameLengthOffset + 1 + nameBytes.Length];
        body[0] = rowHeader;
        body[1] = memberSlotIndex;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), entityId);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), originServerId);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8), legacyServerId);
        body[10] = (byte)uuid.Length;
        uuid.CopyTo(body.AsSpan(11));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(11 + uuid.Length + 6), originServerId);
        body[nameLengthOffset] = (byte)nameBytes.Length;
        nameBytes.CopyTo(body.AsSpan(nameLengthOffset + 1));
        return body;
    }

    private static byte[] BuildProfileListRow(byte rowHeader, int entityId, byte memberSlotIndex, ushort originServerId, string nickname)
    {
        var nameBytes = Encoding.UTF8.GetBytes(nickname);
        var uuid = Encoding.ASCII.GetBytes("01234567-89ab-cdef-0123-456789abcdef");
        var body = new byte[56 + 1 + nameBytes.Length];
        body[0] = rowHeader;
        body[1] = 0;
        body[2] = memberSlotIndex;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), entityId);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(7), originServerId);
        body[11] = (byte)uuid.Length;
        uuid.CopyTo(body.AsSpan(12));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(54), originServerId);
        body[56] = (byte)nameBytes.Length;
        nameBytes.CopyTo(body.AsSpan(57));
        return body;
    }

    private static byte[] BuildPacket(byte opcode0, byte opcode1, ReadOnlySpan<byte> body)
    {
        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(body.Length + 6, prefix, out var prefixLength));
        var packet = new byte[prefixLength + sizeof(ushort) + body.Length];
        prefix[..prefixLength].CopyTo(packet);
        packet[prefixLength] = opcode0;
        packet[prefixLength + 1] = opcode1;
        body.CopyTo(packet.AsSpan(prefixLength + sizeof(ushort)));
        return packet;
    }
}
