using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketRemainHpParserTests
{
    [Fact]
    public void PacketStreamProcessor_Parses_RemainHp_UInt32_As_Long()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess(CapturePacketTestData.BuildRemainHp008DFrame(56688, 3_500_000_000u), connection, 1_000);

        Assert.True(parsed);
        Assert.Equal(1, sink.NpcHpObservationCount);
        Assert.Equal(56688, sink.LastNpcHpInstanceId);
        Assert.Equal(3_500_000_000L, sink.LastNpcHp);
    }

    [Fact]
    public void PacketStreamProcessor_Consumes_Buffered_Frame_When_Dispatch_Throws()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);
        var remainHpFrame = CapturePacketTestData.BuildRemainHp008DFrame(56688, 100);

        Assert.False(processor.AppendAndProcess(remainHpFrame.AsSpan(0, remainHpFrame.Length - 1), connection, 1_000));

        sink.ThrowOnNpcHp = true;
        Assert.Throws<InvalidOperationException>(() => processor.AppendAndProcess(remainHpFrame.AsSpan(remainHpFrame.Length - 1), connection, 1_001));

        var parsed = processor.AppendAndProcess(CapturePacketTestData.BuildMapEvent0061Frame(840037), connection, 1_002);

        Assert.True(parsed);
        Assert.Equal(1, sink.ConfirmedMapCount);
        Assert.Equal(840037u, sink.LastConfirmedMapId);
    }
}
