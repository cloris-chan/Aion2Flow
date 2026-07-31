using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal enum PacketPlayerGroupKind : byte
{
    Party = 1,
    Force = 2
}

internal readonly record struct PacketPlayerGroupMember(PacketPlayerGroupKind Kind, int EntityId, uint GroupId, byte SubPartyIndex, byte MemberSlotIndex, int OriginServerId = 0, string Nickname = "")
{
    public bool HasIdentityProfile => OriginServerId > 0 && !string.IsNullOrEmpty(Nickname);
}

internal readonly record struct PacketPlayerGroupProfile(PacketPlayerGroupKind Kind, int OriginServerId, string Nickname, uint GroupId, byte SubPartyIndex, byte MemberSlotIndex);

internal readonly record struct PacketPlayerGroupRosterParseResult(int MemberCount, int ProfileCount)
{
    public bool IsValid => ProfileCount > 0;
}

internal static class PacketPlayerGroupParser
{
    private const int ForceRowFixedNamePrefixLength = 100;
    private const int PartyRowFixedNamePrefixLength = 110;
    private const int ExpectedUuidLength = 36;
    private const int MaxForceMemberRows = 32;
    private const int MaxPartyMemberRows = 6;
    private const int ProfileListOffset = 25;
    private const int ProfileListExtendedOffset = 34;
    private const int ProfileListMinimumRowStride = 112;
    private const int ProfileListNameLengthOffset = 56;
    private const int CompactProfileNameLengthOffset = 55;
    private const int ForceRosterSnapshotCountOffset = 18;
    private const int ForceRosterSnapshotRowsOffset = ForceRosterSnapshotCountOffset + sizeof(int);
    private const int GroupStatusFixedTailLength = 25;
    private const int ForceRosterServerOffset = 14;
    private const int ForceRosterUuidMarkerOffset = 18;
    private const int ForceRosterRepeatedServerOffset = ForceRosterUuidMarkerOffset + 1 + ExpectedUuidLength + 6;
    private const int ForceRosterNameLengthOffset = ForceRosterRepeatedServerOffset + sizeof(ushort);

