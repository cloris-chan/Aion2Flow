using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ConnectionAdmissionTests
{
    [Fact]
    public void GateAcceptsOnlyTheLockedDownstreamConnection()
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
    public void GateDoesNotReplaceALockWithAPreviouslyAdmittedConnection()
    {
        var firstConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var queuedConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_060, 49_629);

        try
        {
            var firstAdmission = CaptureConnectionGate.EvaluatePacket(in firstConnection, hasCloseFlag: false);
            var queuedAdmission = CaptureConnectionGate.EvaluatePacket(in queuedConnection, hasCloseFlag: false);

            Assert.True(CaptureConnectionGate.TryLock(in firstConnection, firstAdmission.Generation, out var acquired));
            Assert.True(acquired);
            Assert.False(CaptureConnectionGate.TryLock(in queuedConnection, queuedAdmission.Generation, out acquired));
            Assert.False(acquired);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(firstConnection, lockedConnection);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void GateRejectsAnAdmissionAfterItsGenerationIsReleased()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);

        try
        {
            var staleAdmission = CaptureConnectionGate.EvaluatePacket(in connection, hasCloseFlag: false);
            CaptureConnectionGate.Unlock();

            Assert.False(CaptureConnectionGate.IsAdmissionCurrent(in connection, in staleAdmission));
            Assert.False(CaptureConnectionGate.TryLock(in connection, staleAdmission.Generation, out var acquired));
            Assert.False(acquired);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void ClassifierRejectsCompleteAndSplitTlsRecords()
    {
        byte[] tlsRecord = [0x17, 0x03, 0x03, 0x00, 0x01, 0x00];

        var completeClassifier = new TcpConnectionStartClassifier();
        var complete = completeClassifier.Classify(tlsRecord);

        var splitClassifier = new TcpConnectionStartClassifier();
        var prefix = splitClassifier.Classify(tlsRecord.AsSpan(0, 2));
        var suffix = splitClassifier.Classify(tlsRecord.AsSpan(2));

        Assert.Equal(TcpConnectionStartKind.NonGame, complete.Kind);
        Assert.Equal(TcpConnectionStartKind.Pending, prefix.Kind);
        Assert.Equal(TcpConnectionStartKind.NonGame, suffix.Kind);
    }
}
