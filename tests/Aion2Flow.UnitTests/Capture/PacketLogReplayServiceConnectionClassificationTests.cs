using System.Globalization;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayServiceConnectionClassificationTests
{
    [Fact]
    public void Replay_DropsTlsConnection_BeforeCombatParsing()
    {
        var tls = CapturePacketTestData.BuildTlsRecordWithEmbedded0438Bytes();
        var tlsLine = StreamLine(
            DateTimeOffset.FromUnixTimeMilliseconds(1_000),
            new TcpConnection(0x0100007F, 0x0100007F, 49628, 50471),
            sequenceNumber: 100,
            tls);
        var followupLine = StreamLine(
            DateTimeOffset.FromUnixTimeMilliseconds(1_050),
            new TcpConnection(0x0100007F, 0x0100007F, 49628, 50471),
            sequenceNumber: 100 + (uint)tls.Length,
            CapturePacketTestData.Build0438Frame());

        var replay = PacketLogReplayService.Replay(new StringReader(tlsLine + Environment.NewLine + followupLine), "stream.log");

        Assert.Equal(0, replay.SceneJournal.Count);
        Assert.Equal(0, replay.ReplayedLines);
        Assert.True(replay.SkippedEventCounts.TryGetValue("non-game-connection", out var skipped));
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void Replay_DropsSplitTlsConnection_BeforeCombatParsing()
    {
        var tls = CapturePacketTestData.BuildTlsRecordWithEmbedded0438Bytes();
        var connection = new TcpConnection(0x0100007F, 0x0100007F, 49628, 50471);
        var prefixLine = StreamLine(
            DateTimeOffset.FromUnixTimeMilliseconds(1_000),
            connection,
            sequenceNumber: 100,
            tls[..2]);
        var restLine = StreamLine(
            DateTimeOffset.FromUnixTimeMilliseconds(1_010),
            connection,
            sequenceNumber: 102,
            tls[2..]);
        var followupLine = StreamLine(
            DateTimeOffset.FromUnixTimeMilliseconds(1_050),
            connection,
            sequenceNumber: 100 + (uint)tls.Length,
            CapturePacketTestData.Build0438Frame());

        var replay = PacketLogReplayService.Replay(
            new StringReader(prefixLine + Environment.NewLine + restLine + Environment.NewLine + followupLine),
            "stream.log");

        Assert.Equal(0, replay.SceneJournal.Count);
        Assert.Equal(0, replay.ReplayedLines);
        Assert.True(replay.SkippedEventCounts.TryGetValue("connection-start-pending", out var pending));
        Assert.Equal(1, pending);
        Assert.True(replay.SkippedEventCounts.TryGetValue("non-game-connection", out var skipped));
        Assert.Equal(2, skipped);
    }

    private static string StreamLine(DateTimeOffset timestamp, in TcpConnection connection, uint sequenceNumber, byte[] payload)
        => string.Create(CultureInfo.InvariantCulture, $"{timestamp:O}|dir=inbound|{connection.SourceAddress}:{connection.SourcePort}->{connection.DestinationAddress}:{connection.DestinationPort}|seq={sequenceNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}");
}
