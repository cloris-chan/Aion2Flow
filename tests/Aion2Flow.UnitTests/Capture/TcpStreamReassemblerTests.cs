using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class TcpStreamReassemblerTests
{
    [Fact]
    public void Emits_InOrder_Payload_Immediately()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2, 3], At(1_000), ref collector, Capture);

        Assert.Equal([100u], collector.SequenceNumbers);
        Assert.Equal([1, 2, 3], collector.Payloads.Single());
    }

    [Fact]
    public void Buffers_OutOfOrder_Payload_Until_Gap_Is_Filled()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2], At(1_000), ref collector, Capture);
        reassembler.Feed(104, [5, 6], At(3_000), ref collector, Capture);
        reassembler.Feed(102, [3, 4], At(2_000), ref collector, Capture);

        Assert.Equal([100u, 102u, 104u], collector.SequenceNumbers);
        Assert.Equal([1, 2], collector.Payloads[0]);
        Assert.Equal([3, 4], collector.Payloads[1]);
        Assert.Equal([5, 6], collector.Payloads[2]);
        Assert.Equal([1_000L, 2_000L, 3_000L], collector.Timestamps);
    }

    [Fact]
    public void Trims_Overlapping_Payload_Before_Emission()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2, 3, 4], At(1_000), ref collector, Capture);
        reassembler.Feed(102, [3, 4, 5, 6], At(2_000), ref collector, Capture);

        Assert.Equal([100u, 104u], collector.SequenceNumbers);
        Assert.Equal([1, 2, 3, 4], collector.Payloads[0]);
        Assert.Equal([5, 6], collector.Payloads[1]);
    }

    [Fact]
    public void Keeps_Many_Small_OutOfOrder_Segments_Within_Byte_Budget()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [0], At(1_000), ref collector, Capture);
        for (var i = 1; i <= 503; i++)
        {
            reassembler.Feed((uint)(101 + i), [(byte)i], At(1_000 + i), ref collector, Capture);
        }

        reassembler.Feed(101, [1], At(1_001), ref collector, Capture);

        Assert.Equal(505, collector.SequenceNumbers.Count);
        Assert.Equal(604u, collector.SequenceNumbers[^1]);
        Assert.Equal([247], collector.Payloads[^1]);
    }

    [Fact]
    public void Drains_OutOfOrder_Segments_Across_Sequence_Wrap()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();
        const uint start = uint.MaxValue - 3;

        reassembler.StartAt(start);
        reassembler.Feed(0, [5, 6, 7], At(3_000), ref collector, Capture);
        reassembler.Feed(uint.MaxValue - 1, [3, 4], At(2_000), ref collector, Capture);
        reassembler.Feed(start, [1, 2], At(1_000), ref collector, Capture);

        Assert.Equal([start, uint.MaxValue - 1, 0u], collector.SequenceNumbers);
        Assert.Equal([1, 2], collector.Payloads[0]);
        Assert.Equal([3, 4], collector.Payloads[1]);
        Assert.Equal([5, 6, 7], collector.Payloads[2]);
    }

    [Fact]
    public void BufferedSegmentUsesTimeWhenItBecomesDeliverable()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.StartAt(100);
        reassembler.Feed(102, [3, 4], At(1_000), ref collector, Capture);
        reassembler.Feed(100, [1, 2], At(3_000), ref collector, Capture);

        Assert.Equal([3_000L, 3_000L], collector.Timestamps);
        Assert.Equal([30_000L, 30_000L], collector.MonotonicTimestamps);
    }

    [Fact]
    public void AcknowledgmentBelowResumeDoesNotSkipGap()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.StartAt(100);
        reassembler.Feed(104, [5, 6], At(1_000), ref collector, Capture);

        Assert.False(reassembler.TryGetAcknowledgedGap(103, out _));
        Assert.Empty(collector.Payloads);
    }

    [Fact]
    public void AcknowledgmentAtResumeSkipsGapAndDrainsPendingData()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.StartAt(100);
        reassembler.Feed(104, [5, 6], At(1_000), ref collector, Capture);

        Assert.True(reassembler.TryGetAcknowledgedGap(104, out var gap));
        Assert.Equal(new TcpReassemblyGap(100, 104, 104, 4, 1, 2), gap);
        reassembler.SkipGapAndDrain(in gap, ref collector, Capture);

        Assert.Equal([104u], collector.SequenceNumbers);
        Assert.Equal([5, 6], Assert.Single(collector.Payloads));
    }

    [Fact]
    public void RetransmissionBeforeAcknowledgmentPreservesEveryByte()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.StartAt(100);
        reassembler.Feed(104, [5, 6], At(2_000), ref collector, Capture);
        reassembler.Feed(100, [1, 2, 3, 4], At(3_000), ref collector, Capture);

        Assert.False(reassembler.TryGetAcknowledgedGap(106, out _));
        Assert.Equal([100u, 104u], collector.SequenceNumbers);
        Assert.Equal([1, 2, 3, 4], collector.Payloads[0]);
        Assert.Equal([5, 6], collector.Payloads[1]);
    }

    [Fact]
    public void AcknowledgedGapRecoversAcrossSequenceWrap()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.StartAt(uint.MaxValue - 1);
        reassembler.Feed(1, [4, 5], At(1_000), ref collector, Capture);

        Assert.True(reassembler.TryGetAcknowledgedGap(1, out var gap));
        Assert.Equal(3u, gap.ByteCount);
        reassembler.SkipGapAndDrain(in gap, ref collector, Capture);

        Assert.Equal([1u], collector.SequenceNumbers);
        Assert.Equal([4, 5], Assert.Single(collector.Payloads));
    }

    private static CapturedPacketTimestamp At(long unixMilliseconds)
        => new(unixMilliseconds, unixMilliseconds * 10);

    private static void Capture(uint sequenceNumber, ReadOnlySpan<byte> chunk, CapturedPacketTimestamp timestamp, ref ChunkCollector collector)
    {
        collector.SequenceNumbers.Add(sequenceNumber);
        collector.Payloads.Add(chunk.ToArray());
        collector.Timestamps.Add(timestamp.UnixMilliseconds);
        collector.MonotonicTimestamps.Add(timestamp.MonotonicTimestamp);
    }

    private sealed class ChunkCollector
    {
        public List<uint> SequenceNumbers { get; } = [];
        public List<byte[]> Payloads { get; } = [];
        public List<long> Timestamps { get; } = [];
        public List<long> MonotonicTimestamps { get; } = [];
    }
}
