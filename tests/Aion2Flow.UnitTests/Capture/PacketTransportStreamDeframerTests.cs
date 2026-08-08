using System.Buffers.Binary;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketTransportStreamDeframerTests
{
    private const long YearOneToUnixEpochMilliseconds = 62_135_596_800_000;
    private static readonly TcpConnection Connection = new(1, 2, 3, 4);

    [Fact]
    public void FixedLengthEnvelopeExposesCanonicalFrame()
    {
        var frame = BuildFrame(0x15, 0x36, new byte[32]);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(frame));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsLengthPrefixed);
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FixedLengthHeaderCanBeSplitAtEveryBoundary(int split)
    {
        var frame = BuildFrame(0x21, 0x36, new byte[32]);
        var envelope = BuildEnvelope(frame);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(envelope.AsSpan(0, split));

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);

        deframer.Append(envelope.AsSpan(split));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsLengthPrefixed);
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void FixedLengthBodyCanBeSplitAcrossAppends()
    {
        var frame = BuildFrame(0x23, 0x36, new byte[64]);
        var envelope = BuildEnvelope(frame);
        var split = PacketTransportCodec.LengthPrefixedHeaderLength + frame.Length / 2;

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(envelope.AsSpan(0, split));

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());

        deframer.Append(envelope.AsSpan(split));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void ConsumedEnvelopeHeaderKeepsTheTransportPendingUntilItsBodyArrives()
    {
        var first = BuildFrame(0x15, 0x36, new byte[16]);
        var second = BuildFrame(0x21, 0x36, new byte[32]);
        var secondEnvelope = BuildEnvelope(second);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(first));
        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        deframer.ConsumeCanonical(first.Length);

        deframer.Append(secondEnvelope.AsSpan(0, PacketTransportCodec.LengthPrefixedHeaderLength));

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());
        Assert.True(deframer.HasPendingData);

        deframer.Append(secondEnvelope.AsSpan(PacketTransportCodec.LengthPrefixedHeaderLength));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.Equal(second, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void CanonicalFrameCanSpanFixedLengthEnvelopes()
    {
        var frame = BuildFrame(0x40, 0x36, new byte[256]);
        var split = frame.Length / 2;
        var payload = Concat(
            BuildEnvelope(frame.AsSpan(0, split)),
            BuildEnvelope(frame.AsSpan(split)));

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(payload);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.Equal(split, deframer.CanonicalData.Length);
        Assert.True(deframer.TryExpandCanonicalData());
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void CanonicalFrameCanSpanFixedLengthEnvelopesAcrossAppends()
    {
        var frame = BuildFrame(0x40, 0x36, new byte[256]);
        var split = frame.Length / 2;

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(frame.AsSpan(0, split)));

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());

        deframer.Append(BuildEnvelope(frame.AsSpan(split)));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.Equal(split, deframer.CanonicalData.Length);
        Assert.True(deframer.TryExpandCanonicalData());
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void MultipleFixedLengthEnvelopesCanArriveInOneAppend()
    {
        var first = BuildFrame(0x15, 0x36, new byte[24]);
        var second = BuildFrame(0x21, 0x36, new byte[48]);
        var third = BuildFrame(0x23, 0x36, new byte[72]);
        var payload = Concat(
            BuildEnvelope(first),
            BuildEnvelope(second),
            BuildEnvelope(third));

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(payload);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.TryExpandCanonicalData());
        Assert.True(deframer.TryExpandCanonicalData());
        Assert.Equal(Concat(first, second, third), deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void DirectTickCanPrecedeFixedLengthEnvelopes()
    {
        var tick = BuildFrame(0x00, 0x36, new byte[sizeof(long)]);
        var fixedFrame = BuildFrame(0x15, 0x36, new byte[32]);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(Concat(tick, BuildEnvelope(fixedFrame)));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);
        Assert.True(deframer.CanonicalData.StartsWith(tick));

        deframer.ConsumeCanonical(tick.Length);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsLengthPrefixed);
        Assert.Equal((long)tick.Length, deframer.LengthPrefixedStartByteOffset);
        Assert.Equal(fixedFrame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void FixedLengthCompressedFrameReachesPacketProcessor()
    {
        const long clientSentUnixMilliseconds = 1_800_000_000_000;
        const long serverUnixMilliseconds = clientSentUnixMilliseconds + 25;
        const long arrivalTimestamp = 123_456_789;
        var inner = Build0336(clientSentUnixMilliseconds, serverUnixMilliseconds);
        var compressed = BuildCompressedFrame(inner);
        ProtocolRoundTripObservation? observation = null;
        using var processor = new PacketStreamProcessor(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel())(),
            value => observation = value);
        var timestamp = new PacketProcessingTimestamp(
            serverUnixMilliseconds,
            arrivalTimestamp);

        Assert.True(processor.AppendAndProcess(
            BuildEnvelope(compressed),
            in Connection,
            in timestamp));
        Assert.True(observation.HasValue);
        Assert.Equal(clientSentUnixMilliseconds, observation.Value.ClientSentUnixMilliseconds);
        Assert.Equal(serverUnixMilliseconds, observation.Value.ServerUnixMilliseconds);
        Assert.Equal(arrivalTimestamp, observation.Value.ArrivalTimestamp);
    }

    [Fact]
    public void ParsedFrameCompletesFlushWithTrailingPartialEnvelopeHeader()
    {
        const long clientSentUnixMilliseconds = 1_800_000_000_000;
        const long serverUnixMilliseconds = clientSentUnixMilliseconds + 25;
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(
            SceneSinkFactory.CreateForLive(scene)(),
            null);
        var frame = Build0336(clientSentUnixMilliseconds, serverUnixMilliseconds);
        var nextEnvelope = BuildEnvelope(BuildFrame(0x15, 0x36, new byte[16]));

        Assert.True(processor.AppendAndProcess(
            Concat(BuildEnvelope(frame), nextEnvelope[..2]),
            in Connection,
            serverUnixMilliseconds));
        Assert.True(scene.Journal.LastCompletedFlushId > 0);
    }

    [Fact]
    public void CompleteDirectCanonicalFrameDoesNotLockFalseFixedLengthPrefix()
    {
        var frame = BuildFrame(0x00, 0x36, new byte[16_384]);
        Assert.Equal(0x86, frame[0]);
        Assert.Equal(0x80, frame[1]);
        Assert.Equal(0x01, frame[2]);
        Assert.Equal(0x00, frame[3]);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(frame);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void PlausibleUInt32HeaderChainWithoutCanonicalEvidenceWaitsForDisproof()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), 4);
        payload.AsSpan(4, 4).Fill(0x7a);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), 4);
        payload.AsSpan(12, 4).Fill(0x7a);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), 4);
        payload.AsSpan(20, 4).Fill(0x7a);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(payload);

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);

        byte[] invalidHeader = [0xff, 0xff, 0xff, 0x7f];
        deframer.Append(invalidHeader);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);
        Assert.Equal(Concat(payload, invalidHeader), deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void MaximumFixedLengthEnvelopeFitsTheTransportBuffer()
    {
        var frame = BuildFrame(
            0x15,
            0x36,
            new byte[PacketTransportCodec.MaximumEnvelopeBodyLength - 5]);
        Assert.Equal(PacketTransportCodec.MaximumEnvelopeBodyLength, frame.Length);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(frame));

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsLengthPrefixed);
        Assert.Equal(frame.Length, deframer.CanonicalData.Length);
    }

    [Fact]
    public void LockedLengthPrefixedModeAcceptsTinyEnvelopeBodies()
    {
        var first = BuildFrame(0x15, 0x36, new byte[16]);
        var second = BuildFrame(0x21, 0x36, new byte[32]);

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(first));
        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        deframer.ConsumeCanonical(first.Length);

        deframer.Append(BuildEnvelope(second.AsSpan(0, 1)));
        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        deframer.Append(BuildEnvelope(second.AsSpan(1, 2)));
        Assert.True(deframer.TryExpandCanonicalData());
        deframer.Append(BuildEnvelope(second.AsSpan(3)));
        Assert.True(deframer.TryExpandCanonicalData());

        Assert.Equal(second, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void InitialTinyEnvelopeBodiesCanEstablishLengthPrefixedMode()
    {
        var canonical = Concat(
            BuildFrame(0x15, 0x36, new byte[16]),
            BuildFrame(0x21, 0x36, new byte[32]));
        var payload = Concat(
            BuildEnvelope(canonical.AsSpan(0, 1)),
            BuildEnvelope(canonical.AsSpan(1, 2)),
            BuildEnvelope(canonical.AsSpan(3)));

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(payload);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsLengthPrefixed);
        while (deframer.TryExpandCanonicalData())
        {
        }

        Assert.Equal(canonical, deframer.CanonicalData.ToArray());
    }

    [Fact]
    public void InvalidEnvelopeLengthFaultsLockedTransportAndDropsLaterData()
    {
        var frame = BuildFrame(0x15, 0x36, new byte[16]);
        var invalidHeader = new byte[PacketTransportCodec.LengthPrefixedHeaderLength];

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(BuildEnvelope(frame));
        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        deframer.ConsumeCanonical(frame.Length);

        deframer.Append(invalidHeader);

        Assert.Equal(PacketTransportDataAvailability.Invalid, deframer.PrepareCanonicalData());
        Assert.True(deframer.IsFaulted);
        Assert.False(deframer.HasPendingData);

        deframer.Append(BuildEnvelope(frame));

        Assert.Equal(PacketTransportDataAvailability.Invalid, deframer.PrepareCanonicalData());
        Assert.True(deframer.CanonicalData.IsEmpty);
    }

    [Fact]
    public void OversizedAppendFaultsWithoutDiscardingIntoAFalseBoundary()
    {
        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(new byte[CaptureBufferLimits.TransportRawBufferSize + 1]);

        Assert.True(deframer.IsFaulted);
        Assert.False(deframer.HasPendingData);
        Assert.Equal(PacketTransportDataAvailability.Invalid, deframer.PrepareCanonicalData());
    }

    [Fact]
    public void MaximumCanonicalFrameCanSpanTwoFixedLengthEnvelopes()
    {
        var frame = BuildFrame(
            0x15,
            0x36,
            new byte[CaptureBufferLimits.StreamTailBufferSize - 5]);
        var firstEnvelope = BuildEnvelope(frame.AsSpan(0, PacketTransportCodec.MaximumEnvelopeBodyLength));
        var secondEnvelope = BuildEnvelope(frame.AsSpan(PacketTransportCodec.MaximumEnvelopeBodyLength));

        using var deframer = new PacketTransportStreamDeframer();
        deframer.Append(firstEnvelope);

        Assert.Equal(PacketTransportDataAvailability.NeedMore, deframer.PrepareCanonicalData());
        Assert.False(deframer.IsLengthPrefixed);

        deframer.Append(secondEnvelope);

        Assert.Equal(PacketTransportDataAvailability.Available, deframer.PrepareCanonicalData());
        Assert.True(deframer.TryExpandCanonicalData());
        Assert.Equal(frame, deframer.CanonicalData.ToArray());
    }

    private static byte[] Build0336(long clientSentUnixMilliseconds, long serverUnixMilliseconds)
    {
        var body = new byte[18];
        BinaryPrimitives.WriteInt64LittleEndian(
            body.AsSpan(2),
            YearOneToUnixEpochMilliseconds + clientSentUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(10), serverUnixMilliseconds);
        return BuildFrame(0x03, 0x36, body);
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

    private static byte[] BuildEnvelope(ReadOnlySpan<byte> body)
    {
        var envelope = new byte[PacketTransportCodec.LengthPrefixedHeaderLength + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(envelope, body.Length);
        body.CopyTo(envelope.AsSpan(PacketTransportCodec.LengthPrefixedHeaderLength));
        return envelope;
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
