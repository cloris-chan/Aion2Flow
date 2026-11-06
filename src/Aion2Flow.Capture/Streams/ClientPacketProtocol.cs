using System.Buffers.Binary;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture.Streams;

internal abstract class ClientPacketProtocol
{
    public static ClientPacketProtocol Purple { get; } = new PurplePacketProtocol();
    public static ClientPacketProtocol Steam { get; } = new SteamPacketProtocol();

    public static ClientPacketProtocol Resolve(ClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.ProtocolRevision switch
        {
            ProtocolRevision.Purple20260701 => Purple,
            ProtocolRevision.Steam20261029 => Steam,
            _ => throw new NotSupportedException($"Unsupported client protocol: {profile.ProtocolRevision}.")
        };
    }

    public abstract bool ParseFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context);
    public abstract bool ParseRecovery(ReadOnlySpan<byte> packet, ref PacketParseContext context, out int nestedOffset);
    public abstract bool ScanUnknownFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context);
    public abstract bool IsKnownGameplayOpcode(byte opcode0, byte opcode1);
    public abstract bool IsStrongWorldFrame(ReadOnlySpan<byte> frame, int prefixLength, long captureMilliseconds);

    public bool IsConfirmedServerTick(ReadOnlySpan<byte> frame, long captureMilliseconds)
    {
        const long maximumClockDifferenceMilliseconds = 60_000;
        if (frame.Length != 11 || frame[0] != 0x0e || frame[1] != 0x00 || frame[2] != 0x36)
            return false;

        var serverMilliseconds = BinaryPrimitives.ReadInt64LittleEndian(frame[3..]);
        return serverMilliseconds >= captureMilliseconds - maximumClockDifferenceMilliseconds &&
               serverMilliseconds <= captureMilliseconds + maximumClockDifferenceMilliseconds;
    }

    public bool HasRecognizedFrameStart(ReadOnlySpan<byte> payload, in PacketCanonicalFrameProbe frame)
    {
        if (frame.Kind == PacketCanonicalFrameProbeKind.Invalid ||
            frame.PrefixLength == 0 || payload.Length < frame.PrefixLength + sizeof(ushort))
            return false;

        var opcode0 = payload[frame.PrefixLength];
        var opcode1 = payload[frame.PrefixLength + 1];
        return (opcode0 == 0xff && opcode1 == 0xff) || opcode1 == 0x39 || IsKnownGameplayOpcode(opcode0, opcode1);
    }

    private sealed class PurplePacketProtocol : ClientPacketProtocol
    {
        public override bool ParseFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context)
            => PurplePacketOpcodeDispatcher.ParseFramePayload(packet, ref context);

        public override bool ParseRecovery(ReadOnlySpan<byte> packet, ref PacketParseContext context, out int nestedOffset)
            => PacketRecoveryParser.ParseRecoveryPacket(packet, ref context, out nestedOffset);

        public override bool ScanUnknownFrame(ReadOnlySpan<byte> packet, ref PacketParseContext context)
            => PacketUnknownFramePayloadScanner.Scan(packet, ref context);

        public override bool IsKnownGameplayOpcode(byte opcode0, byte opcode1)
            => (opcode0, opcode1) switch
            {
                (0x29, 0x33) or
                (0x00, 0x36) or (0x03, 0x36) or (0x11, 0x36) or (0x15, 0x36) or
                (0x21, 0x36) or (0x23, 0x36) or (0x33, 0x36) or (0x40, 0x36) or
                (0x41, 0x36) or (0x45, 0x36) or (0x46, 0x36) or (0x49, 0x36) or
                (0x4a, 0x36) or (0x4b, 0x36) or
                (0x1d, 0x37) or
                (0x02, 0x38) or (0x03, 0x38) or (0x04, 0x38) or (0x05, 0x38) or
                (0x06, 0x38) or (0x22, 0x38) or (0x2a, 0x38) or (0x2b, 0x38) or (0x2c, 0x38) or
                (0x35, 0x38) or (0x01, 0x40) or (0x02, 0x40) or (0x84, 0x56) or
                (0x00, 0x61) or (0x01, 0x61) or
                (0x00, 0x8d) or (0x04, 0x8d) or (0x21, 0x8d) or
                (0x00, 0x92) or (0x0d, 0x92) or (0x1b, 0x92) or (0x2e, 0x92) or (0x2f, 0x92) or
                (0x09, 0x94) or (0x0b, 0x94) or
                (0x02, 0x96) or (0x0a, 0x96) or (0x1b, 0x96) or (0x1d, 0x96) or
                (0x1e, 0x96) or (0x2b, 0x96) => true,
                _ => false
            };

        public override bool IsStrongWorldFrame(ReadOnlySpan<byte> frame, int prefixLength, long captureMilliseconds)
        {
            if (IsConfirmedServerTick(frame, captureMilliseconds))
                return true;

            return (frame[prefixLength], frame[prefixLength + 1]) switch
            {
                (0x03, 0x36) => Packet0336RoundTripParser.TryParse(frame, out _),
                (0x33, 0x36) => Packet3336NicknameParser.TryParse(frame, out _),
                (0x45, 0x36) => Packet4536PcMetadataParser.TryParse(frame, out _),
                (0x04, 0x8d) => Packet048DNicknameParser.TryParse(frame, out _),
                _ => false
            };
        }
    }
}
