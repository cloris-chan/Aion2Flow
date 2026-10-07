using System.Diagnostics;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class WinDivertCaptureServiceRoundTripTests
{
    [Fact]
    public async Task SupplementalTransportUpdatesRoundTripWhilePrimaryRemainsLocked()
    {
        var primary = new TcpConnection(0x0100000A, 0x0200000A, 7_135, 1_541);
        var supplemental = new TcpConnection(0x0300000A, 0x0400000A, 5_464, 1_542);
        await using var ports = new ProcessPortDiscoveryService();
        await using var capture = new WinDivertCaptureService(ports);

        CaptureConnectionGate.Unlock();
        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(
                in primary,
                out var primaryAdmission,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 129));
            Assert.True(CaptureConnectionGate.TryPromoteSupplemental(
                in supplemental,
                connectionOrdinal: 131,
                out _));
            var arrivalTimestamp = Stopwatch.GetTimestamp();
            var observation = new ProtocolRoundTripObservation(
                supplemental,
                ClientSentMonotonicMilliseconds: GetClientTimestampBefore(arrivalTimestamp, 78),
                ArrivalTimestamp: arrivalTimestamp);

            Assert.True(capture.TryObserveProtocolRoundTrip(
                in observation,
                nowTimestamp: arrivalTimestamp,
                out var roundTripMilliseconds));
            Assert.InRange(roundTripMilliseconds, 78, 79);
            Assert.NotNull(capture.CurrentRoundTripTimeMilliseconds);
            Assert.InRange(capture.CurrentRoundTripTimeMilliseconds.Value, 78, 79);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(primary, lockedConnection);

            Assert.True(CaptureConnectionGate.TryClose(
                in primary,
                primaryAdmission.Generation,
                primaryAdmission.ConnectionOrdinal,
                out _));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out lockedConnection));
            Assert.Equal(supplemental, lockedConnection);
            Assert.NotNull(capture.CurrentRoundTripTimeMilliseconds);
            Assert.InRange(capture.CurrentRoundTripTimeMilliseconds.Value, 78, 79);

            var replacement = new TcpConnection(0x0700000A, 0x0800000A, 7_135, 1_543);
            Assert.True(CaptureConnectionGate.TryPromote(
                in replacement,
                out _,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 200));
            Assert.Null(capture.CurrentRoundTripTimeMilliseconds);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task TransportOutsideActiveSessionCannotUpdateRoundTrip()
    {
        var primary = new TcpConnection(0x0100000A, 0x0200000A, 7_135, 1_541);
        var unknown = new TcpConnection(0x0500000A, 0x0600000A, 5_464, 1_542);
        await using var ports = new ProcessPortDiscoveryService();
        await using var capture = new WinDivertCaptureService(ports);

        CaptureConnectionGate.Unlock();
        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(
                in primary,
                out _,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 129));
            var arrivalTimestamp = Stopwatch.GetTimestamp();
            var observation = new ProtocolRoundTripObservation(
                unknown,
                ClientSentMonotonicMilliseconds: GetClientTimestampBefore(arrivalTimestamp, 78),
                ArrivalTimestamp: arrivalTimestamp);

            Assert.False(capture.TryObserveProtocolRoundTrip(
                in observation,
                nowTimestamp: arrivalTimestamp,
                out _));
            Assert.Null(capture.CurrentRoundTripTimeMilliseconds);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    private static uint GetClientTimestampBefore(long arrivalTimestamp, int roundTripMilliseconds)
    {
        var arrivalMilliseconds = (long)Math.Floor(arrivalTimestamp * 1_000d / Stopwatch.Frequency);
        return (uint)((arrivalMilliseconds - roundTripMilliseconds) & 0x00ff_ffff);
    }
}
