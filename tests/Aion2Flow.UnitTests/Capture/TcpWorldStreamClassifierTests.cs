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
}
