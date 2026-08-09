using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class LatestTcpAcknowledgmentTrackerTests
{
    [Fact]
    public void CoalescesAcknowledgmentsUntilTheDispatcherCompletesTheNotification()
    {
        var tracker = new LatestTcpAcknowledgmentTracker();
        var connection = new TcpConnection(0x0100000a, 0x0200000a, 21060, 49628);

        Assert.True(tracker.Observe(in connection, 3, 7, 100));
        Assert.False(tracker.Observe(in connection, 3, 7, 101));
        Assert.True(tracker.TryGetLatest(out var latest));
        Assert.Equal(101u, latest.AcknowledgmentNumber);
        Assert.False(tracker.CompleteNotification(latest.Version - 1));
        Assert.True(tracker.CompleteNotification(latest.Version));
        Assert.True(tracker.Observe(in connection, 3, 7, 102));
    }

    [Fact]
    public void IgnoresOlderAcknowledgmentsAndStaleConnectionState()
    {
        var tracker = new LatestTcpAcknowledgmentTracker();
        var connection = new TcpConnection(0x0100000a, 0x0200000a, 21060, 49628);
        var replacement = connection with { DestinationPort = 49629 };

        Assert.True(tracker.Observe(in connection, 3, 7, 100));
        Assert.False(tracker.Observe(in connection, 3, 7, 99));
        Assert.False(tracker.TryGet(in replacement, 3, 7, out _));
        Assert.False(tracker.TryGet(in connection, 2, 7, out _));
        Assert.True(tracker.TryGet(in connection, 3, 7, out var acknowledgment));
        Assert.Equal(100u, acknowledgment);
    }

    [Fact]
    public void HandlesAcknowledgmentSequenceWrap()
    {
        var tracker = new LatestTcpAcknowledgmentTracker();
        var connection = new TcpConnection(0x0100000a, 0x0200000a, 21060, 49628);

        Assert.True(tracker.Observe(in connection, 3, 7, uint.MaxValue - 1));
        Assert.False(tracker.Observe(in connection, 3, 7, uint.MaxValue - 2));
        Assert.False(tracker.Observe(in connection, 3, 7, 0));
        Assert.True(tracker.TryGet(in connection, 3, 7, out var acknowledgment));
        Assert.Equal(0u, acknowledgment);
    }
}
