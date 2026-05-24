using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketRecoveryParser
{
    public static bool ParseRecoveryPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context, out int nestedOffset, bool scanNicknames = true)
    {
        nestedOffset = -1;

        if (packet.Length < 4)
        {
            return false;
        }

        if (packet[2] == 0xff && packet[3] == 0xff)
        {
            if (packet.Length <= 10)
            {
                return false;
            }

            nestedOffset = 10;
            return false;
        }

        var processed = false;
        var target = context.Sink.CurrentTarget;
        if (target != 0)
        {
            Span<byte> targetBytes = stackalloc byte[5];
            if (!PacketTransportCodec.TryWriteVarInt(target, targetBytes, out var targetByteCount))
            {
                return false;
            }

            Span<byte> damageKeyword = stackalloc byte[2 + 5];
            damageKeyword[0] = 0x04;
            damageKeyword[1] = 0x38;
            targetBytes[..targetByteCount].CopyTo(damageKeyword[2..]);
            var damageNeedle = damageKeyword[..(2 + targetByteCount)];

            Span<byte> periodicKeyword = stackalloc byte[2 + 5];
            periodicKeyword[0] = 0x05;
            periodicKeyword[1] = 0x38;
            targetBytes[..targetByteCount].CopyTo(periodicKeyword[2..]);
            var periodicNeedle = periodicKeyword[..(2 + targetByteCount)];

            var damageIdx = packet.IndexOf(damageNeedle);
            var periodicIdx = packet.IndexOf(periodicNeedle);
            var idx = -1;
            var handlerKind = 0;

            if (damageIdx > 0 && periodicIdx > 0)
            {
                if (damageIdx < periodicIdx) { idx = damageIdx; handlerKind = 1; }
                else { idx = periodicIdx; handlerKind = 2; }
            }
            else if (damageIdx > 0)
            {
                idx = damageIdx; handlerKind = 1;
            }
            else if (periodicIdx > 0)
            {
                idx = periodicIdx; handlerKind = 2;
            }

            if (idx > 0 && handlerKind != 0 && PacketTransportCodec.TryReadVarInt(packet, idx - 1, out var packetLengthInfo) && packetLengthInfo.ByteCount == 1)
            {
                var startIdx = idx - 1;
                var endIdx = idx - 1 + packetLengthInfo.Value - 3;
                if (startIdx >= 0 && startIdx < endIdx && endIdx <= packet.Length)
                {
                    var extractedPacket = packet[startIdx..endIdx];
                    var bodyOffset = packetLengthInfo.ByteCount + 2;
                    var previous = context.EnterStructure(PacketStructureKind.RecoveredFrame, startIdx, extractedPacket.Length, bodyOffset, Math.Max(0, extractedPacket.Length - bodyOffset), 0);
                    try
                    {
                        processed = handlerKind == 1
                            ? PacketCombatHandler.Parse0438ValuePacket(extractedPacket, ref context)
                            : PacketCombatHandler.ParsePeriodicValuePacket(extractedPacket, ref context);
                    }
                    finally
                    {
                        context.RestoreStructure(previous);
                    }

                    if (processed && endIdx < packet.Length)
                    {
                        ParseRecoveryPacket(packet[endIdx..], ref context, out var remainingNestedOffset, scanNicknames: false);
                        if (remainingNestedOffset >= 0)
                        {
                            nestedOffset = endIdx + remainingNestedOffset;
                        }
                    }
                }
            }
        }

        if (scanNicknames && !processed)
        {
            PacketEmbeddedNicknameScanner.Scan(packet, ref context);
        }

        return processed;
    }
}
