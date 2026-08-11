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
    private const int MaximumFrameLength = CaptureBufferLimits.StreamTailBufferSize;
    private const int MaximumDecompressedLength = 4 * 1024 * 1024;
    private const int MaximumInnerFrameCount = 4096;
    private const long MaximumClockDifferenceMilliseconds = 60_000;
    private readonly PacketTransportStreamDeframer _transport = new();
    private bool _hasCanonicalAlignment = !allowMidstreamRecovery;
    private int _consecutiveGameplayFrames;
    private int _recoveryScanOffset;
    private int _sentinelRecoveryScanOffset;
    private int _replayStartByteOffset;
    private TcpWorldStreamClassification _classification;

    public bool HasProtocolEvidence { get; private set; }
    public int ReplayStartByteOffset => _replayStartByteOffset;

    public void AllowMidstreamRecovery()
    {
        if (allowMidstreamRecovery)
        {
            return;
        }

        allowMidstreamRecovery = true;
        if (!HasProtocolEvidence)
        {
            _hasCanonicalAlignment = false;
        }
    }

    public TcpWorldStreamClassification Append(ReadOnlySpan<byte> payload, long completionCaptureMilliseconds)
    {
        if (_classification != TcpWorldStreamClassification.Pending || payload.IsEmpty)
        {
            return _classification;
        }

        if (_transport.BufferedByteCount + payload.Length > CaptureBufferLimits.CandidateStreamByteLimit)
        {
            _classification = TcpWorldStreamClassification.Rejected;
            return _classification;
        }

        _transport.Append(payload);
        if (allowMidstreamRecovery && !_hasCanonicalAlignment && !_transport.IsLengthPrefixed)
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
        _transport.Dispose();
    }

    public static bool IsPlausibleConnectionStart(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return false;
        }

        var lengthPrefixed = PacketTransportCodec.ProbeLengthPrefixedStream(
            payload,
            PacketTransportCodec.MaximumEnvelopeBodyLength);
        if (lengthPrefixed.Kind != PacketLengthPrefixedProbeKind.Invalid)
        {
            return true;
        }

        if (CapturedNonAionPayload.IsNonGameConnectionStart(payload) ||
            IsKnownTextProtocolStart(payload))
        {
            return false;
        }

        var frame = PacketTransportCodec.ProbeCanonicalFrame(payload, MaximumFrameLength);
        return frame.Kind switch
        {
            PacketCanonicalFrameProbeKind.NeedMore => true,
            PacketCanonicalFrameProbeKind.Complete => true,
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
        while (true)
        {
            var availability = _transport.PrepareCanonicalData();
            if (availability == PacketTransportDataAvailability.NeedMore)
            {
                if (_transport.TryGetPendingDirectRecoveryTickOffset(out var tickOffset))
                {
                    var pendingData = _transport.RawData;
                    if (tickOffset <= pendingData.Length - 11 &&
                        IsConfirmed0036(
                            pendingData.Slice(tickOffset, 11),
                            completionCaptureMilliseconds))
                    {
                        _transport.ResolvePendingDirectRecovery();
                        _replayStartByteOffset = checked(
                            (int)(_transport.TotalRawConsumedByteCount + PacketTransportCodec.LengthPrefixedHeaderLength));
                        _transport.DiscardRawPrefix(PacketTransportCodec.LengthPrefixedHeaderLength);
                        _transport.MarkDirectCanonicalAlignment();
                        _hasCanonicalAlignment = true;
                        HasProtocolEvidence = true;
                        _consecutiveGameplayFrames = 0;
                        return TcpWorldStreamClassification.Confirmed;
                    }

                    if (_transport.ResolvePendingLengthPrefixed())
                    {
                        continue;
                    }
                }

                return TcpWorldStreamClassification.Pending;
            }

            if (availability == PacketTransportDataAvailability.Invalid)
            {
                return TcpWorldStreamClassification.Rejected;
            }

            if (!_transport.IsLengthPrefixed &&
                (CapturedNonAionPayload.IsNonGameConnectionStart(_transport.RawData) ||
                 IsKnownTextProtocolStart(_transport.RawData)))
            {
                return TcpWorldStreamClassification.Rejected;
            }

            ObserveCanonicalProtocolEvidence();
            var data = _transport.CanonicalData;
            var probe = PacketTransportCodec.ProbeCanonicalFrame(data, MaximumFrameLength);
            if (probe.Kind == PacketCanonicalFrameProbeKind.NeedMore)
            {
                if (_transport.TryExpandCanonicalData())
                {
                    continue;
                }

                if (_transport.IsFaulted)
                {
                    return TcpWorldStreamClassification.Rejected;
                }

                return TcpWorldStreamClassification.Pending;
            }

            if (probe.Kind == PacketCanonicalFrameProbeKind.Invalid)
            {
                return _hasCanonicalAlignment
                    ? TcpWorldStreamClassification.Rejected
                    : TcpWorldStreamClassification.Pending;
            }

            var frame = data[..probe.FrameLength];
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

            if (PacketTransportCodec.IsKnownGameplayOpcode(opcode0, opcode1))
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
    }

    private RecoveryResult TryRecoverCanonicalBoundary(long completionCaptureMilliseconds)
    {
        var payload = _transport.RawData;
        var lengthPrefixed = PacketTransportCodec.ProbeLengthPrefixedStreamBoundary(
            payload,
            PacketTransportCodec.MaximumEnvelopeBodyLength);
        if (lengthPrefixed.Kind == PacketLengthPrefixedBoundaryProbeKind.Ambiguous)
        {
            var tickOffset = lengthPrefixed.DirectRecoveryTickOffset;
            if (tickOffset >= 0 &&
                tickOffset <= payload.Length - 11 &&
                IsConfirmed0036(
                    payload.Slice(tickOffset, 11),
                    completionCaptureMilliseconds))
            {
                _transport.ActivateDirectRecovery();
                var canonicalStartOffset = checked(
                    lengthPrefixed.RawOffset + PacketTransportCodec.LengthPrefixedHeaderLength);
                _replayStartByteOffset = checked(
                    (int)(_transport.TotalRawConsumedByteCount + canonicalStartOffset));
                _transport.DiscardRawPrefix(canonicalStartOffset);
                _transport.MarkDirectCanonicalAlignment();
                _hasCanonicalAlignment = true;
                HasProtocolEvidence = true;
                _recoveryScanOffset = 0;
                _sentinelRecoveryScanOffset = 0;
                _consecutiveGameplayFrames = 0;
                return RecoveryResult.Confirmed;
            }

            _transport.DiscardRawPrefix(lengthPrefixed.RawOffset);
            _transport.ActivateLengthPrefixed();
            _replayStartByteOffset = checked((int)_transport.LengthPrefixedStartByteOffset);
            _recoveryScanOffset = 0;
            _sentinelRecoveryScanOffset = 0;
            _consecutiveGameplayFrames = 0;
            return RecoveryResult.Boundary;
        }

        var hasPendingLengthPrefixedBoundary =
            lengthPrefixed.Kind == PacketLengthPrefixedBoundaryProbeKind.Pending;
        if (lengthPrefixed.Kind == PacketLengthPrefixedBoundaryProbeKind.Complete)
        {
            _transport.DiscardRawPrefix(lengthPrefixed.RawOffset);
            _transport.ActivateLengthPrefixed(lengthPrefixed.CanonicalPrefixLength);
            _replayStartByteOffset = checked((int)_transport.LengthPrefixedStartByteOffset);
            _recoveryScanOffset = 0;
            _sentinelRecoveryScanOffset = 0;
            _consecutiveGameplayFrames = 0;
            return RecoveryResult.Boundary;
        }

        const int tickFrameLength = 11;
        var scanEnd = payload.Length - tickFrameLength;
        var pendingTickOffset = -1;
        for (var offset = _recoveryScanOffset; offset <= scanEnd; offset++)
        {
            if (IsConfirmed0036(payload.Slice(offset, tickFrameLength), completionCaptureMilliseconds))
            {
                var hasAmbiguousLengthPrefixedBoundary =
                    hasPendingLengthPrefixedBoundary ||
                    PacketTransportCodec.HasIncompleteLengthPrefixedEnvelopeContainingRange(
                        payload,
                        offset,
                        tickFrameLength,
                        PacketTransportCodec.MaximumEnvelopeBodyLength);
                if (hasAmbiguousLengthPrefixedBoundary &&
                    !IsCompleteCanonicalSequence(payload[..offset]) &&
                    !HasRecognizedCanonicalContinuation(payload[(offset + tickFrameLength)..]))
                {
                    pendingTickOffset = pendingTickOffset < 0
                        ? offset
                        : pendingTickOffset;
                    continue;
                }

                HasProtocolEvidence = true;
                _replayStartByteOffset = checked((int)(_transport.TotalRawConsumedByteCount + offset));
                return RecoveryResult.Confirmed;
            }
        }

        _recoveryScanOffset = pendingTickOffset >= 0
            ? pendingTickOffset
            : Math.Max(0, payload.Length - (tickFrameLength - 1));
        var pendingSentinelOffset = -1;
        var sentinelSearchOffset = _sentinelRecoveryScanOffset;
        while (sentinelSearchOffset <= payload.Length - PacketTransportCodec.Pattern.Length)
        {
            var relativeOffset = payload[sentinelSearchOffset..].IndexOf(PacketTransportCodec.Pattern);
            if (relativeOffset < 0)
            {
                break;
            }

            var sentinelOffset = checked(sentinelSearchOffset + relativeOffset);
            var hasAmbiguousSentinelBoundary =
                hasPendingLengthPrefixedBoundary ||
                PacketTransportCodec.HasIncompleteLengthPrefixedEnvelopeContainingRange(
                    payload,
                    sentinelOffset,
                    PacketTransportCodec.Pattern.Length,
                    PacketTransportCodec.MaximumEnvelopeBodyLength);
            if (hasAmbiguousSentinelBoundary &&
                !IsCompleteCanonicalSequence(payload[..sentinelOffset]) &&
                !HasRecognizedCanonicalContinuation(
                    payload[(sentinelOffset + PacketTransportCodec.Pattern.Length)..]))
            {
                pendingSentinelOffset = pendingSentinelOffset < 0
                    ? sentinelOffset
                    : pendingSentinelOffset;
                sentinelSearchOffset = checked(sentinelOffset + 1);
                continue;
            }

            _replayStartByteOffset = checked((int)(_transport.TotalRawConsumedByteCount + sentinelOffset));
            HasProtocolEvidence = true;
            var consumedLength = sentinelOffset + PacketTransportCodec.Pattern.Length;
            _transport.DiscardRawPrefix(consumedLength);
            _hasCanonicalAlignment = true;
            _recoveryScanOffset = 0;
            _sentinelRecoveryScanOffset = 0;
            _consecutiveGameplayFrames = 0;
            return RecoveryResult.Boundary;
        }

        _sentinelRecoveryScanOffset = pendingSentinelOffset >= 0
            ? pendingSentinelOffset
            : Math.Max(0, payload.Length - (PacketTransportCodec.Pattern.Length - 1));
        return RecoveryResult.Pending;
    }

    private static bool HasRecognizedCanonicalContinuation(ReadOnlySpan<byte> payload)
    {
        var frame = PacketTransportCodec.ProbeCanonicalFrame(payload, MaximumFrameLength);
        return frame.Kind == PacketCanonicalFrameProbeKind.Complete &&
               PacketTransportCodec.HasRecognizedCanonicalFrameStart(payload, in frame);
    }

    private static bool IsCompleteCanonicalSequence(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return false;
        }

        var offset = 0;
        while (offset < payload.Length)
        {
            var frame = PacketTransportCodec.ProbeCanonicalFrame(
                payload[offset..],
                MaximumFrameLength);
            if (frame.Kind != PacketCanonicalFrameProbeKind.Complete)
            {
                return false;
            }

            offset += frame.FrameLength;
        }

        return offset == payload.Length;
    }

    private void ConsumeFrame(int frameLength)
    {
        _transport.ConsumeCanonical(frameLength);
        _recoveryScanOffset = 0;
        _sentinelRecoveryScanOffset = 0;
    }

    private void EstablishCanonicalAlignment()
    {
        if (_hasCanonicalAlignment)
        {
            return;
        }

        _hasCanonicalAlignment = true;
        _replayStartByteOffset = checked((int)(_transport.IsLengthPrefixed
            ? _transport.LengthPrefixedStartByteOffset
            : _transport.TotalRawConsumedByteCount));
    }

    private void ObserveCanonicalProtocolEvidence()
    {
        if (HasProtocolEvidence || !_hasCanonicalAlignment)
        {
            return;
        }

        var data = _transport.CanonicalData;
        var frame = PacketTransportCodec.ProbeCanonicalFrame(data, MaximumFrameLength);
        if (frame.Kind == PacketCanonicalFrameProbeKind.Invalid ||
            frame.PrefixLength == 0 ||
            data.Length < frame.PrefixLength + sizeof(ushort))
        {
            return;
        }

        var opcodeOffset = frame.PrefixLength;
        HasProtocolEvidence = PacketTransportCodec.IsKnownGameplayOpcode(
            data[opcodeOffset],
            data[opcodeOffset + 1]);
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
            var probe = PacketTransportCodec.ProbeCanonicalFrame(payload[offset..], MaximumFrameLength);
            if (probe.Kind != PacketCanonicalFrameProbeKind.Complete)
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

            if (PacketTransportCodec.IsKnownGameplayOpcode(opcode0, opcode1))
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
}
