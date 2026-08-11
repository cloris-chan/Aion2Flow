using System.Buffers.Binary;
using Cloris.Aion2Flow.Capture.Streams;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class TcpWorldStreamClassifierTests
{
    [Fact]
    public void AlignedPartialGameplayFrameProvidesProtocolEvidenceWithoutConfirming()
    {
        var frame = BuildFrame(0x15, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(frame.AsSpan(0, 3), 1_000));
        Assert.True(classifier.HasProtocolEvidence);
    }

    [Fact]
    public void UnalignedPartialGameplayBytesDoNotProvideProtocolEvidence()
    {
        var frame = BuildFrame(0x15, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(frame.AsSpan(0, 3), 1_000));
        Assert.False(classifier.HasProtocolEvidence);
    }

    [Fact]
    public void EnablingMidstreamRecoveryAfterAnchoredBootstrapReopensBoundaryScan()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var bootstrap = BuildFrame(0x7a, 0x7b, new byte[32]);
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(bootstrap, captureMilliseconds));

        classifier.AllowMidstreamRecovery();

        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(Concat([0x7a, 0x7b, 0x7a], tick), captureMilliseconds));
    }

    [Fact]
    public void CompressedWeakGameplayFramesRequireTheDirectFrameEvidenceThreshold()
    {
        var firstGameplayFrame = BuildFrame(0x15, 0x36, new byte[32]);
        var secondGameplayFrame = BuildFrame(0x21, 0x36, new byte[32]);

        using var directClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, directClassifier.Append(firstGameplayFrame, 1_000));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, directClassifier.Append(secondGameplayFrame, 1_100));

        using var compressedClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            compressedClassifier.Append(BuildCompressedFrame(firstGameplayFrame), 1_000));
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            compressedClassifier.Append(BuildCompressedFrame(secondGameplayFrame), 1_100));
    }

    [Fact]
    public void CompressedStrongGameplayFrameConfirmsImmediately()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var body = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(body, captureMilliseconds - 100);
        var compressedTick = BuildCompressedFrame(BuildFrame(0x00, 0x36, body));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(compressedTick, captureMilliseconds));
    }

    [Fact]
    public void DirectAndCompressedServiceFramesAreRejectedWithoutAnEstablishedAlignment()
    {
        var serviceFrame = BuildFrame(0x01, 0x39, new byte[32]);

        using var directClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Rejected, directClassifier.Append(serviceFrame, 1_000));

        using var compressedClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Rejected,
            compressedClassifier.Append(BuildCompressedFrame(serviceFrame), 1_000));
    }

    [Fact]
    public void CompleteNonWorldCompressedPrefixDoesNotBlockLaterGameplayEvidence()
    {
        var unknownFrame = BuildFrame(0x7a, 0x7b, new byte[1020]);
        Assert.Equal(1024, unknownFrame.Length);
        var innerBatch = new byte[4 * 1024 * 1024];
        for (var offset = 0; offset < innerBatch.Length; offset += unknownFrame.Length)
        {
            unknownFrame.CopyTo(innerBatch, offset);
        }

        var compressedNonWorldBatch = BuildCompressedFrame(innerBatch);
        var firstGameplayFrame = BuildFrame(0x15, 0x36, new byte[32]);
        var secondGameplayFrame = BuildFrame(0x21, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(compressedNonWorldBatch, 1_000));
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(firstGameplayFrame, 1_100));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(secondGameplayFrame, 1_200));
        Assert.Equal(compressedNonWorldBatch.Length, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void CompleteInvalidCompressedPrefixDoesNotBlockLaterGameplayEvidence()
    {
        var invalidCompressedFrame = BuildInvalidCompressedFrame();
        var firstGameplayFrame = BuildFrame(0x15, 0x36, new byte[32]);
        var secondGameplayFrame = BuildFrame(0x21, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(invalidCompressedFrame, 1_000));
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(firstGameplayFrame, 1_100));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(secondGameplayFrame, 1_200));
        Assert.Equal(invalidCompressedFrame.Length, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void LengthPrefixedTickConfirmsFromTheEnvelopeHeader()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var body = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(body, captureMilliseconds - 100);
        var envelope = BuildLengthPrefixedEnvelope(BuildFrame(0x00, 0x36, body));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(envelope.AsSpan(0, 3), captureMilliseconds));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(envelope.AsSpan(3), captureMilliseconds));
        Assert.Equal(0, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void MidstreamLengthPrefixedRecoveryReplaysFromTheEnvelopeHeader()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int prefixLength = 9;
        var body = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(body, captureMilliseconds - 100);
        var envelope = BuildLengthPrefixedEnvelope(BuildFrame(0x00, 0x36, body));
        var payload = new byte[prefixLength + envelope.Length];
        payload.AsSpan(0, prefixLength).Fill(0x7a);
        envelope.CopyTo(payload, prefixLength);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(payload, captureMilliseconds));
        Assert.Equal(prefixLength, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void LengthPrefixedServiceFrameIsRejected()
    {
        var envelope = BuildLengthPrefixedEnvelope(BuildFrame(0x01, 0x39, new byte[32]));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Rejected, classifier.Append(envelope, 1_000));
    }

    [Fact]
    public void LengthPrefixedTickCanSpanMultipleEnvelopes()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var body = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(body, captureMilliseconds - 100);
        var tick = BuildFrame(0x00, 0x36, body);
        const int split = 5;
        var first = BuildLengthPrefixedEnvelope(tick.AsSpan(0, split));
        var second = BuildLengthPrefixedEnvelope(tick.AsSpan(split));
        var payload = new byte[first.Length + second.Length];
        first.CopyTo(payload, 0);
        second.CopyTo(payload, first.Length);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(payload, captureMilliseconds));
        Assert.Equal(0, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void MidstreamLengthPrefixedTickCrossingAnEnvelopeStaysLengthPrefixed()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int rawPrefixLength = 7;
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var payload = Concat(
            new byte[rawPrefixLength],
            BuildLengthPrefixedEnvelope(tick.AsSpan(0, 5)),
            BuildLengthPrefixedEnvelope(tick.AsSpan(5)));
        payload.AsSpan(0, rawPrefixLength).Fill(0x7a);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);

        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(payload, captureMilliseconds));
        Assert.Equal(rawPrefixLength, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void MidstreamLengthPrefixedRecoveryProtectsTickInsideEnvelopeBody()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int rawPrefixLength = 9;
        var oldFrame = BuildFrame(0x7a, 0x7b, new byte[24]);
        var continuation = oldFrame.AsSpan(oldFrame.Length - 7).ToArray();
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var second = BuildFrame(0x15, 0x36, new byte[32]);
        var third = BuildFrame(0x21, 0x36, new byte[32]);
        var firstEnvelope = BuildLengthPrefixedEnvelope(Concat(continuation, tick));
        var secondEnvelope = BuildLengthPrefixedEnvelope(second);
        var thirdEnvelope = BuildLengthPrefixedEnvelope(third);
        var firstAppend = new byte[rawPrefixLength + firstEnvelope.Length];
        firstAppend.AsSpan(0, rawPrefixLength).Fill(0x7a);
        firstEnvelope.CopyTo(firstAppend, rawPrefixLength);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(firstAppend, captureMilliseconds));
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(secondEnvelope, captureMilliseconds));
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(thirdEnvelope, captureMilliseconds));
        Assert.Equal(rawPrefixLength, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void IncompleteLengthPrefixedBodyDoesNotExposeEmbeddedTick()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int rawPrefixLength = 9;
        var oldFrame = BuildFrame(0x7a, 0x7b, new byte[24]);
        var continuation = oldFrame.AsSpan(oldFrame.Length - 7).ToArray();
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var unknown = BuildFrame(0x7a, 0x7b, new byte[128]);
        var second = BuildFrame(0x15, 0x36, new byte[32]);
        var third = BuildFrame(0x21, 0x36, new byte[32]);
        var firstEnvelope = BuildLengthPrefixedEnvelope(Concat(continuation, tick, unknown));
        var firstEnvelopeSplit = sizeof(int) + continuation.Length + tick.Length;
        var firstAppend = new byte[rawPrefixLength + firstEnvelopeSplit];
        firstAppend.AsSpan(0, rawPrefixLength).Fill(0x7a);
        firstEnvelope.AsSpan(0, firstEnvelopeSplit).CopyTo(firstAppend.AsSpan(rawPrefixLength));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(firstAppend, captureMilliseconds));
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(
                Concat(
                    firstEnvelope.AsSpan(firstEnvelopeSplit).ToArray(),
                    BuildLengthPrefixedEnvelope(second)),
                captureMilliseconds));
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(BuildLengthPrefixedEnvelope(third), captureMilliseconds));
        Assert.Equal(rawPrefixLength, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void MidstreamDirectEvidenceOverridesPlausibleLengthPrefixedCandidates()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int tickOffset = 20;
        var payload = new byte[tickOffset];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 8);
        payload.AsSpan(sizeof(int), 8).Fill(0x7a);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), 200_000);
        payload.AsSpan(16).Fill(0x7a);
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var gameplay = BuildFrame(0x15, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(Concat(payload, tick, gameplay), captureMilliseconds));
        Assert.Equal(tickOffset, classifier.ReplayStartByteOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectTickCrossingPlausibleEnvelopeHeaderConfirmsFromTheDirectTick(
        bool allowMidstreamRecovery)
    {
        const long captureMilliseconds = 0x000000000546AD01;
        var leadingFrame = BuildFrame(0x15, 0x36, new byte[392]);
        var crossingFrame = BuildFrame(0x23, 0x36, new byte[24]);
        crossingFrame[19] = 0xad;
        crossingFrame[20] = 0x46;
        crossingFrame[21] = 0x05;
        crossingFrame[22] = 0x00;
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds));
        var payload = Concat(
            [0x9f, 0x01, 0x00, 0x00],
            leadingFrame,
            crossingFrame,
            tick);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery);

        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(payload, captureMilliseconds));
        Assert.Equal(4, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void LaterCanonicalSentinelOverridesEarlierAmbiguousOccurrence()
    {
        const int canonicalSentinelOffset = 20;
        var prefix = new byte[canonicalSentinelOffset];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 200_000);
        prefix.AsSpan(sizeof(int)).Fill(0x7a);
        PacketTransportCodec.Pattern.CopyTo(prefix.AsSpan(8));
        var gameplay = BuildFrame(0x15, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(
                Concat(prefix, PacketTransportCodec.Pattern.ToArray(), gameplay),
                1_800_000_000_000));
        Assert.Equal(canonicalSentinelOffset, classifier.ReplayStartByteOffset);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(BuildFrame(0x21, 0x36, new byte[32]), 1_800_000_000_100));
    }

    [Fact]
    public void LaterCanonicalSentinelOverridesEarlierAmbiguousTick()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int canonicalSentinelOffset = 28;
        var prefix = new byte[canonicalSentinelOffset];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 200_000);
        prefix.AsSpan(sizeof(int)).Fill(0x7a);
        BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100)).CopyTo(prefix, 8);
        var gameplay = BuildFrame(0x15, 0x36, new byte[32]);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(
                Concat(prefix, PacketTransportCodec.Pattern.ToArray(), gameplay),
                captureMilliseconds));
        Assert.Equal(canonicalSentinelOffset, classifier.ReplayStartByteOffset);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(BuildFrame(0x21, 0x36, new byte[32]), captureMilliseconds + 100));
    }

    [Fact]
    public void LaterFixedBoundaryOverridesUnrelatedPendingCandidate()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        const int fixedOffset = 12;
        var prefix = new byte[fixedOffset];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 4);
        prefix.AsSpan(sizeof(int), 4).Fill(0x7a);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(8), 200_000);
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(
                Concat(prefix, BuildLengthPrefixedEnvelope(tick)),
                captureMilliseconds));
        Assert.Equal(fixedOffset, classifier.ReplayStartByteOffset);
    }

    [Fact]
    public void LengthPrefixedCompressedTickConfirmsImmediately()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var compressedTick = BuildCompressedFrame(
            BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100)));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(BuildLengthPrefixedEnvelope(compressedTick), captureMilliseconds));
    }

    [Fact]
    public void TlsLookingLengthPrefixConfirmsFixedWorldStream()
    {
        const int envelopeBodyLength = 0x00010316;
        const long captureMilliseconds = 1_800_000_000_000;
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var filler = BuildFrame(0x15, 0x36, new byte[envelopeBodyLength - tick.Length - 5]);
        var envelope = BuildLengthPrefixedEnvelope(Concat(tick, filler));
        Assert.Equal(envelopeBodyLength, envelope.Length - sizeof(int));
        Assert.True(CapturedNonAionPayload.IsNonGameConnectionStart(envelope.AsSpan(0, 5)));
        Assert.True(TcpWorldStreamClassifier.IsPlausibleConnectionStart(envelope.AsSpan(0, 5)));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            classifier.Append(envelope.AsSpan(0, 5), captureMilliseconds));
        Assert.Equal(
            TcpWorldStreamClassification.Confirmed,
            classifier.Append(envelope.AsSpan(5), captureMilliseconds));
    }

    [Fact]
    public void ReplayStartClassifierPreservesTlsLookingFixedEnvelope()
    {
        const int envelopeBodyLength = 0x00010316;
        var first = BuildFrame(0x15, 0x36, new byte[32]);
        var filler = BuildFrame(0x21, 0x36, new byte[envelopeBodyLength - first.Length - 5]);
        var envelope = BuildLengthPrefixedEnvelope(Concat(first, filler));
        using var classifier = new TcpConnectionStartClassifier();

        Assert.Equal(
            TcpConnectionStartKind.Pending,
            classifier.Classify(envelope.AsSpan(0, 5)).Kind);
        var result = classifier.Classify(envelope.AsSpan(5));
        try
        {
            Assert.Equal(TcpConnectionStartKind.Game, result.Kind);
            Assert.Equal(PacketTransportFraming.LengthPrefixed, result.Framing);
            Assert.Equal(envelope, result.ResolveAcceptedPayload(default).ToArray());
        }
        finally
        {
            result.Return();
        }
    }

    [Fact]
    public void ReplayStartClassifierRejectsNonCollidingTlsHeader()
    {
        byte[] tlsRecord = [0x17, 0x03, 0x03, 0x40, 0x00, 0x00];
        using var classifier = new TcpConnectionStartClassifier();

        Assert.Equal(TcpConnectionStartKind.NonGame, classifier.Classify(tlsRecord).Kind);
    }

    [Fact]
    public void ReplayStartClassifierDistinguishesAlignedAndRecoveringDirectStreams()
    {
        using var alignedClassifier = new TcpConnectionStartClassifier();
        var aligned = alignedClassifier.Classify(BuildFrame(0x15, 0x36, new byte[32]));
        Assert.Equal(TcpConnectionStartKind.Game, aligned.Kind);
        Assert.Equal(PacketTransportFraming.DirectAligned, aligned.Framing);

        using var recoveryClassifier = new TcpConnectionStartClassifier();
        byte[] midstream = [0xff, 0xff, 0xff, 0x7f, 0x00];
        var recovering = recoveryClassifier.Classify(midstream);
        Assert.Equal(TcpConnectionStartKind.Game, recovering.Kind);
        Assert.Equal(PacketTransportFraming.DirectRecovery, recovering.Framing);
    }

    [Fact]
    public void ReplayStartClassifierResolvesTheCrossingTickBeforeChoosingTransportFraming()
    {
        const long captureMilliseconds = 0x000000000546AD01;
        var leadingFrame = BuildFrame(0x15, 0x36, new byte[392]);
        var crossingFrame = BuildFrame(0x23, 0x36, new byte[24]);
        crossingFrame[19] = 0xad;
        crossingFrame[20] = 0x46;
        crossingFrame[21] = 0x05;
        crossingFrame[22] = 0x00;
        var directTick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds));
        var directPayload = Concat(
            [0x9f, 0x01, 0x00, 0x00],
            leadingFrame,
            crossingFrame,
            directTick);
        using var directClassifier = new TcpConnectionStartClassifier();
        var direct = directClassifier.Classify(directPayload, captureMilliseconds);
        try
        {
            Assert.Equal(TcpConnectionStartKind.Game, direct.Kind);
            Assert.Equal(PacketTransportFraming.DirectAligned, direct.Framing);
            Assert.Equal(PacketTransportCodec.LengthPrefixedHeaderLength, direct.TransportPrefixLength);
        }
        finally
        {
            direct.Return();
        }

        var fixedTick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 100));
        var fixedPayload = Concat(
            BuildLengthPrefixedEnvelope(fixedTick.AsSpan(0, 5)),
            BuildLengthPrefixedEnvelope(fixedTick.AsSpan(5)));
        using var fixedClassifier = new TcpConnectionStartClassifier();
        var fixedResult = fixedClassifier.Classify(fixedPayload, captureMilliseconds);
        try
        {
            Assert.Equal(TcpConnectionStartKind.Game, fixedResult.Kind);
            Assert.Equal(PacketTransportFraming.LengthPrefixed, fixedResult.Framing);
        }
        finally
        {
            fixedResult.Return();
        }
    }

    private static byte[] BuildCompressedFrame(ReadOnlySpan<byte> inner)
    {
        var compressedBuffer = new byte[LZ4Codec.MaximumOutputSize(inner.Length)];
        var compressedLength = LZ4Codec.Encode(inner, compressedBuffer);
        Assert.True(compressedLength > 0);

        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(compressedLength + 10, prefix, out var prefixLength));
        var frame = new byte[prefixLength + 6 + compressedLength];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = 0xff;
        frame[prefixLength + 1] = 0xff;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(prefixLength + 2), inner.Length);
        compressedBuffer.AsSpan(0, compressedLength).CopyTo(frame.AsSpan(prefixLength + 6));
        return frame;
    }

    private static byte[] BuildInvalidCompressedFrame()
    {
        const int compressedLength = 1;
        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(compressedLength + 10, prefix, out var prefixLength));
        var frame = new byte[prefixLength + 6 + compressedLength];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = 0xff;
        frame[prefixLength + 1] = 0xff;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(prefixLength + 2), 4 * 1024 * 1024);
        return frame;
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

    private static byte[] BuildLengthPrefixedEnvelope(ReadOnlySpan<byte> body)
    {
        var envelope = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(envelope, body.Length);
        body.CopyTo(envelope.AsSpan(sizeof(int)));
        return envelope;
    }

    private static byte[] WriteInt64(long value)
    {
        var result = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(result, value);
        return result;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(static part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
