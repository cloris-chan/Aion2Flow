using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketCaptureDispatcherConnectionClassificationTests
{
    [Fact]
    public void CaptureConnectionGate_Rejects_Reverse_Direction_After_Downstream_Lock()
    {
        var downstream = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var upstream = new TcpConnection(0x0200000A, 0x0100000A, 49_628, 21_060);

        try
        {
            var candidate = CaptureConnectionGate.EvaluatePacket(in downstream, hasCloseFlag: false);
            Assert.Equal(CapturePacketAdmissionKind.UnlockedCandidate, candidate.Kind);
            Assert.True(CaptureConnectionGate.TryLock(in downstream, candidate.Generation, out var acquired));
            Assert.True(acquired);

            Assert.Equal(CapturePacketAdmissionKind.LockedConnection, CaptureConnectionGate.EvaluatePacket(in downstream, hasCloseFlag: false).Kind);
            Assert.Equal(CapturePacketAdmissionKind.Rejected, CaptureConnectionGate.EvaluatePacket(in upstream, hasCloseFlag: false).Kind);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void DispatchCapturedPacket_Does_Not_Replace_Lock_With_Previously_Admitted_Connection()
    {
        try
        {
            var sink = new RecordingRuntimeObservationSink();
            var dispatcher = new PacketCaptureDispatcher(() => sink);
            var firstConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
            var queuedConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_060, 49_629);
            var firstAdmission = CaptureConnectionGate.EvaluatePacket(in firstConnection, hasCloseFlag: false);
            var queuedAdmission = CaptureConnectionGate.EvaluatePacket(in queuedConnection, hasCloseFlag: false);

            Assert.True(Dispatch(dispatcher, firstConnection, firstAdmission, sequenceNumber: 100, CapturePacketTestData.Build0438Frame(), 1_000));
            Assert.False(Dispatch(dispatcher, queuedConnection, queuedAdmission, sequenceNumber: 200, CapturePacketTestData.Build0438Frame(), 1_001));
            Assert.Equal(1, sink.CombatWireObservationCount);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(firstConnection, lockedConnection);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void DispatchCapturedPacket_Rejects_Packet_From_Released_Generation()
    {
        var sink = new RecordingRuntimeObservationSink();
        var dispatcher = new PacketCaptureDispatcher(() => sink);
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var staleAdmission = CaptureConnectionGate.EvaluatePacket(in connection, hasCloseFlag: false);
        CaptureConnectionGate.Unlock();

        var parsed = Dispatch(dispatcher, connection, staleAdmission, sequenceNumber: 100, CapturePacketTestData.Build0438Frame(), 1_000);

        Assert.False(parsed);
        Assert.Equal(0, sink.CombatWireObservationCount);
    }

    [Fact]
    public void DispatchCapturedPacket_Notifies_ConnectionLock_After_First_0336_Observation()
    {
        try
        {
            var sink = new RecordingRuntimeObservationSink();
            var protocolObserved = false;
            var lockObserved = false;
            var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
            var dispatcher = new PacketCaptureDispatcher(
                () => sink,
                observation =>
                {
                    Assert.Equal(connection, observation.Connection);
                    Assert.False(CaptureConnectionGate.IsLocked);
                    protocolObserved = true;
                },
                lockedConnection =>
                {
                    Assert.True(protocolObserved);
                    Assert.Equal(connection, lockedConnection);
                    Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var currentConnection));
                    Assert.Equal(connection, currentConnection);
                    lockObserved = true;
                });
            var frame = CapturePacketTestData.BuildRoundTrip0336Frame(1_783_721_094_650, 1_783_721_094_542);

            var parsed = Dispatch(dispatcher, connection, sequenceNumber: 100, frame, 1_783_721_094_728);

            Assert.True(parsed);
            Assert.True(protocolObserved);
            Assert.True(lockObserved);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

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
        Assert.Equal(0, sink.CombatWireObservationCount);
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
        Assert.Equal(0, sink.CombatWireObservationCount);
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
            Assert.Equal(1, sink.CombatWireObservationCount);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(connection, lockedConnection);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    private static bool Dispatch(PacketCaptureDispatcher dispatcher, in TcpConnection connection, uint sequenceNumber, byte[] payload, long timestampMilliseconds)
    {
        var admission = CaptureConnectionGate.EvaluatePacket(in connection, hasCloseFlag: false);
        Assert.True(admission.IsAccepted);
        return Dispatch(dispatcher, connection, admission, sequenceNumber, payload, timestampMilliseconds);
    }

    private static bool Dispatch(PacketCaptureDispatcher dispatcher, in TcpConnection connection, CapturePacketAdmission admission, uint sequenceNumber, byte[] payload, long timestampMilliseconds)
    {
        var packet = CapturedPacket.CreateCopy(connection, admission, payload, sequenceNumber, timestampMilliseconds);
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
