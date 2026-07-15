using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketMapEventParserTests
{
    [Theory]
    [MemberData(nameof(MapEventFrames))]
    public void PacketStreamProcessor_Parses_MapEvent_Frames(byte[] frame)
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess(frame, connection, 1_000);

        Assert.True(parsed);
        Assert.Equal(1, sink.ConfirmedMapCount);
        Assert.Equal(840037u, sink.LastConfirmedMapId);
    }

    [Fact]
    public void PacketStreamProcessor_Parses_MapEvent_Without_Resource_Match()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess(CapturePacketTestData.BuildMapEvent0061Frame(4_000_000), connection, 1_000);

        Assert.True(parsed);
        Assert.Equal(1, sink.ConfirmedMapCount);
        Assert.Equal(4_000_000u, sink.LastConfirmedMapId);
    }

    [Fact]
    public void PacketStreamProcessor_Rejects_MapEvent_With_Zero_MapId()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess(CapturePacketTestData.BuildMapEvent0061Frame(0), connection, 1_000);

        Assert.False(parsed);
        Assert.Equal(0, sink.ConfirmedMapCount);
    }

    [Fact]
    public void PacketStreamProcessor_Rejects_Low_MapEvent_Code()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess(CapturePacketTestData.BuildMapEvent0061Frame(21), connection, 1_000);

        Assert.False(parsed);
        Assert.Equal(0, sink.ConfirmedMapCount);
    }

    [Fact]
    public void PacketStreamProcessor_Low_MapEvent_Code_Does_Not_Override_SceneState_Map()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var sceneStateParsed = processor.AppendAndProcess(CapturePacketTestData.BuildState0240Frame(20), connection, 1_000);
        var mapEventParsed = processor.AppendAndProcess(CapturePacketTestData.BuildMapEvent0061Frame(21), connection, 1_001);

        Assert.True(sceneStateParsed);
        Assert.False(mapEventParsed);
        Assert.Equal(1, sink.ConfirmedMapCount);
        Assert.Equal(20u, sink.LastConfirmedMapId);
    }

    [Fact]
    public void PacketStreamProcessor_Does_Not_Parse_MapEvent_Opcode_Inside_0336_Timestamps()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess([
            0x18, 0x03, 0x36, 0x00, 0x00, 0x61, 0xD4, 0x06, 0x3F, 0x22, 0x3A, 0x00, 0x00, 0x02, 0xFC, 0xD9, 0x2C, 0x9F, 0x01, 0x00, 0x00
        ], connection, 1_783_163_911_300);

        Assert.True(parsed);
        Assert.Equal(0, sink.ConfirmedMapCount);
    }

    [Fact]
    public void PacketStreamProcessor_Does_Not_Parse_0191_List_As_MapEvent()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

        var parsed = processor.AppendAndProcess([
            0x0E, 0x01, 0x91, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00
        ], connection, 1_000);

        Assert.False(parsed);
        Assert.Equal(0, sink.ConfirmedMapCount);
    }

    public static TheoryData<byte[]> MapEventFrames() => new()
    {
        CapturePacketTestData.BuildMapEvent0061Frame(840037),
        CapturePacketTestData.BuildMapEvent0161Frame(840037)
    };
}
