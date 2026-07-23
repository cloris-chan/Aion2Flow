using System.Buffers;
using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Packets;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Capture.Streams;

internal enum TcpWorldStreamClassification : byte
{
    Pending,
    Confirmed,
    Rejected
}

internal sealed class TcpWorldStreamClassifier(bool allowMidstreamRecovery) : IDisposable
{
    private const int MaximumFrameLength = CaptureBufferLimits.CandidateStreamByteLimit;
    private const int MaximumDecompressedLength = 4 * 1024 * 1024;
    private const int MaximumInnerFrameCount = 4096;
    private const long MaximumClockDifferenceMilliseconds = 10_000;
    private readonly PacketTailBuffer _buffer = new(CaptureBufferLimits.CandidateStreamByteLimit);
    private bool _hasCanonicalAlignment = !allowMidstreamRecovery;
    private int _consecutiveGameplayFrames;
    private int _consumedByteCount;
    private int _recoveryScanOffset;
    private int _replayStartByteOffset;
    private TcpWorldStreamClassification _classification;

    public bool HasProtocolEvidence { get; private set; }
    public int ReplayStartByteOffset => _replayStartByteOffset;

    public void AllowMidstreamRecovery()
    {
        allowMidstreamRecovery = true;
    }

    public TcpWorldStreamClassification Append(ReadOnlySpan<byte> payload, long completionCaptureMilliseconds)
    {
        if (_classification != TcpWorldStreamClassification.Pending || payload.IsEmpty)
        {
            return _classification;
        }

        if (_buffer.Length + payload.Length > _buffer.Capacity)
        {
            _classification = TcpWorldStreamClassification.Rejected;
            return _classification;
        }

        _buffer.Append(payload);
        ObserveCanonicalProtocolEvidence();
        if (allowMidstreamRecovery && !_hasCanonicalAlignment)
        {
            var recovery = TryRecoverCanonicalBoundary(completionCaptureMilliseconds);
            if (recovery == RecoveryResult.Confirmed)
            {
                _classification = TcpWorldStreamClassification.Confirmed;
                return _classification;
            }
        }

        _classification = ClassifyAvailableFrames(completionCaptureMilliseconds);
        return _classification;
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }

    public static bool IsPlausibleConnectionStart(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty ||
            CapturedNonAionPayload.IsNonGameConnectionStart(payload) ||
            IsKnownTextProtocolStart(payload))
        {
            return false;
        }

