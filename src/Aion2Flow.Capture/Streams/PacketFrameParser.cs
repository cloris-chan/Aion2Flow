using System.Buffers;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketFrameParser(IRuntimeObservationSink sink)
{
    private const int MaxDecompressedSize = 4 * 1024 * 1024;
    private readonly SceneObservationWriter _writer = new(sink);
    private readonly PacketOrdinalState _ordinals = new();

    private static ReadOnlySpan<byte> Pattern => PacketTransportCodec.Pattern;

    public long CurrentAppendBatchOrdinal => _ordinals.CurrentAppendBatchOrdinal;

    public long BeginAppendBatch() => _ordinals.BeginAppendBatch();

    public void EndAppendBatch(long previous) => _ordinals.EndAppendBatch(previous);

    public bool ParsePacketEntry(ReadOnlySpan<byte> packet, in TcpConnection connection, long timestampMilliseconds)
    {
        var context = new PacketParseContext(sink, _writer, _ordinals, connection, timestampMilliseconds);
        ParsePacketEntry(packet, ref context);
        return context.Parsed;
    }

    private void ParsePacketEntry(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (packet.IsEmpty)
        {
            return;
        }

        if (TryParseCompressedContainer(packet, ref context)) return;
        if (TryParseFrameBatch(packet, ref context)) return;

        var payload = packet.EndsWith(Pattern)
            ? packet[..^Pattern.Length]
            : packet;

        if (payload.IsEmpty)
        {
            return;
        }

        if (packet.EndsWith(Pattern))
        {
            RawPacketDump.AppendFrameEvent("tail-pattern", context.Connection, $"packetLen={packet.Length}", packet);
        }

        if (TryParsePacketContainer(packet, ref context)) return;
        if (ParseFramePayload(payload, ref context)) return;

        RawPacketDump.AppendFrameEvent("recovery-path", context.Connection, $"payloadLen={payload.Length}", payload);
        ParseRecoveryPacket(payload, ref context);
    }

    private bool TryParseCompressedContainer(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!PacketTransportCodec.TryReadVarInt(packet, 0, out var lengthInfo))
        {
            return false;
        }

        var totalLength = lengthInfo.Value + lengthInfo.ByteCount - 4;
        if (totalLength <= 0 || totalLength != packet.Length)
        {
            return false;
        }

        var offset = lengthInfo.ByteCount;
        var extraFlag = false;
        if (offset < packet.Length && packet[offset] is >= 0xf0 and < 0xff)
        {
            extraFlag = true;
            offset++;
        }

        if (offset + 6 > packet.Length)
        {
            return false;
        }

        if (packet[offset] != 0xff || packet[offset + 1] != 0xff)
        {
            return false;
        }
        offset += 2;

        var uncompressedLength = packet[offset]
            | (packet[offset + 1] << 8)
            | (packet[offset + 2] << 16)
            | (packet[offset + 3] << 24);

        if (uncompressedLength <= 0 || uncompressedLength > MaxDecompressedSize)
        {
            return false;
        }
        offset += 4;

        if (offset >= packet.Length)
        {
            return false;
        }

        var rented = ArrayPool<byte>.Shared.Rent(uncompressedLength);
        try
        {
            var decoded = LZ4Codec.Decode(packet[offset..], rented.AsSpan(0, uncompressedLength));
            if (decoded <= 0)
            {
                return false;
            }

            var restored = rented.AsSpan(0, decoded);
            RawPacketDump.AppendFrameEvent("compressed-container", context.Connection, $"extraFlag={extraFlag}|decoded={decoded}|encoded={packet.Length - offset}", restored);
            return TryParseFrameBatch(restored, ref context) || TryParsePacketContainer(restored, ref context) || ParseFramePayload(restored, ref context);
        }
        catch
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private bool TryParseFrameBatch(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var previousBatchOrdinal = _ordinals.BeginFrameBatch();
        var offset = 0;
        var parsed = false;
        var frameCount = 0;

        try
        {
            while (offset < packet.Length)
            {
                if (packet.Length - offset >= Pattern.Length && packet[offset..].StartsWith(Pattern))
                {
                    offset += Pattern.Length;
                    continue;
                }

                if (!PacketTransportCodec.TryReadTransportLength(packet, offset, out var frameLength))
                {
                    break;
                }

                if (frameLength <= 0 || offset + frameLength > packet.Length)
                {
                    break;
                }

                var frame = packet.Slice(offset, frameLength);
                var framePayload = frame.EndsWith(Pattern)
                    ? frame[..^Pattern.Length]
                    : frame;

                if (framePayload.IsEmpty)
                {
                    offset += frameLength;
                    continue;
                }

                frameCount++;
                RawPacketDump.AppendFrameEvent("frame-batch", context.Connection, $"offset={offset}|frameLength={frameLength}", frame);

                if (ParseFramePayload(framePayload, ref context))
                {
                    parsed = true;
                }

                offset += frameLength;
            }

            return parsed && frameCount > 0;
        }
        finally
        {
            _ordinals.EndFrameBatch(previousBatchOrdinal);
        }
    }

    private bool TryParsePacketContainer(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var parsed = false;

        for (var offset = 0; offset <= packet.Length - 3; offset++)
        {
            if (!PacketTransportCodec.TryReadVarInt(packet, offset, out var packetLengthInfo))
            {
                continue;
            }

            var declaredLength = packetLengthInfo.Value;
            if (declaredLength <= Pattern.Length || declaredLength > packet.Length - offset)
            {
                continue;
            }

            var candidate = packet.Slice(offset, declaredLength);
            if (!candidate[^Pattern.Length..].SequenceEqual(Pattern))
            {
                continue;
            }

            var bodyLength = declaredLength - Pattern.Length;
            if (bodyLength <= 0)
            {
                continue;
            }

            RawPacketDump.AppendFrameEvent("container-candidate", context.Connection, $"offset={offset}|declaredLength={declaredLength}", candidate);
            parsed |= ParsePerfectPacket(candidate[..bodyLength], ref context);
        }

        return parsed;
    }

    private bool ParsePerfectPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (packet.Length < 3) return false;
        return PacketOpcodeDispatcher.TryParseExactFrame(packet, ref context);
    }

    private bool ParseFramePayload(ReadOnlySpan<byte> payload, ref PacketParseContext context)
    {
        var previous = _ordinals.BeginFramePayload();

        try
        {
            return PacketOpcodeDispatcher.ParseFramePayload(payload, ref context);
        }
        finally
        {
            _ordinals.EndFramePayload(previous.Frame, previous.Batch);
        }
    }

    private void ParseRecoveryPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        PacketRecoveryParser.ParseRecoveryPacket(packet, ref context, out var nestedOffset);
        if (nestedOffset >= 0 && nestedOffset < packet.Length)
        {
            ParsePacketEntry(packet[nestedOffset..], ref context);
        }
    }
}
