using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketOpcodeDispatcher
{
    public static bool ParseFramePayload(ReadOnlySpan<byte> payload, ref PacketParseContext context)
        => TryParseExactFrame(payload, ref context);

    public static bool TryParseExactFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketTransportCodec.TryReadTransportLength(packet, 0, out var frameLength) || frameLength != packet.Length)
            return false;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;

        var opcode0 = packet[reader.Offset];
        var opcode1 = packet[reader.Offset + 1];

        return (opcode0, opcode1) switch
        {
            (0x02, 0x38) => PacketCombatHandler.ParseCompactControl0238Packet(packet, ref context),
            (0x04, 0x38) => PacketCombatHandler.Parse0438ValuePacket(packet, ref context),
            (0x05, 0x38) => PacketCombatHandler.ParsePeriodicValuePacket(packet, ref context),
            (0x06, 0x38) => PacketCombatHandler.ParseCompactControl0638Packet(packet, ref context),
            (0x00, 0x61) => PacketStateHandler.ParseMapEventPacket(packet, ref context, 0x0061),
            (0x01, 0x61) => PacketStateHandler.ParseMapEventPacket(packet, ref context, 0x0161),
            (0x21, 0x36) => PacketStateHandler.ParseState2136Packet(packet, ref context),
            (0x23, 0x36) => PacketStateHandler.ParsePendingMapArrival2336Packet(packet, ref context),
            (0x2e, 0x92) => PacketStateHandler.ParseMap2E92Packet(packet, ref context),
            (0x01, 0x40) => PacketStateHandler.ParseState0140Packet(packet, ref context),
            (0x02, 0x40) => PacketStateHandler.ParseState0240Packet(packet, ref context),
            (0x2a, 0x38) => PacketStateHandler.ParseAux2A38Packet(packet, ref context),
            (0x2b, 0x38) => PacketStateHandler.ParseAux2B38Packet(packet, ref context),
            (0x2c, 0x38) => PacketStateHandler.ParseAux2C38Packet(packet, ref context),
            (0x29, 0x33) => PacketIdentityHandler.ParseLegionMetadataPacket(packet, ref context),
            (0x35, 0x38) => PacketCombatHandler.Parse3538SidecarPacket(packet, ref context),
            (0x00, 0x92) => PacketIdentityHandler.ParsePartyMemberListPacket(packet, ref context),
            (0x02, 0x96) => PacketIdentityHandler.ParseForceRosterSnapshotPacket(packet, ref context),
            (0x0a, 0x96) => PacketIdentityHandler.ParseForceRosterProfilePacket(packet, ref context),
            (0x0d, 0x92) => PacketIdentityHandler.ParsePartyProfilePacket(packet, ref context),
            (0x1b, 0x92) => PacketIdentityHandler.ParsePartyStatusPacket(packet, ref context),
            (0x1b, 0x96) => PacketIdentityHandler.ParseForceProfilePacket(packet, ref context),
            (0x1d, 0x96) => PacketIdentityHandler.ParseForceMemberPacket(packet, ref context),
            (0x1e, 0x96) => PacketIdentityHandler.ParseForceMemberListPacket(packet, ref context),
            (0x2b, 0x96) => PacketIdentityHandler.ParseForceStatusPacket(packet, ref context),
            (0x1d, 0x37) => PacketStateHandler.ParseState1D37Packet(packet, ref context),
            (0x33, 0x36) => PacketIdentityHandler.ParseOwnNicknamePacket(packet, ref context),
            (0x09, 0x94) => PacketIdentityHandler.Parse0994NicknamePacket(packet, ref context),
            (0x0b, 0x94) => PacketIdentityHandler.Parse0994NicknamePacket(packet, ref context),
            (0x49, 0x36) => PacketStateHandler.ParseState4936Packet(packet, ref context),
            (0x84, 0x56) => PacketStateHandler.ParseWrapped8456Packet(packet, ref context),
            (0x40, 0x36) => PacketStateHandler.ParseSummonPacket(packet, ref context),
            (0x41, 0x36) => PacketStateHandler.ParseState4136Packet(packet, ref context),
            (0x45, 0x36) => PacketStateHandler.ParseState4536Packet(packet, ref context),
            (0x46, 0x36) => PacketStateHandler.ParseState4636Packet(packet, ref context),
            (0x04, 0x8d) => PacketIdentityHandler.ParseNicknamePacket(packet, ref context),
            (0x00, 0x8d) => PacketStateHandler.ParseRemainHpPacket(packet, ref context),
            (0x21, 0x8d) => PacketStateHandler.ParseBattleTogglePacket(packet, ref context),
            _ => false
        };
    }
}
