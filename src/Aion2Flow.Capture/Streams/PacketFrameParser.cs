using System.Buffers;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketFrameParser(IRuntimeObservationSink sink) : IDisposable
{
    private const int MaxDecompressedSize = 4 * 1024 * 1024;
    private const int MaxRetainedDecompressionBufferSize = 512 * 1024;
    private readonly SceneObservationWriter _writer = new(sink);
    private readonly PacketOrdinalState _ordinals = new();
    private byte[]? _decompressionBuffer;

    private static ReadOnlySpan<byte> Pattern => PacketTransportCodec.Pattern;

    public long CurrentAppendBatchOrdinal => _ordinals.CurrentAppendBatchOrdinal;

    public long BeginAppendBatch() => _ordinals.BeginAppendBatch();

    public void EndAppendBatch(long previous) => _ordinals.EndAppendBatch(previous);

    public void Dispose()
    {
        if (_decompressionBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_decompressionBuffer);
            _decompressionBuffer = null;
        }
    }

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

        if (TryParsePacketContainer(packet, ref context)) return;
        if (HasLeadingNonWholeTransportFrame(payload)) return;
        if (ParseFramePayload(payload, ref context)) return;

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
        if (offset < packet.Length && packet[offset] is >= 0xf0 and < 0xff)
        {
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

        var rented = RentDecompressionBuffer(uncompressedLength, out var returnAfterUse);
        try
        {
            var decoded = LZ4Codec.Decode(packet[offset..], rented.AsSpan(0, uncompressedLength));
            if (decoded <= 0)
            {
                return false;
            }

            var restored = rented.AsSpan(0, decoded);
            if (TryParseFrameBatch(restored, ref context) || TryParsePacketContainer(restored, ref context))
            {
                return true;
            }

            return HasLeadingNonWholeTransportFrame(restored) || ParseFramePayload(restored, ref context);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (returnAfterUse)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private byte[] RentDecompressionBuffer(int length, out bool returnAfterUse)
    {
        if (length > MaxRetainedDecompressionBufferSize)
        {
            returnAfterUse = true;
            return ArrayPool<byte>.Shared.Rent(length);
        }

        if (_decompressionBuffer is null || _decompressionBuffer.Length < length)
        {
            if (_decompressionBuffer is not null)
                ArrayPool<byte>.Shared.Return(_decompressionBuffer);

            _decompressionBuffer = ArrayPool<byte>.Shared.Rent(length);
        }

        returnAfterUse = false;
        return _decompressionBuffer;
    }

    private bool TryParseFrameBatch(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!IsCompleteFrameBatch(packet))
        {
            return false;
        }

        var previousBatchOrdinal = _ordinals.BeginFrameBatch();
        var offset = 0;

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
                    return true;
                }

                if (frameLength <= 0 || offset + frameLength > packet.Length)
                {
                    return true;
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

                if (!ParseFramePayload(framePayload, ref context))
                {
                    ParseUnknownFramePayload(framePayload, ref context);
                }

                offset += frameLength;
            }

            return true;
        }
        finally
        {
            _ordinals.EndFrameBatch(previousBatchOrdinal);
        }
    }

    private static bool IsCompleteFrameBatch(ReadOnlySpan<byte> packet)
    {
        var offset = 0;
        var frameCount = 0;

        while (offset < packet.Length)
        {
            if (packet.Length - offset >= Pattern.Length && packet[offset..].StartsWith(Pattern))
            {
                offset += Pattern.Length;
                continue;
            }

            if (!PacketTransportCodec.TryReadTransportLength(packet, offset, out var frameLength))
            {
                return false;
            }

            if (frameLength <= 0 || offset + frameLength > packet.Length)
            {
                return false;
            }

            var frame = packet.Slice(offset, frameLength);
            var framePayload = frame.EndsWith(Pattern)
                ? frame[..^Pattern.Length]
                : frame;

            if (!framePayload.IsEmpty)
            {
                frameCount++;
            }

            offset += frameLength;
        }

        return frameCount > 0 && offset == packet.Length;
    }

    private static bool TryParsePacketContainer(ReadOnlySpan<byte> packet, ref PacketParseContext context)
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

            parsed |= ParsePerfectPacket(candidate[..bodyLength], ref context);
        }

        return parsed;
    }

    private static bool ParsePerfectPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (packet.Length < 3) return false;
        return PacketOpcodeDispatcher.TryParseExactFrame(packet, ref context);
    }

    private void ParseUnknownFramePayload(ReadOnlySpan<byte> framePayload, ref PacketParseContext context)
    {
        if (TrySliceFrameBody(framePayload, out var body) && !body.IsEmpty)
        {
            if (TryParsePacketContainer(body, ref context) || TryParseFrameBatch(body, ref context))
            {
                return;
            }

            if (!HasLeadingNonWholeTransportFrame(body) && ParseFramePayload(body, ref context))
            {
                return;
            }

            if (ParseRecoveryPacket(body, ref context))
            {
                return;
            }

            if (PacketUnknownFramePayloadScanner.Scan(body, ref context))
            {
                return;
            }
        }

        if (PacketUnknownFramePayloadScanner.Scan(framePayload, ref context))
        {
            return;
        }

        PacketEmbeddedIdentityScanner.Scan(framePayload, ref context);
    }

    private static bool TrySliceFrameBody(ReadOnlySpan<byte> framePayload, out ReadOnlySpan<byte> body)
    {
        body = default;
        if (!PacketTransportCodec.TryReadVarInt(framePayload, 0, out var lengthInfo))
        {
            return false;
        }

        var bodyOffset = lengthInfo.ByteCount + 2;
        if (bodyOffset > framePayload.Length)
        {
            return false;
        }

        body = framePayload[bodyOffset..];
        return true;
    }

    private static bool HasLeadingNonWholeTransportFrame(ReadOnlySpan<byte> payload)
    {
        return PacketTransportCodec.TryReadTransportLength(payload, 0, out var frameLength) &&
               frameLength > 0 &&
               frameLength != payload.Length;
    }

    private bool ParseFramePayload(ReadOnlySpan<byte> payload, ref PacketParseContext context)
    {
        var (Frame, Batch) = _ordinals.BeginFramePayload();

        try
        {
            return PacketOpcodeDispatcher.ParseFramePayload(payload, ref context);
        }
        finally
        {
            _ordinals.EndFramePayload(Frame, Batch);
        }
    }

    private bool ParseRecoveryPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var parsed = PacketRecoveryParser.ParseRecoveryPacket(packet, ref context, out var nestedOffset);
        if (nestedOffset >= 0 && nestedOffset < packet.Length)
        {
            ParsePacketEntry(packet[nestedOffset..], ref context);
        }

        return parsed;
    }
}
