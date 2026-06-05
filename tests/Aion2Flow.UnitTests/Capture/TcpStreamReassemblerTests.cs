using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class TcpStreamReassemblerTests
{
    [Fact]
    public void Emits_InOrder_Payload_Immediately()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2, 3], ref collector, Capture);

        Assert.Equal([100u], collector.SequenceNumbers);
        Assert.Equal([1, 2, 3], collector.Payloads.Single());
    }

    [Fact]
    public void Buffers_OutOfOrder_Payload_Until_Gap_Is_Filled()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2], ref collector, Capture);
        reassembler.Feed(104, [5, 6], ref collector, Capture);
        reassembler.Feed(102, [3, 4], ref collector, Capture);

        Assert.Equal([100u, 102u, 104u], collector.SequenceNumbers);
        Assert.Equal([1, 2], collector.Payloads[0]);
        Assert.Equal([3, 4], collector.Payloads[1]);
        Assert.Equal([5, 6], collector.Payloads[2]);
    }

    [Fact]
    public void Trims_Overlapping_Payload_Before_Emission()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [1, 2, 3, 4], ref collector, Capture);
        reassembler.Feed(102, [3, 4, 5, 6], ref collector, Capture);

        Assert.Equal([100u, 104u], collector.SequenceNumbers);
        Assert.Equal([1, 2, 3, 4], collector.Payloads[0]);
        Assert.Equal([5, 6], collector.Payloads[1]);
    }

    [Fact]
    public void Keeps_Many_Small_OutOfOrder_Segments_Within_Byte_Budget()
    {
        using var reassembler = new TcpStreamReassembler();
        var collector = new ChunkCollector();

        reassembler.Feed(100, [0], ref collector, Capture);
        for (var i = 1; i <= 503; i++)
        {
            reassembler.Feed((uint)(101 + i), [(byte)i], ref collector, Capture);
        }

        reassembler.Feed(101, [1], ref collector, Capture);

        Assert.Equal(505, collector.SequenceNumbers.Count);
        Assert.Equal(604u, collector.SequenceNumbers[^1]);
        Assert.Equal([247], collector.Payloads[^1]);
    }

    private static void Capture(uint sequenceNumber, ReadOnlySpan<byte> chunk, ref ChunkCollector collector)
    {
        collector.SequenceNumbers.Add(sequenceNumber);
        collector.Payloads.Add(chunk.ToArray());
    }

    private sealed class ChunkCollector
    {
        public List<uint> SequenceNumbers { get; } = [];
        public List<byte[]> Payloads { get; } = [];
    }
}
