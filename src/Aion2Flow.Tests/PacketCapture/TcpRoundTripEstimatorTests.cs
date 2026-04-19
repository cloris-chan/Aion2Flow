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
    public void Skips_Stale_Pending_On_Cumulative_Ack()
    {
        var estimator = new TcpRoundTripEstimator();
        var t0 = Stopwatch.GetTimestamp();
        var t1 = t0 + (Stopwatch.Frequency / 200);
        var t2 = t0 + (Stopwatch.Frequency / 100);

        estimator.TrackOutbound(sequenceNumber: 100, payloadLength: 10, t0);
        estimator.TrackOutbound(sequenceNumber: 110, payloadLength: 20, t1);

        var resolved = estimator.TryResolveInbound(acknowledgmentNumber: 130, t2, out var rtt);

        Assert.True(resolved);
        Assert.True(rtt >= 4d);
    }

    [Fact]
    public void Ignores_Zero_Payload_Outbound()
    {
        var estimator = new TcpRoundTripEstimator();
        var t0 = Stopwatch.GetTimestamp();

        estimator.TrackOutbound(sequenceNumber: 500, payloadLength: 0, t0);

        var resolved = estimator.TryResolveInbound(acknowledgmentNumber: 500, t0 + Stopwatch.Frequency / 100, out _);

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

    [Fact]
    public void Ewma_Smooths_Multiple_Samples()
    {
        var estimator = new TcpRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();
        var tick25ms = Stopwatch.Frequency / 40;

        for (var i = 0; i < 5; i++)
        {
            estimator.TrackOutbound(sequenceNumber: (uint)(1000 + i * 100), payloadLength: 10, t);
            estimator.TryResolveInbound(acknowledgmentNumber: (uint)(1010 + i * 100), t + tick25ms, out _);
            t += tick25ms * 2;
        }

        var rtt = estimator.CurrentMilliseconds;
        Assert.NotNull(rtt);
        Assert.InRange(rtt.Value, 20d, 30d);
    }
}