        var prefix = ReadCanonicalLengthPrefix(payload);
        return prefix.Kind switch
        {
            LengthPrefixKind.NeedMore => true,
            LengthPrefixKind.Complete =>
                prefix.FrameLength >= prefix.PrefixLength + sizeof(ushort) &&
                prefix.FrameLength <= MaximumFrameLength,
            _ => false
        };
    }

    internal static bool IsConfirmed0036(ReadOnlySpan<byte> frame, long completionCaptureMilliseconds)
    {
        if (frame.Length != 11 || frame[0] != 0x0e || frame[1] != 0x00 || frame[2] != 0x36)
        {
            return false;
        }

        var serverMilliseconds = BinaryPrimitives.ReadInt64LittleEndian(frame[3..]);
        return serverMilliseconds >= completionCaptureMilliseconds - MaximumClockDifferenceMilliseconds &&
               serverMilliseconds <= completionCaptureMilliseconds + MaximumClockDifferenceMilliseconds;
    }

    private TcpWorldStreamClassification ClassifyAvailableFrames(long completionCaptureMilliseconds)
    {
        if (CapturedNonAionPayload.IsNonGameConnectionStart(_buffer.Data) || IsKnownTextProtocolStart(_buffer.Data))
        {
            return TcpWorldStreamClassification.Rejected;
        }

        while (_buffer.Length != 0)
        {
            ObserveCanonicalProtocolEvidence();
            var probe = ProbeCanonicalFrame(_buffer.Data);
            if (probe.Kind == FrameProbeKind.NeedMore)
            {
                return TcpWorldStreamClassification.Pending;
            }

            if (probe.Kind == FrameProbeKind.Invalid)
            {
                return _hasCanonicalAlignment
                    ? TcpWorldStreamClassification.Rejected
                    : TcpWorldStreamClassification.Pending;
            }

            var frame = _buffer.Data[..probe.FrameLength];
            if (frame.SequenceEqual(PacketTransportCodec.Pattern))
            {
                HasProtocolEvidence = true;
                ConsumeFrame(probe.FrameLength);
                continue;
            }

            if (IsStrongWorldFrame(frame, probe.PrefixLength, completionCaptureMilliseconds))
            {
                HasProtocolEvidence = true;
                EstablishCanonicalAlignment();
                return TcpWorldStreamClassification.Confirmed;
            }

            var compressed = ClassifyCompressedBatch(
                frame,
                probe.PrefixLength,
                completionCaptureMilliseconds,
                _consecutiveGameplayFrames);
            if (compressed.Kind == CompressedBatchKind.World)
            {
                HasProtocolEvidence = true;
                EstablishCanonicalAlignment();
                return TcpWorldStreamClassification.Confirmed;
            }

            if (compressed.Kind == CompressedBatchKind.Service)
            {
                return TcpWorldStreamClassification.Rejected;
            }

            if (compressed.Kind == CompressedBatchKind.Invalid)
            {
                if (_hasCanonicalAlignment)
                {
                    return TcpWorldStreamClassification.Rejected;
                }

                _consecutiveGameplayFrames = 0;
                ConsumeFrame(probe.FrameLength);
                continue;
            }

            if (compressed.Kind == CompressedBatchKind.WeakGameplay)
            {
                HasProtocolEvidence = true;
                EstablishCanonicalAlignment();
                _consecutiveGameplayFrames = compressed.ConsecutiveGameplayFrames;
                ConsumeFrame(probe.FrameLength);
                continue;
            }

            if (compressed.Kind == CompressedBatchKind.NonWorld)
            {
                _consecutiveGameplayFrames = 0;
                ConsumeFrame(probe.FrameLength);
                continue;
            }

            var opcodeOffset = probe.PrefixLength;
            var opcode0 = frame[opcodeOffset];
            var opcode1 = frame[opcodeOffset + 1];
            if (opcode1 == 0x39)
            {
                return TcpWorldStreamClassification.Rejected;
            }

            if (IsKnownGameplayOpcode(opcode0, opcode1))
            {
                HasProtocolEvidence = true;
                EstablishCanonicalAlignment();
                _consecutiveGameplayFrames++;
                if (_consecutiveGameplayFrames >= 2)
                {
                    return TcpWorldStreamClassification.Confirmed;
                }
            }
            else
            {
                if (!_hasCanonicalAlignment)
                {
                    return TcpWorldStreamClassification.Pending;
                }

                _consecutiveGameplayFrames = 0;
            }

            ConsumeFrame(probe.FrameLength);
        }

        return TcpWorldStreamClassification.Pending;
    }

    private RecoveryResult TryRecoverCanonicalBoundary(long completionCaptureMilliseconds)
    {
        var payload = _buffer.Data;
        const int tickFrameLength = 11;
        var scanEnd = payload.Length - tickFrameLength;
        for (var offset = _recoveryScanOffset; offset <= scanEnd; offset++)
        {
            if (IsConfirmed0036(payload.Slice(offset, tickFrameLength), completionCaptureMilliseconds))
            {
                HasProtocolEvidence = true;
                _replayStartByteOffset = checked(_consumedByteCount + offset);
                return RecoveryResult.Confirmed;
            }
        }

        _recoveryScanOffset = Math.Max(0, payload.Length - (tickFrameLength - 1));
        var sentinelOffset = payload.IndexOf(PacketTransportCodec.Pattern);
        if (sentinelOffset < 0)
        {
            return RecoveryResult.Pending;
        }

        _replayStartByteOffset = checked(_consumedByteCount + sentinelOffset);
        HasProtocolEvidence = true;
        var consumedLength = sentinelOffset + PacketTransportCodec.Pattern.Length;
        _buffer.Consume(consumedLength);
        _consumedByteCount = checked(_consumedByteCount + consumedLength);
        _hasCanonicalAlignment = true;
        _recoveryScanOffset = 0;
        _consecutiveGameplayFrames = 0;
        return RecoveryResult.Boundary;
    }

    private void ConsumeFrame(int frameLength)
    {
        _buffer.Consume(frameLength);
        _consumedByteCount = checked(_consumedByteCount + frameLength);
        _recoveryScanOffset = 0;
    }

    private void EstablishCanonicalAlignment()
    {
        if (_hasCanonicalAlignment)
        {
            return;
        }

        _hasCanonicalAlignment = true;
        _replayStartByteOffset = _consumedByteCount;
    }

    private void ObserveCanonicalProtocolEvidence()
    {
        if (HasProtocolEvidence || !_hasCanonicalAlignment)
        {
            return;
        }

        var prefix = ReadCanonicalLengthPrefix(_buffer.Data);
        if (prefix.Kind != LengthPrefixKind.Complete ||
            prefix.FrameLength < prefix.PrefixLength + sizeof(ushort) ||
            prefix.FrameLength > MaximumFrameLength ||
            _buffer.Length < prefix.PrefixLength + sizeof(ushort))
        {
            return;
        }

        var opcodeOffset = prefix.PrefixLength;
        HasProtocolEvidence = IsKnownGameplayOpcode(
            _buffer.Data[opcodeOffset],
            _buffer.Data[opcodeOffset + 1]);
    }

    private static bool IsStrongWorldFrame(ReadOnlySpan<byte> frame, int prefixLength, long completionCaptureMilliseconds)
    {
        if (IsConfirmed0036(frame, completionCaptureMilliseconds))
        {
            return true;
        }

        var opcode0 = frame[prefixLength];
        var opcode1 = frame[prefixLength + 1];
        return (opcode0, opcode1) switch
        {
            (0x03, 0x36) =>
                Packet0336RoundTripParser.TryParse(frame, out var echo) &&
                Packet0336RoundTripParser.IsPlausibleClientEcho(echo.ClientSentUnixMilliseconds, completionCaptureMilliseconds),
            (0x33, 0x36) => Packet3336NicknameParser.TryParse(frame, out _),
            (0x45, 0x36) => Packet4536PcMetadataParser.TryParse(frame, out _),
            (0x04, 0x8d) => Packet048DNicknameParser.TryParse(frame, out _),
            _ => false
        };
    }

    private static CompressedBatchProbe ClassifyCompressedBatch(
        ReadOnlySpan<byte> frame,
        int prefixLength,
        long completionCaptureMilliseconds,
        int precedingGameplayFrameCount)
    {
        var offset = prefixLength;
        if (offset < frame.Length && frame[offset] is >= 0xf0 and < 0xff)
        {
            offset++;
        }

        if (offset + 2 > frame.Length || frame[offset] != 0xff || frame[offset + 1] != 0xff)
        {
            return CompressedBatchProbe.NotCompressed;
        }

        offset += 2;
        if (offset + sizeof(int) >= frame.Length)
        {
            return CompressedBatchProbe.Invalid;
        }

        var decompressedLength = BinaryPrimitives.ReadInt32LittleEndian(frame[offset..]);
        if (decompressedLength <= 0 || decompressedLength > MaximumDecompressedLength)
        {
            return CompressedBatchProbe.Invalid;
        }

        offset += sizeof(int);
        var decompressed = ArrayPool<byte>.Shared.Rent(decompressedLength);
        try
        {
            var decoded = LZ4Codec.Decode(frame[offset..], decompressed.AsSpan(0, decompressedLength));
            if (decoded != decompressedLength)
            {
                return CompressedBatchProbe.Invalid;
            }

            return ClassifyInnerFrameBatch(
                decompressed.AsSpan(0, decoded),
                completionCaptureMilliseconds,
                precedingGameplayFrameCount);
        }
        catch
        {
            return CompressedBatchProbe.Invalid;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(decompressed);
        }
    }

    private static CompressedBatchProbe ClassifyInnerFrameBatch(
        ReadOnlySpan<byte> payload,
        long completionCaptureMilliseconds,
        int precedingGameplayFrameCount)
    {
        var offset = 0;
        var frameCount = 0;
        var consecutiveGameplayFrames = precedingGameplayFrameCount;
        var hasStrongWorldFrame = false;
        var hasGameplaySequence = false;
        var hasGameplayFrame = false;
        var containsOnlyServiceFrames = true;
        while (offset < payload.Length)
        {
            var probe = ProbeCanonicalFrame(payload[offset..]);
            if (probe.Kind != FrameProbeKind.Complete)
            {
                return CompressedBatchProbe.Invalid;
            }

            var frame = payload.Slice(offset, probe.FrameLength);
            if (frame.SequenceEqual(PacketTransportCodec.Pattern))
            {
                offset += probe.FrameLength;
                continue;
            }

            frameCount++;
            if (frameCount > MaximumInnerFrameCount)
            {
                return CompressedBatchProbe.Invalid;
            }

            var opcodeOffset = probe.PrefixLength;
            var opcode0 = frame[opcodeOffset];
            var opcode1 = frame[opcodeOffset + 1];
            if (IsStrongWorldFrame(frame, probe.PrefixLength, completionCaptureMilliseconds))
            {
                hasStrongWorldFrame = true;
            }

            if (IsKnownGameplayOpcode(opcode0, opcode1))
            {
                hasGameplayFrame = true;
                containsOnlyServiceFrames = false;
                consecutiveGameplayFrames++;
                if (consecutiveGameplayFrames >= 2)
                {
                    hasGameplaySequence = true;
                }
            }
            else
            {
                consecutiveGameplayFrames = 0;
                containsOnlyServiceFrames &= opcode1 == 0x39;
            }

            offset += probe.FrameLength;
        }

        if (frameCount == 0 || offset != payload.Length)
        {
            return CompressedBatchProbe.Invalid;
        }

        if (hasStrongWorldFrame || hasGameplaySequence)
        {
            return CompressedBatchProbe.World;
        }

        if (containsOnlyServiceFrames)
        {
            return CompressedBatchProbe.Service;
        }

        return hasGameplayFrame && consecutiveGameplayFrames != 0
            ? new CompressedBatchProbe(CompressedBatchKind.WeakGameplay, consecutiveGameplayFrames)
            : CompressedBatchProbe.NonWorld;
    }

    private static FrameProbe ProbeCanonicalFrame(ReadOnlySpan<byte> payload)
    {
        var prefix = ReadCanonicalLengthPrefix(payload);
        if (prefix.Kind == LengthPrefixKind.NeedMore)
        {
            return FrameProbe.NeedMore;
        }

        if (prefix.Kind == LengthPrefixKind.Invalid ||
            prefix.FrameLength < prefix.PrefixLength + sizeof(ushort) ||
            prefix.FrameLength > MaximumFrameLength)
        {
            return FrameProbe.Invalid;
        }

        return payload.Length < prefix.FrameLength
            ? FrameProbe.NeedMore
            : new FrameProbe(FrameProbeKind.Complete, prefix.FrameLength, prefix.PrefixLength);
    }

    private static LengthPrefixProbe ReadCanonicalLengthPrefix(ReadOnlySpan<byte> payload)
    {
        ulong value = 0;
        for (var index = 0; index < 5; index++)
        {
            if (index >= payload.Length)
            {
                return LengthPrefixProbe.NeedMore;
            }

            var current = payload[index];
            if (index == 4 && (current & 0xf0) != 0)
            {
                return LengthPrefixProbe.Invalid;
            }

            value |= (ulong)(current & 0x7f) << (index * 7);
            if ((current & 0x80) != 0)
            {
                continue;
            }

            var prefixLength = index + 1;
            if (prefixLength > 1 && value < (1UL << ((prefixLength - 1) * 7)))
            {
                return LengthPrefixProbe.Invalid;
            }

            var frameLength = (long)value + prefixLength - 4;
            if (frameLength <= 0 || frameLength > int.MaxValue)
            {
                return LengthPrefixProbe.Invalid;
            }

            return new LengthPrefixProbe(LengthPrefixKind.Complete, (int)frameLength, prefixLength);
        }

        return LengthPrefixProbe.Invalid;
    }

    private static bool IsKnownGameplayOpcode(byte opcode0, byte opcode1)
    {
        return (opcode0, opcode1) switch
        {
            (0x29, 0x33) or
            (0x00, 0x36) or (0x03, 0x36) or (0x11, 0x36) or (0x15, 0x36) or
            (0x21, 0x36) or (0x23, 0x36) or (0x33, 0x36) or (0x40, 0x36) or
            (0x41, 0x36) or (0x45, 0x36) or (0x46, 0x36) or (0x49, 0x36) or
            (0x4a, 0x36) or (0x4b, 0x36) or
            (0x1d, 0x37) or
            (0x02, 0x38) or (0x03, 0x38) or (0x04, 0x38) or (0x05, 0x38) or
            (0x06, 0x38) or (0x2a, 0x38) or (0x2b, 0x38) or (0x2c, 0x38) or
            (0x35, 0x38) or
            (0x01, 0x40) or (0x02, 0x40) or
            (0x84, 0x56) or
            (0x00, 0x61) or (0x01, 0x61) or
            (0x00, 0x8d) or (0x04, 0x8d) or (0x21, 0x8d) or
            (0x00, 0x92) or (0x0d, 0x92) or (0x1b, 0x92) or (0x2e, 0x92) or
            (0x09, 0x94) or (0x0b, 0x94) or
            (0x02, 0x96) or (0x0a, 0x96) or (0x1b, 0x96) or (0x1d, 0x96) or
            (0x1e, 0x96) or (0x2b, 0x96) => true,
            _ => false
        };
    }

    private static bool IsKnownTextProtocolStart(ReadOnlySpan<byte> payload)
    {
        return payload.StartsWith("GET "u8) ||
               payload.StartsWith("POST "u8) ||
               payload.StartsWith("PUT "u8) ||
               payload.StartsWith("HEAD "u8) ||
               payload.StartsWith("HTTP/"u8) ||
               payload.StartsWith("PRI * HTTP/2.0"u8) ||
               payload.StartsWith("SSH-"u8);
    }

    private enum CompressedBatchKind : byte
    {
        NotCompressed,
        World,
        WeakGameplay,
        NonWorld,
        Service,
        Invalid
    }

    private readonly record struct CompressedBatchProbe(CompressedBatchKind Kind, int ConsecutiveGameplayFrames)
    {
        public static CompressedBatchProbe NotCompressed { get; } = new(CompressedBatchKind.NotCompressed, 0);
        public static CompressedBatchProbe World { get; } = new(CompressedBatchKind.World, 0);
        public static CompressedBatchProbe NonWorld { get; } = new(CompressedBatchKind.NonWorld, 0);
        public static CompressedBatchProbe Service { get; } = new(CompressedBatchKind.Service, 0);
        public static CompressedBatchProbe Invalid { get; } = new(CompressedBatchKind.Invalid, 0);
    }

    private enum RecoveryResult : byte
    {
        Pending,
        Boundary,
        Confirmed
    }

    private enum FrameProbeKind : byte
    {
        NeedMore,
        Invalid,
        Complete
    }

    private enum LengthPrefixKind : byte
    {
        NeedMore,
        Invalid,
        Complete
    }

    private readonly record struct FrameProbe(FrameProbeKind Kind, int FrameLength, int PrefixLength)
    {
        public static FrameProbe NeedMore { get; } = new(FrameProbeKind.NeedMore, 0, 0);
        public static FrameProbe Invalid { get; } = new(FrameProbeKind.Invalid, 0, 0);
    }

    private readonly record struct LengthPrefixProbe(LengthPrefixKind Kind, int FrameLength, int PrefixLength)
    {
        public static LengthPrefixProbe NeedMore { get; } = new(LengthPrefixKind.NeedMore, 0, 0);
        public static LengthPrefixProbe Invalid { get; } = new(LengthPrefixKind.Invalid, 0, 0);
    }
}
