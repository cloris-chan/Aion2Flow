using System.Buffers.Binary;
using System.Diagnostics;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class TcpWorldConnectionCandidatePriorityTests
{
    [Theory]
    [InlineData(false, false, (byte)CandidateConnectionPriority.UnknownInbound)]
    [InlineData(true, false, (byte)CandidateConnectionPriority.ObservedHandshake)]
    [InlineData(false, true, (byte)CandidateConnectionPriority.KnownProcess)]
    [InlineData(true, true, (byte)CandidateConnectionPriority.KnownProcess)]
    public void CandidatePrioritySeparatesTransportAndProcessEvidence(
        bool isExpectedDownstream,
        bool isKnownProcessPort,
        byte expectedPriorityValue)
    {
        Assert.Equal(
            (CandidateConnectionPriority)expectedPriorityValue,
            WinDivertCaptureService.ResolveCandidatePriority(isExpectedDownstream, isKnownProcessPort));
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, false)]
    public void ActivePayloadClassificationAccountsForCompletedPromotion(
        bool isExpectedDownstream,
        bool hasPendingPromotion,
        bool hasCurrentActiveAdmission,
        bool expected)
    {
        Assert.Equal(
            expected,
            WinDivertCaptureService.ShouldClassifyActivePayload(
                isExpectedDownstream,
                hasPendingPromotion,
                hasCurrentActiveAdmission));
    }

    [Theory]
    [InlineData((byte)CandidateConnectionPriority.KnownProcess)]
    [InlineData((byte)CandidateConnectionPriority.ProtocolEvidence)]
    public void UnknownCandidateCountFloodCannotEvictTrustedCandidate(
        byte trustedPriorityValue)
    {
        var trustedPriority = (CandidateConnectionPriority)trustedPriorityValue;
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var trustedConnection = CreateConnection(0);
        const uint trustedSequenceNumber = 10_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    trustedConnection,
                    frame.AsSpan(0, 4),
                    trustedSequenceNumber,
                    connectionOrdinal: 1,
                    trustedPriority,
                    observedTimestamp,
                    initialSequenceNumber: trustedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));

            for (var index = 1; index <= CaptureBufferLimits.CandidateStreamCountLimit; index++)
            {
                Assert.Equal(
                    CandidatePacketDisposition.Buffered,
                    Add(
                        candidates,
                        CreateConnection(index),
                        frame.AsSpan(0, 4),
                        sequenceNumber: 20_000,
                        connectionOrdinal: index + 1,
                        CandidateConnectionPriority.UnknownInbound,
                        observedTimestamp + index,
                        initialSequenceNumber: null,
                        allowMidstreamRecovery: true,
                        out _));
            }

            Assert.True(candidates.Contains(in trustedConnection));
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    trustedConnection,
                    frame.AsSpan(4),
                    trustedSequenceNumber + 4,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    observedTimestamp + CaptureBufferLimits.CandidateStreamCountLimit + 1,
                    initialSequenceNumber: trustedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void ExistingCandidatePriorityCanBeUpgradedButNotDowngraded()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var trustedConnection = CreateConnection(0);
        const uint trustedSequenceNumber = 10_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    trustedConnection,
                    frame.AsSpan(0, 2),
                    trustedSequenceNumber,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    observedTimestamp,
                    initialSequenceNumber: trustedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    trustedConnection,
                    frame.AsSpan(2, 2),
                    trustedSequenceNumber + 2,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.KnownProcess,
                    observedTimestamp + 1,
                    initialSequenceNumber: trustedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));

            for (var index = 1; index <= CaptureBufferLimits.CandidateStreamCountLimit; index++)
            {
                Assert.Equal(
                    CandidatePacketDisposition.Buffered,
                    Add(
                        candidates,
                        CreateConnection(index),
                        frame.AsSpan(0, 4),
                        sequenceNumber: 20_000,
                        connectionOrdinal: index + 1,
                        CandidateConnectionPriority.UnknownInbound,
                        observedTimestamp + index + 1,
                        initialSequenceNumber: null,
                        allowMidstreamRecovery: true,
                        out _));
            }

            Assert.True(candidates.Contains(in trustedConnection));
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    trustedConnection,
                    frame.AsSpan(4),
                    trustedSequenceNumber + 4,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    observedTimestamp + CaptureBufferLimits.CandidateStreamCountLimit + 2,
                    initialSequenceNumber: trustedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void NewerAttemptReplacesOlderSameTupleCandidateAndCannotBeReplacedBack()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var connection = CreateConnection(0);
        const uint oldSequenceNumber = 10_000;
        const uint newSequenceNumber = 20_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(0, 4),
                    oldSequenceNumber,
                    connectionOrdinal: 10,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp,
                    initialSequenceNumber: oldSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(0, 4),
                    newSequenceNumber,
                    connectionOrdinal: 11,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 1,
                    initialSequenceNumber: newSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));
            Assert.True(candidates.TryGetOrdinal(in connection, out var retainedOrdinal));
            Assert.Equal(11, retainedOrdinal);

            Assert.Equal(
                CandidatePacketDisposition.Discarded,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(4),
                    oldSequenceNumber + 4,
                    connectionOrdinal: 10,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 2,
                    initialSequenceNumber: oldSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));
            Assert.True(candidates.TryGetOrdinal(in connection, out retainedOrdinal));
            Assert.Equal(11, retainedOrdinal);

            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(4),
                    newSequenceNumber + 4,
                    connectionOrdinal: 11,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 3,
                    initialSequenceNumber: newSequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(11, promotion.CandidateOrdinal);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void ResetOlderThanKeepsCandidateForTheCurrentAttempt()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var connection = CreateConnection(0);
        using var candidates = new TcpWorldConnectionCandidateTracker();

        Assert.Equal(
            CandidatePacketDisposition.Buffered,
            Add(
                candidates,
                connection,
                frame.AsSpan(0, 4),
                sequenceNumber: 10_000,
                connectionOrdinal: 11,
                CandidateConnectionPriority.ObservedHandshake,
                Stopwatch.GetTimestamp(),
                initialSequenceNumber: 10_000,
                allowMidstreamRecovery: false,
                out _));

        candidates.ResetOlderThan(in connection, newerConnectionOrdinal: 10);
        Assert.True(candidates.Contains(in connection));

        candidates.ResetOlderThan(in connection, newerConnectionOrdinal: 12);
        Assert.False(candidates.Contains(in connection));
    }

    [Fact]
    public void UnknownCandidateByteFloodCannotEvictProtocolEvidenceCandidate()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var expectedConnection = CreateConnection(0);
        const uint expectedSequenceNumber = 10_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    expectedConnection,
                    frame.AsSpan(0, 4),
                    expectedSequenceNumber,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp,
                    initialSequenceNumber: expectedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));

            var pendingPayload = new byte[17_000];
            pendingPayload.AsSpan().Fill(0x01);
            for (var index = 1; index <= 62; index++)
            {
                Assert.Equal(
                    CandidatePacketDisposition.Buffered,
                    Add(
                        candidates,
                        CreateConnection(index),
                        pendingPayload,
                        sequenceNumber: 20_000,
                        connectionOrdinal: index + 1,
                        CandidateConnectionPriority.UnknownInbound,
                        observedTimestamp + index,
                        initialSequenceNumber: null,
                        allowMidstreamRecovery: true,
                        out _));
            }

            Assert.True(candidates.Contains(in expectedConnection));
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    expectedConnection,
                    frame.AsSpan(4),
                    expectedSequenceNumber + 4,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 63,
                    initialSequenceNumber: expectedSequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void ObservedHandshakeFloodCannotEvictCandidateWithProtocolEvidence()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var worldConnection = CreateConnection(0);
        const uint worldSequenceNumber = 10_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    worldConnection,
                    frame.AsSpan(0, 3),
                    worldSequenceNumber,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp,
                    initialSequenceNumber: worldSequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));

            for (var index = 1; index <= CaptureBufferLimits.CandidateStreamCountLimit; index++)
            {
                Assert.Equal(
                    CandidatePacketDisposition.Buffered,
                    Add(
                        candidates,
                        CreateConnection(index),
                        frame.AsSpan(0, 1),
                        sequenceNumber: 20_000,
                        connectionOrdinal: index + 1,
                        CandidateConnectionPriority.ObservedHandshake,
                        observedTimestamp + index,
                        initialSequenceNumber: 20_000,
                        allowMidstreamRecovery: false,
                        out _));
            }

            Assert.True(candidates.Contains(in worldConnection));
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    worldConnection,
                    frame.AsSpan(3),
                    worldSequenceNumber + 3,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + CaptureBufferLimits.CandidateStreamCountLimit + 1,
                    initialSequenceNumber: worldSequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void ExactRetransmissionsDoNotConsumeCandidateBufferLimitsOrReplaySlots()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var largeFrame = BuildFrame(0x15, 0x36, new byte[40_000]);
        var tickFrame = Build0036Frame(captureMilliseconds);
        var firstSegment = largeFrame.AsSpan(0, largeFrame.Length - 1).ToArray();
        var finalSegment = new byte[1 + tickFrame.Length];
        finalSegment[0] = largeFrame[^1];
        tickFrame.CopyTo(finalSegment.AsSpan(1));
        var connection = CreateConnection(0);
        const uint sequenceNumber = 10_000;
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    connection,
                    firstSegment,
                    sequenceNumber,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp,
                    initialSequenceNumber: sequenceNumber,
                    allowMidstreamRecovery: false,
                    out _));

            for (var index = 1; index <= CaptureBufferLimits.CandidateStreamSegmentLimit + 32; index++)
            {
                Assert.Equal(
                    CandidatePacketDisposition.Buffered,
                    Add(
                        candidates,
                        connection,
                        firstSegment,
                        sequenceNumber,
                        connectionOrdinal: 1,
                        CandidateConnectionPriority.ObservedHandshake,
                        observedTimestamp + index,
                        initialSequenceNumber: sequenceNumber,
                        allowMidstreamRecovery: false,
                        out _));
            }

            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    connection,
                    finalSegment,
                    sequenceNumber + (uint)firstSegment.Length,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + CaptureBufferLimits.CandidateStreamSegmentLimit + 33,
                    initialSequenceNumber: sequenceNumber,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(2, promotion.Packets.Count);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void DefinitiveNonWorldStartsAreRemovedImmediately()
    {
        var payloads = new[]
        {
            new byte[] { 0x16, 0x03, 0x03, 0x00, 0x00 },
            "GET / HTTP/1.1\r\n\r\n"u8.ToArray(),
            "SSH-2.0-test\r\n"u8.ToArray(),
            BuildFrame(0x00, 0x39, ReadOnlySpan<byte>.Empty)
        };
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();

        for (var index = 0; index < payloads.Length; index++)
        {
            var connection = CreateConnection(index);
            Assert.Equal(
                CandidatePacketDisposition.Discarded,
                Add(
                    candidates,
                    connection,
                    payloads[index],
                    sequenceNumber: 10_000,
                    connectionOrdinal: index + 1,
                    CandidateConnectionPriority.UnknownInbound,
                    observedTimestamp + index,
                    initialSequenceNumber: null,
                    allowMidstreamRecovery: true,
                    out _));
            Assert.False(candidates.Contains(in connection));
        }
    }

    [Fact]
    public void UnanchoredCandidateCanStillRecoverPastMissingPrefixBytes()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var payload = new byte[5 + frame.Length];
        "noise"u8.CopyTo(payload);
        frame.CopyTo(payload.AsSpan(5));
        var connection = CreateConnection(0);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    connection,
                    payload,
                    sequenceNumber: 10_000,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    Stopwatch.GetTimestamp(),
                    initialSequenceNumber: null,
                    allowMidstreamRecovery: true,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(10_005u, promotion.ReplayStartSequenceNumber);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void LargeUnknownBootstrapIsRetainedUntilLaterProtocolEvidence()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var unknownFrame = BuildFrame(0x7a, 0x7b, new byte[116_000]);
        var tickFrame = Build0036Frame(captureMilliseconds);
        var connection = CreateConnection(0);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    connection,
                    unknownFrame,
                    sequenceNumber: 10_000,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    Stopwatch.GetTimestamp(),
                    initialSequenceNumber: null,
                    allowMidstreamRecovery: true,
                    out _));
            Assert.True(candidates.Contains(in connection));

            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    connection,
                    tickFrame,
                    sequenceNumber: 10_000u + (uint)unknownFrame.Length,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.UnknownInbound,
                    Stopwatch.GetTimestamp(),
                    initialSequenceNumber: null,
                    allowMidstreamRecovery: true,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(2, promotion.Packets.Count);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void ObservedHandshakeCandidateIsRetainedUntilProtocolEvidenceArrives()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0036Frame(captureMilliseconds);
        var connection = CreateConnection(0);
        var observedTimestamp = Stopwatch.GetTimestamp();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(0, 4),
                    sequenceNumber: 10_000,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp,
                    initialSequenceNumber: 10_000,
                    allowMidstreamRecovery: false,
                    out _));

            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                Add(
                    candidates,
                    connection,
                    frame.AsSpan(4),
                    sequenceNumber: 10_004,
                    connectionOrdinal: 1,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + Stopwatch.Frequency * 31L,
                    initialSequenceNumber: 10_000,
                    allowMidstreamRecovery: false,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(2, promotion.Packets.Count);
        }
        finally
        {
            promotion?.Return();
        }
    }

    private static CandidatePacketDisposition Add(
        TcpWorldConnectionCandidateTracker candidates,
        TcpConnection connection,
        ReadOnlySpan<byte> payload,
        uint sequenceNumber,
        long connectionOrdinal,
        CandidateConnectionPriority priority,
        long observedTimestamp,
        uint? initialSequenceNumber,
        bool allowMidstreamRecovery,
        out CaptureConnectionPromotion? promotion)
    {
        var packet = CapturedPacket.CreateCopy(
            connection,
            new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
            payload,
            sequenceNumber,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return candidates.Add(
            packet,
            allowNewCandidate: true,
            allowMidstreamRecovery,
            initialSequenceNumber,
            connectionOrdinal,
            priority,
            observedTimestamp,
            out promotion);
    }

    private static TcpConnection CreateConnection(int index) =>
        new(
            SourceAddress: 0x0100000A + (uint)index,
            DestinationAddress: 0x0200000A,
            SourcePort: (ushort)(21_000 + index),
            DestinationPort: 49_628);

    private static byte[] Build0036Frame(long captureMilliseconds)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, captureMilliseconds);
        return BuildFrame(0x00, 0x36, bytes);
    }

    private static byte[] BuildFrame(byte opcode0, byte opcode1, ReadOnlySpan<byte> body)
    {
        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(body.Length + 6, prefix, out var prefixLength));
        var frame = new byte[prefixLength + sizeof(ushort) + body.Length];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = opcode0;
        frame[prefixLength + 1] = opcode1;
        body.CopyTo(frame.AsSpan(prefixLength + sizeof(ushort)));
        return frame;
    }
}
