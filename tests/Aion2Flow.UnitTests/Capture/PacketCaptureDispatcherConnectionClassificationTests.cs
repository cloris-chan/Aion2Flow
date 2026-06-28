using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketCaptureDispatcherConnectionClassificationTests
{
    [Fact]
    public void DispatchCapturedPacket_DropsTlsConnection_BeforeCombatParsing()
    {
        var sink = new RecordingRuntimeObservationSink();
        var dispatcher = new PacketCaptureDispatcher(() => sink);
        var connection = new TcpConnection(0x0100007F, 0x0100007F, 49628, 50471);
        var tls = CapturePacketTestData.BuildTlsRecordWithEmbedded0438Bytes();

        var parsedTls = Dispatch(dispatcher, connection, sequenceNumber: 100, tls, 1_000);
        var parsedFollowup = Dispatch(dispatcher, connection, sequenceNumber: 100 + (uint)tls.Length, CapturePacketTestData.Build0438Frame(), 1_050);

        Assert.False(parsedTls);
        Assert.False(parsedFollowup);
        Assert.Equal(0, sink.CombatObservationCount);
    }

    [Fact]
    public void DispatchCapturedPacket_DropsSplitTlsConnection_BeforeCombatParsing()
    {
        var sink = new RecordingRuntimeObservationSink();
        var dispatcher = new PacketCaptureDispatcher(() => sink);
        var connection = new TcpConnection(0x0100007F, 0x0100007F, 49628, 50471);
        var tls = CapturePacketTestData.BuildTlsRecordWithEmbedded0438Bytes();

        var parsedPrefix = Dispatch(dispatcher, connection, sequenceNumber: 100, tls[..2], 1_000);
        var parsedRest = Dispatch(dispatcher, connection, sequenceNumber: 102, tls[2..], 1_010);
        var parsedFollowup = Dispatch(dispatcher, connection, sequenceNumber: 100 + (uint)tls.Length, CapturePacketTestData.Build0438Frame(), 1_050);

        Assert.False(parsedPrefix);
        Assert.False(parsedRest);
        Assert.False(parsedFollowup);
        Assert.Equal(0, sink.CombatObservationCount);
    }

    [Fact]
    public void DispatchCapturedPacket_LocksRawGameConnection_AfterParse()
    {
        try
        {
            var sink = new RecordingRuntimeObservationSink();
            var dispatcher = new PacketCaptureDispatcher(() => sink);
            var connection = new TcpConnection(0x0A000001, 0xC0A80001, 49628, 2106);

            var parsed = Dispatch(dispatcher, connection, sequenceNumber: 200, CapturePacketTestData.Build0438Frame(), 1_000);

            Assert.True(parsed);
            Assert.Equal(1, sink.CombatObservationCount);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.True(lockedConnection.IsSameConnection(in connection, out _));
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    private static bool Dispatch(PacketCaptureDispatcher dispatcher, in TcpConnection connection, uint sequenceNumber, byte[] payload, long timestampMilliseconds)
    {
        var packet = CapturedPacket.CreateCopy(connection, payload, sequenceNumber, timestampMilliseconds);
        try
        {
            return dispatcher.DispatchCapturedPacket(packet);
        }
        finally
        {
            packet.Return();
        }
    }
}