    public static bool TryParsePartyMember(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, 0x0D, 0x92, out var body))
            return false;

        return TryReadDirectPartyMemberRow(body, 0, out member, out _);
    }

    public static bool TryParsePartyStatusMember(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
        => TryParseGroupStatusMember(packet, 0x1B, 0x92, PacketPlayerGroupKind.Party, out member);

    public static bool TryParseForceStatusMember(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
        => TryParseGroupStatusMember(packet, 0x2B, 0x96, PacketPlayerGroupKind.Force, out member);

    private static bool TryParseGroupStatusMember(ReadOnlySpan<byte> packet, byte opcode0, byte opcode1, PacketPlayerGroupKind kind, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, opcode0, opcode1, out var body))
            return false;

        var reader = new PacketSpanReader(body);
        if (!reader.TryReadVarInt(out var entityId) || entityId <= 0 ||
            !reader.TryReadVarInt(out var currentPrimaryResource) ||
            !reader.TryReadVarInt(out var maximumPrimaryResource) ||
            currentPrimaryResource < 0 || maximumPrimaryResource <= 0 || currentPrimaryResource > maximumPrimaryResource ||
            reader.Remaining != GroupStatusFixedTailLength ||
            reader.RemainingSpan[^1] > 1)
        {
            return false;
        }

        member = new PacketPlayerGroupMember(kind, entityId, 0, 0, 0);
        return true;
    }

    public static bool TryParsePartyProfile(ReadOnlySpan<byte> packet, out PacketPlayerGroupProfile profile)
    {
        profile = default;
        if (!TryReadPayload(packet, 0x0D, 0x92, out var body))
            return false;

        return TryReadPartyProfileRow(body, 0, out profile, out _);
    }

    public static bool TryParseForceMember(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, 0x1D, 0x96, out var body))
            return false;

        return TryReadForceMemberRow(body, 0, out member, out _);
    }

    public static bool TryParseForceProfile(ReadOnlySpan<byte> packet, out PacketPlayerGroupMember member)
    {
        member = default;
        if (!TryReadPayload(packet, 0x1B, 0x96, out var body))
            return false;

        return TryReadCompactForceProfileRow(body, 0, out member, out _);
    }

    public static bool TryParseForceRosterProfile(ReadOnlySpan<byte> packet, out PacketPlayerGroupProfile profile)
    {
        profile = default;
        if (!TryReadPayload(packet, 0x0A, 0x96, out var body))
            return false;

        return TryReadForceRosterProfileRow(body, 0, out profile, out _);
    }

    public static PacketPlayerGroupRosterParseResult ParseForceRosterSnapshot(
        ReadOnlySpan<byte> packet,
        Span<PacketPlayerGroupMember> members,
        Span<PacketPlayerGroupProfile> profiles)
    {
        if (members.Length == 0 || profiles.Length == 0 || !TryReadPayload(packet, 0x02, 0x96, out var body) || body.Length < ForceRosterSnapshotRowsOffset)
            return default;

        var declaredProfileCount = BinaryPrimitives.ReadInt32LittleEndian(body[ForceRosterSnapshotCountOffset..]);
        if (declaredProfileCount is <= 0 or > MaxForceMemberRows || profiles.Length < declaredProfileCount)
            return default;

        var memberCount = 0;
        var profileCount = 0;
        var offset = ForceRosterSnapshotRowsOffset;
        while (profileCount < declaredProfileCount && offset <= body.Length - CompactProfileNameLengthOffset - 1)
        {
            if (!TryReadCompactForceRosterRow(body, offset, out var row, out var rowTailOffset))
            {
                offset++;
                continue;
            }

            profiles[profileCount++] = new PacketPlayerGroupProfile(
                PacketPlayerGroupKind.Force,
                row.OriginServerId,
                row.Nickname,
                0,
                0,
                row.MemberSlotIndex);

            if (row.EntityId > 0)
            {
                if (memberCount >= members.Length)
                    return default;

                members[memberCount++] = row;
            }

            offset = Math.Max(rowTailOffset, offset + 1);
        }

        return profileCount == declaredProfileCount
            ? new PacketPlayerGroupRosterParseResult(memberCount, profileCount)
            : default;
    }

    public static int ParsePartyMemberList(ReadOnlySpan<byte> packet, Span<PacketPlayerGroupMember> destination)
    {
        if (destination.Length == 0 || !TryReadPayload(packet, 0x00, 0x92, out var body))
            return 0;

        var offset = body.Length > ProfileListExtendedOffset && body[0] == 0x01
            ? ProfileListExtendedOffset
            : ProfileListOffset;
        var count = 0;
        while (count < destination.Length &&
               count < MaxPartyMemberRows &&
               offset <= body.Length - ProfileListNameLengthOffset - 1)
        {
            if (!TryReadProfileListMemberRow(body, offset, out var member, out var rowTailOffset))
            {
                offset++;
                continue;
            }

            destination[count++] = member;
            if (!TryFindNextProfileListMemberRow(body, Math.Max(rowTailOffset + 32, offset + ProfileListMinimumRowStride), out offset))
                break;
        }

        return count;
    }

    public static int ParsePartyProfileList(ReadOnlySpan<byte> packet, Span<PacketPlayerGroupProfile> destination)
    {
        if (destination.Length == 0 || !TryReadPayload(packet, 0x00, 0x92, out var body))
            return 0;

        var count = 0;
        var offset = 0;
        while (count < destination.Length && count < MaxPartyMemberRows && offset <= body.Length - CompactProfileNameLengthOffset - 1)
        {
            if (TryReadPartyProfileRow(body, offset, out var profile, out var rowTailOffset))
            {
                destination[count++] = profile;
                offset = Math.Max(rowTailOffset, offset + 1);
                continue;
            }

            offset++;
        }

        return count;
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

            if (TryReadDirectPartyMemberRow(body, offset, out member, out rowLength))
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

    private static bool TryFindNextProfileListMemberRow(ReadOnlySpan<byte> body, int searchOffset, out int rowOffset)
    {
        for (var offset = Math.Max(0, searchOffset); offset <= body.Length - ProfileListNameLengthOffset - 1; offset++)
        {
            if (TryReadProfileListMemberRow(body, offset, out _, out _))
            {
                rowOffset = offset;
                return true;
            }
        }

        rowOffset = 0;
        return false;
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

        TryReadIdentityName(body, nameLengthOffset, requireValidText: false, out var nickname, out _);
        rowLength = ForceRowFixedNamePrefixLength + uuidLength + nameLength;
        if (rowOffset + rowLength > body.Length)
            return false;

        member = new PacketPlayerGroupMember(
            PacketPlayerGroupKind.Force,
            entityId,
            BinaryPrimitives.ReadUInt32LittleEndian(body[(rowOffset + 4)..]),
            subPartyIndex,
            memberSlotIndex,
            originServerId,
            nickname);
        return true;
    }

    private static bool TryReadDirectPartyMemberRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowLength)
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
        if (entityId <= 0 || !IsKnownServerId(originServerId))
            return false;

        var uuidLength = body[rowOffset + 10];
        var repeatedServerOffset = rowOffset + 11 + uuidLength + 6;
        var nameLengthOffset = repeatedServerOffset + sizeof(ushort);
        if (uuidLength != ExpectedUuidLength ||
            nameLengthOffset >= body.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(body[repeatedServerOffset..]) != originServerId)
            return false;

        var nameLength = body[nameLengthOffset];
        if (nameLength == 0 || nameLengthOffset + 1 + nameLength > body.Length)
            return false;

        TryReadIdentityName(body, nameLengthOffset, requireValidText: false, out var nickname, out _);
        rowLength = Math.Min(PartyRowFixedNamePrefixLength + uuidLength + nameLength, body.Length - rowOffset);

        member = new PacketPlayerGroupMember(PacketPlayerGroupKind.Party, entityId, 0, 0, memberSlotIndex, originServerId, nickname);
        return true;
    }

    private static bool TryReadPartyProfileRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupProfile profile, out int rowTailOffset)
    {
        profile = default;
        rowTailOffset = 0;
        if (rowOffset < 0 || rowOffset + CompactProfileNameLengthOffset + 1 > body.Length)
            return false;

        var memberSlotIndex = body[rowOffset + 1];
        if (body[rowOffset] is not (0x00 or 0x03) ||
            memberSlotIndex is < 1 or > 6 ||
            BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 2)..]) != 0)
        {
            return false;
        }

        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 6)..]);
        if (!IsKnownServerId(originServerId))
            return false;

        var uuidLength = body[rowOffset + 10];
        var repeatedServerOffset = rowOffset + 11 + uuidLength + 6;
        var nameLengthOffset = repeatedServerOffset + 2;
        if (uuidLength != ExpectedUuidLength ||
            !IsUuidText(body.Slice(rowOffset + 11, uuidLength)) ||
            nameLengthOffset >= body.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(body[repeatedServerOffset..]) != originServerId)
        {
            return false;
        }

        if (!TryReadIdentityName(body, nameLengthOffset, requireValidText: true, out var nickname, out rowTailOffset))
            return false;

        profile = new PacketPlayerGroupProfile(PacketPlayerGroupKind.Party, originServerId, nickname, 0, 0, memberSlotIndex);
        return true;
    }

    private static bool TryReadForceRosterProfileRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupProfile profile, out int rowTailOffset)
    {
        profile = default;
        rowTailOffset = 0;
        if (rowOffset < 0 || rowOffset + ForceRosterNameLengthOffset + 1 > body.Length)
            return false;

        var memberSlotIndex = body[rowOffset + 9];
        if (memberSlotIndex is < 1 or > 6 ||
            BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 10)..]) != 0)
        {
            return false;
        }

        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + ForceRosterServerOffset)..]);
        var originServerIdCopy = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + ForceRosterServerOffset + sizeof(ushort))..]);
        if (!IsKnownServerId(originServerId) || originServerId != originServerIdCopy)
            return false;

        var uuidTextOffset = rowOffset + ForceRosterUuidMarkerOffset + 1;
        var repeatedServerOffset = rowOffset + ForceRosterRepeatedServerOffset;
        var nameLengthOffset = rowOffset + ForceRosterNameLengthOffset;
        if (body[rowOffset + ForceRosterUuidMarkerOffset] != (byte)'$' ||
            !IsUuidText(body.Slice(uuidTextOffset, ExpectedUuidLength)) ||
            BinaryPrimitives.ReadUInt16LittleEndian(body[repeatedServerOffset..]) != originServerId)
        {
            return false;
        }

        if (!TryReadIdentityName(body, nameLengthOffset, requireValidText: true, out var nickname, out rowTailOffset) ||
            NicknameParserUtil.TryReadClassCode(body, rowTailOffset) is null)
        {
            return false;
        }

        profile = new PacketPlayerGroupProfile(PacketPlayerGroupKind.Force, originServerId, nickname, 0, 0, memberSlotIndex);
        return true;
    }

    private static bool TryReadProfileListMemberRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowTailOffset)
    {
        member = default;
        rowTailOffset = 0;
        if (rowOffset < 0 || rowOffset + ProfileListNameLengthOffset + 1 > body.Length)
            return false;

        var memberSlotIndex = body[rowOffset + 2];
        if (body[rowOffset] > 0x05 ||
            body[rowOffset + 1] > 0x07 ||
            memberSlotIndex is < 1 or > 6)
        {
            return false;
        }

        var entityId = BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 3)..]);
        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 7)..]);
        if (entityId <= 0 ||
            !IsKnownServerId(originServerId) ||
            body[rowOffset + 11] != ExpectedUuidLength ||
            !IsUuidText(body.Slice(rowOffset + 12, ExpectedUuidLength)) ||
            BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 54)..]) != originServerId)
        {
            return false;
        }

        if (!TryReadIdentityName(body, rowOffset + ProfileListNameLengthOffset, requireValidText: true, out var nickname, out rowTailOffset))
            return false;

        member = new PacketPlayerGroupMember(PacketPlayerGroupKind.Party, entityId, 0, 0, memberSlotIndex, originServerId, nickname);
        return true;
    }

    private static bool TryReadCompactForceProfileRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowTailOffset)
    {
        if (!TryReadCompactForceRosterRow(body, rowOffset, out member, out rowTailOffset) || member.EntityId <= 0)
        {
            member = default;
            rowTailOffset = 0;
            return false;
        }

        return true;
    }

    private static bool TryReadCompactForceRosterRow(ReadOnlySpan<byte> body, int rowOffset, out PacketPlayerGroupMember member, out int rowTailOffset)
    {
        member = default;
        rowTailOffset = 0;
        if (rowOffset < 0 || rowOffset + CompactProfileNameLengthOffset + 1 > body.Length)
            return false;

        var memberSlotIndex = body[rowOffset + 1];
        if (body[rowOffset] > 0x04 || memberSlotIndex is < 1 or > 6)
            return false;

        var entityId = BinaryPrimitives.ReadInt32LittleEndian(body[(rowOffset + 2)..]);
        var originServerId = BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 6)..]);
        if (entityId < 0 ||
            !IsKnownServerId(originServerId) ||
            body[rowOffset + 10] != ExpectedUuidLength ||
            !IsUuidText(body.Slice(rowOffset + 11, ExpectedUuidLength)) ||
            BinaryPrimitives.ReadUInt16LittleEndian(body[(rowOffset + 53)..]) != originServerId)
        {
            return false;
        }

        if (!TryReadIdentityName(body, rowOffset + CompactProfileNameLengthOffset, requireValidText: true, out var nickname, out rowTailOffset))
            return false;

        member = new PacketPlayerGroupMember(PacketPlayerGroupKind.Force, entityId, 0, 0, memberSlotIndex, originServerId, nickname);
        return true;
    }

    private static bool TryReadIdentityName(ReadOnlySpan<byte> body, int nameLengthOffset, bool requireValidText, out string nickname, out int tailOffset)
    {
        nickname = string.Empty;
        tailOffset = 0;
        if ((uint)nameLengthOffset >= (uint)body.Length)
            return false;

        var nameLength = body[nameLengthOffset];
        var textOffset = nameLengthOffset + 1;
        if (nameLength is < 1 or > NicknameParserUtil.MaxNicknameLength || textOffset + nameLength > body.Length)
            return false;

        if (NicknameParserUtil.TryReadLengthPrefixedIdentityText(body, nameLengthOffset, out nickname, out _, out tailOffset))
            return true;

        tailOffset = textOffset + nameLength;
        return !requireValidText;
    }

    private static bool IsUuidText(ReadOnlySpan<byte> value)
    {
        if (value.Length != ExpectedUuidLength)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var b = value[i];
            if (i is 8 or 13 or 18 or 23)
            {
                if (b != (byte)'-')
                    return false;
                continue;
            }

            if (b is not (>= (byte)'0' and <= (byte)'9') &&
                b is not (>= (byte)'A' and <= (byte)'F') &&
                b is not (>= (byte)'a' and <= (byte)'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownServerId(int serverId) => serverId is >= 1000 and <= 2999;
}
