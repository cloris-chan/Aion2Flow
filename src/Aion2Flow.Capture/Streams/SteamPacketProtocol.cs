using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class SteamPacketProtocol : ClientPacketProtocol
{
    public override bool ParseFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketTransportCodec.TryReadTransportLength(packet, 0, out var frameLength) || frameLength != packet.Length)
            return false;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _) || !reader.TryReadByte(out var opcode0) || !reader.TryReadByte(out var opcode1))
            return false;

        // Only observed Steam families are dispatched. Unproved layouts stay unresolved.
        return (opcode0, opcode1) switch
        {
            (0x21, 0x36) or (0x23, 0x36) or (0x01, 0x40) or (0x02, 0x40) or
            (0x2e, 0x92) or (0x2f, 0x92) => ParseMapPacket(packet, (ushort)((opcode0 << 8) | opcode1), ref context),
            (0x03, 0x36) => PacketTelemetryHandler.ParseRoundTripEcho0336Packet(packet, ref context),
            (0x33, 0x36) => PacketIdentityHandler.ParseOwnNicknamePacket(packet, ref context),
            (0x41, 0x36) => ParseNpcState(packet, ref context),
            (0x45, 0x36) => PacketIdentityHandler.ParseNearbyPcProfilePacket(packet, ref context),
            (0x02, 0x38) => PacketCombatHandler.ParseCompactControl0238Packet(packet, ref context),
            (0x04, 0x38) => PacketCombatHandler.Parse0438ValuePacket(packet, ref context),
            (0x05, 0x38) => PacketCombatHandler.ParsePeriodicValuePacket(packet, ref context),
            (0x06, 0x38) => PacketCombatHandler.ParseCompactControl0638Packet(packet, ref context),
            (0x22, 0x38) => PacketCooldownHandler.Parse2238Packet(packet, ref context),
            (0x2a, 0x38) => PacketStateHandler.ParseAux2A38Packet(packet, ref context),
            (0x2b, 0x38) => PacketStateHandler.ParseAux2B38Packet(packet, ref context),
            (0x00, 0x8d) => PacketStateHandler.ParseRemainHpPacket(packet, ref context),
            _ => false
        };
    }

    public override bool IsKnownGameplayOpcode(byte opcode0, byte opcode1) => (opcode0, opcode1) is
        (0x00, 0x36) or (0x03, 0x36) or (0x21, 0x36) or (0x23, 0x36) or (0x33, 0x36) or (0x41, 0x36) or (0x45, 0x36) or
        (0x02, 0x38) or (0x04, 0x38) or (0x05, 0x38) or (0x06, 0x38) or
        (0x22, 0x38) or (0x2a, 0x38) or (0x2b, 0x38) or (0x01, 0x40) or (0x02, 0x40) or
        (0x00, 0x8d) or (0x2e, 0x92) or (0x2f, 0x92);

    public override bool IsStrongWorldFrame(ReadOnlySpan<byte> frame, int prefixLength, long captureMilliseconds)
    {
        if (IsConfirmedServerTick(frame, captureMilliseconds))
            return true;
        return (frame[prefixLength], frame[prefixLength + 1]) switch
        {
            (0x03, 0x36) => Packet0336RoundTripParser.TryParse(frame, out _),
            (0x33, 0x36) => Packet3336NicknameParser.TryParse(frame, out _),
            (0x41, 0x36) => SteamPacket4136Parser.TryParse(frame, out _),
            (0x45, 0x36) => Packet4536PcMetadataParser.TryParse(frame, out _),
            _ => false
        };
    }

    public override bool ParseRecovery(ReadOnlySpan<byte> packet, ref PacketParseContext context, out int nestedOffset)
    {
        nestedOffset = -1;
        return false;
    }

    public override bool ScanUnknownFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context) => false;

    private static bool ParseMapPacket(ReadOnlySpan<byte> packet, ushort opcode, ref PacketParseContext context)
        => SteamPacketMapParser.TryParse(packet, out var mapEvent) &&
           PacketMapHandler.Apply(in mapEvent, opcode, packet.Length, ref context);

    private static bool ParseNpcState(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!SteamPacket4136Parser.TryParse(packet, out var state))
            return false;

        var source = context.CreateObservationSource(0x4136, packet.Length);
        context.Sink.RememberNpcObservationSource(state.EntityId);
        context.Writer.ApplyNpcCatalog(in source, state.EntityId, state.NpcCode);
        if (state.OwnerId is int ownerId)
            context.Sink.AppendSummon(in source, ownerId, state.EntityId);
        if (state.CurrentHp is int currentHp && state.MaxHp is int maxHp)
            context.Sink.AppendNpcHp(in source, state.EntityId, currentHp, maxHp);
        return context.MarkParsed();
    }
}
