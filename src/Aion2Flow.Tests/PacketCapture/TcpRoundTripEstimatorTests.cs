using Cloris.Aion2Flow.PacketCapture;
using Cloris.Aion2Flow.PacketCapture.Capture;
using System.Diagnostics;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class TcpRoundTripEstimatorTests
{
    [Fact]
    public void Resolves_Exact_Acknowledgment_To_Rtt_Sample()
    {
        var estimator = new TcpRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 100);

        estimator.TrackOutbound(sequenceNumber: 1000, payloadLength: 11, startedAt);

        var resolved = estimator.TryResolveInbound(acknowledgmentNumber: 1011, resolvedAt, out var smoothedMilliseconds);

        Assert.True(resolved);
        Assert.True(smoothedMilliseconds >= 9d);
        Assert.True(estimator.CurrentMilliseconds >= 9d);
    }

    [Fact]
    public void Ignores_NonMatching_Acknowledgment()
    {
        var estimator = new TcpRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var checkedAt = startedAt + (Stopwatch.Frequency / 100);

        estimator.TrackOutbound(sequenceNumber: 2000, payloadLength: 11, startedAt);

        var resolved = estimator.TryResolveInbound(acknowledgmentNumber: 2005, checkedAt, out _);

        Assert.False(resolved);
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Clears_Current_Value()
    {
        var estimator = new TcpRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 100);

        estimator.TrackOutbound(sequenceNumber: 3000, payloadLength: 11, startedAt);
        estimator.TryResolveInbound(acknowledgmentNumber: 3011, resolvedAt, out _);

        estimator.Clear();

        Assert.Null(estimator.CurrentMilliseconds);
    }
}
