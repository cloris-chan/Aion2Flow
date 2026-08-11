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
                ClientSentUnixMilliseconds: 1_000,
                ServerUnixMilliseconds: 0,
                ArrivalTimestamp: arrivalTimestamp);

            Assert.True(capture.TryObserveProtocolRoundTrip(
                in observation,
                arrivalUnixMilliseconds: 1_078,
                nowTimestamp: arrivalTimestamp,
                out var roundTripMilliseconds));
            Assert.Equal(78, roundTripMilliseconds);
            Assert.Equal(78, capture.CurrentRoundTripTimeMilliseconds);
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(primary, lockedConnection);

            Assert.True(CaptureConnectionGate.TryClose(
                in primary,
                primaryAdmission.Generation,
                primaryAdmission.ConnectionOrdinal,
                out _));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out lockedConnection));
            Assert.Equal(supplemental, lockedConnection);
            Assert.Equal(78, capture.CurrentRoundTripTimeMilliseconds);

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
                ClientSentUnixMilliseconds: 1_000,
                ServerUnixMilliseconds: 0,
                ArrivalTimestamp: arrivalTimestamp);

            Assert.False(capture.TryObserveProtocolRoundTrip(
                in observation,
                arrivalUnixMilliseconds: 1_078,
                nowTimestamp: arrivalTimestamp,
                out _));
            Assert.Null(capture.CurrentRoundTripTimeMilliseconds);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }
}
