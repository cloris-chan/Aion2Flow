using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal enum PacketPlayerGroupKind : byte
{
    Party = 1,
    Force = 2
}

internal readonly record struct PacketPlayerGroupMember(PacketPlayerGroupKind Kind, int EntityId, uint GroupId, byte SubPartyIndex, byte MemberSlotIndex);

internal static class PacketPlayerGroupParser
{
    private const int ForceRowFixedNamePrefixLength = 100;
    private const int PartyRowFixedNamePrefixLength = 110;
    private const int ExpectedUuidLength = 36;
    private const int MaxForceMemberRows = 32;

    public static bool TryParsePartyProfile(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, 0x0D, 0x92, out var body))
            return false;

        return TryReadPartyProfileRow(body, 0, out member, out _);
    }

    public static bool TryParseForceMember(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, 0x1D, 0x96, out var body))
            return false;

        return TryReadForceMemberRow(body, 0, out member, out _);
    }

    public static int ParseMemberList(ReadOnlySpan<byte> packet, Span<PacketPlayerGroupMember> destination)
    {
        if (destination.Length == 0 || !TryReadPayload(packet, 0x1E, 0x96, out var body))
            return 0;

        var count = 0;
        var offset = body.Length > 0 ? 1 : 0;
        while (count < destination.Length && count < MaxForceMemberRows && offset <= body.Length - 25)
        {
            if (TryReadForceMemberRow(body, offset, out var member, out var rowLength))
            {
                destination[count++] = member;
                offset += rowLength;
                continue;
            }

            if (TryReadPartyProfileRow(body, offset, out member, out rowLength))
            {
                destination[count++] = member;
                offset += rowLength;
                continue;
            }

            offset++;
        }

        return count;
    }

    private static bool TryReadPayload(ReadOnlySpan<byte> packet, byte opcode0, byte opcode1, out ReadOnlySpan<byte> body)
    {
        body = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _) || reader.Remaining < 2)
            return false;

        if (packet[reader.Offset] != opcode0 || packet[reader.Offset + 1] != opcode1)
            return false;

        body = packet[(reader.Offset + 2)..];
        return true;
    }

    private static bool TryReadForceMemberRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowLength)
    {
        member = default;
        rowLength = 0;
        if (rowOffset < 0 || rowOffset + 25 > body.Length)
            return false;

        var subPartyIndex = body[rowOffset + 8];
        var memberSlotIndex = body[rowOffset + 15];
        if (subPartyIndex is < 1 or > 4 ||
            body[rowOffset + 9] != 0x05 ||
            body[rowOffset + 10] != 0 ||
            body[rowOffset + 11] != 0 ||
            body[rowOffset + 12] != 0 ||
            body[rowOffset + 13] is not (0x01 or 0x02) ||
            body[rowOffset + 14] != 0x03 ||
            memberSlotIndex is < 1 or > 6)
        {
            return false;
        }

        var entityId = BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 16)..]);
        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 20)..]);
        var originServerIdCopy = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 22)..]);
        if (entityId <= 0 || !IsKnownServerId(originServerId) || originServerId != originServerIdCopy)
            return false;

        var uuidLength = body[rowOffset + 24];
        var nameLengthOffset = rowOffset + 25 + uuidLength + 8;
        if (uuidLength != ExpectedUuidLength || nameLengthOffset >= body.Length)
            return false;

        var nameLength = body[nameLengthOffset];
        if (nameLength == 0 || nameLengthOffset + 1 + nameLength > body.Length)
            return false;

        rowLength = ForceRowFixedNamePrefixLength + uuidLength + nameLength;
        if (rowOffset + rowLength > body.Length)
            return false;

        member = new PacketPlayerGroupMember(
            PacketPlayerGroupKind.Force,
            entityId,
            BinaryPrimitives.ReadUInt32LittleEndian(body[(rowOffset + 4)..]),
            subPartyIndex,
            memberSlotIndex);
        return true;
    }

    private static bool TryReadPartyProfileRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowLength)
    {
        member = default;
        rowLength = 0;
        if (rowOffset < 0 || rowOffset + 11 > body.Length)
            return false;

        var memberSlotIndex = body[rowOffset + 1];
        if (body[rowOffset] != 0x03 || memberSlotIndex is < 1 or > 5)
            return false;

        var entityId = BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 2)..]);
        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 6)..]);
        var originServerIdCopy = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 8)..]);
        if (entityId <= 0 || !IsKnownServerId(originServerId) || originServerId != originServerIdCopy)
            return false;

        var uuidLength = body[rowOffset + 10];
        var nameLengthOffset = rowOffset + 11 + uuidLength + 8;
        if (uuidLength != ExpectedUuidLength || nameLengthOffset >= body.Length)
            return false;

        var nameLength = body[nameLengthOffset];
        if (nameLength == 0 || nameLengthOffset + 1 + nameLength > body.Length)
            return false;

        rowLength = Math.Min(PartyRowFixedNamePrefixLength + uuidLength + nameLength, body.Length - rowOffset);

        member = new PacketPlayerGroupMember(PacketPlayerGroupKind.Party, entityId, 0, 0, memberSlotIndex);
        return true;
    }

    private static bool IsKnownServerId(int serverId) => serverId is >= 1000 and <= 2999;
}
