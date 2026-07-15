using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class Packet0336RoundTripParserTests
{
    [Fact]
    public void Parses_Echoed_Client_And_Server_Timestamps()
    {
        const long clientSentUnixMilliseconds = 1_784_045_094_650;
        const long serverUnixMilliseconds = 1_784_045_095_566;
        var frame = CapturePacketTestData.BuildRoundTrip0336Frame(clientSentUnixMilliseconds, serverUnixMilliseconds);

        var parsed = Packet0336RoundTripParser.TryParse(frame, out var result);

        Assert.True(parsed);
        Assert.Equal(clientSentUnixMilliseconds, result.ClientSentUnixMilliseconds);
        Assert.Equal(serverUnixMilliseconds, result.ServerUnixMilliseconds);
    }

    [Fact]
    public void Rejects_NonZero_Reserved_Bytes()
    {
        var frame = CapturePacketTestData.BuildRoundTrip0336Frame(1_783_721_094_650, 1_783_721_094_542);
        frame[3] = 1;

        Assert.False(Packet0336RoundTripParser.TryParse(frame, out _));
    }

    [Fact]
    public void Reassembled_Frame_Reports_Completion_Arrival_Time()
    {
        const long clientSentUnixMilliseconds = 1_783_721_094_650;
        const long serverUnixMilliseconds = 1_783_721_094_542;
        const long arrivalUnixMilliseconds = 1_783_721_094_728;
        var frame = CapturePacketTestData.BuildRoundTrip0336Frame(clientSentUnixMilliseconds, serverUnixMilliseconds);
        var observations = new List<ProtocolRoundTripObservation>();
        var connection = new TcpConnection(0x0100007F, 0x0200007F, 21_060, 49_628);
        using var processor = new PacketStreamProcessor(new RecordingRuntimeObservationSink(), observations.Add);

        var parsedPrefix = processor.AppendAndProcess(frame.AsSpan(0, 9), connection, arrivalUnixMilliseconds - 1);
        var parsedSuffix = processor.AppendAndProcess(frame.AsSpan(9), connection, arrivalUnixMilliseconds);

        Assert.False(parsedPrefix);
        Assert.True(parsedSuffix);
        var observation = Assert.Single(observations);
        Assert.Equal(connection, observation.Connection);
        Assert.Equal(clientSentUnixMilliseconds, observation.ClientSentUnixMilliseconds);
        Assert.Equal(serverUnixMilliseconds, observation.ServerUnixMilliseconds);
        Assert.Equal(arrivalUnixMilliseconds, observation.ArrivalUnixMilliseconds);
    }

    [Fact]
    public void PacketStreamProcessor_Rejects_Echo_Outside_The_Arrival_Window()
    {
        var frame = CapturePacketTestData.BuildRoundTrip0336Frame(1_783_721_094_650, 1_783_721_094_542);
        var connection = new TcpConnection(0x0100007F, 0x0200007F, 21_060, 49_628);
        using var processor = new PacketStreamProcessor(new RecordingRuntimeObservationSink());

        var parsed = processor.AppendAndProcess(frame, connection, 1_000);

        Assert.False(parsed);
    }
}
