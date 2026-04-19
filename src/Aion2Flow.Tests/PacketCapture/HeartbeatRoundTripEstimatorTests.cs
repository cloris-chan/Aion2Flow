using System.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Capture;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class HeartbeatRoundTripEstimatorTests
{
    private static readonly long TicksPerMs = Stopwatch.Frequency / 1000;
    private static readonly byte[] ServerReply = [0x06, 0x00, 0x36];

    [Fact]
    public void Pairs_Heartbeat_With_Standalone_Reply()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();

        estimator.TryTrackOutbound(BuildHeartbeat(), t);
        Assert.True(estimator.TryResolveInbound(ServerReply, t + (TicksPerMs * 25), out var rtt));

        Assert.InRange(rtt, 24d, 26d);
        Assert.NotNull(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Rejects_Payload_Not_Ending_With_Reply()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();
        estimator.TryTrackOutbound(BuildHeartbeat(), t);

        byte[] bundled = [0x06, 0x00, 0x36, 0xAB, 0xCD];
        Assert.False(estimator.TryResolveInbound(bundled, t + (TicksPerMs * 1), out _));
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Accepts_Reply_Suffixed_To_Payload()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();
        estimator.TryTrackOutbound(BuildHeartbeat(), t);

        byte[] suffixed = [0x1A, 0x1C, 0x37, 0x06, 0x00, 0x36];
        Assert.True(estimator.TryResolveInbound(suffixed, t + (TicksPerMs * 30), out _));
        Assert.NotNull(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Rejects_Bare_Tcp_Ack()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        estimator.TryTrackOutbound(BuildHeartbeat(), Stopwatch.GetTimestamp());

        Assert.False(estimator.TryResolveInbound([], Stopwatch.GetTimestamp(), out _));
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Rejects_Reply_With_No_Pending()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        Assert.False(estimator.TryResolveInbound(ServerReply, Stopwatch.GetTimestamp(), out var rtt));
        Assert.Equal(0d, rtt);
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Overwrite_Tracks_Latest_Heartbeat()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t0 = Stopwatch.GetTimestamp();
        var t1 = t0 + (TicksPerMs * 50);

        Assert.True(estimator.TryTrackOutbound(BuildHeartbeat(), t0));
        Assert.True(estimator.TryTrackOutbound(BuildHeartbeat(), t1));
        Assert.True(estimator.TryResolveInbound(ServerReply, t1 + (TicksPerMs * 25), out var rtt));

        Assert.InRange(rtt, 24d, 26d);
    }

    [Fact]
    public void Accumulates_Max_Candidate_Within_Cycle()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();

        estimator.TryTrackOutbound(BuildHeartbeat(), t);

        Assert.True(estimator.TryResolveInbound(ServerReply, t + (TicksPerMs * 5), out _));

        byte[] suffixed = [0x1A, 0x1C, 0x37, 0x06, 0x00, 0x36];
        Assert.True(estimator.TryResolveInbound(suffixed, t + (TicksPerMs * 30), out _));

        estimator.TryTrackOutbound(BuildHeartbeat(), t + (TicksPerMs * 100));
        Assert.InRange(estimator.CurrentMilliseconds!.Value, 28d, 32d);
    }

    [Fact]
    public void Warmup_Climbs_To_Server_Rtt_Despite_Proxy_Seed()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var baseline = Stopwatch.GetTimestamp();

        estimator.TryTrackOutbound(BuildHeartbeat(), baseline);
        estimator.TryResolveInbound(ServerReply, baseline + (TicksPerMs * 2), out _);

        for (var i = 1; i <= 4; i++)
        {
            var sentAt = baseline + (i * TicksPerMs * 100);
            estimator.TryTrackOutbound(BuildHeartbeat(), sentAt);
            estimator.TryResolveInbound(ServerReply, sentAt + (TicksPerMs * 30), out _);
        }

        Assert.InRange(estimator.CurrentMilliseconds!.Value, 28d, 32d);
    }

    [Fact]
    public void Dampened_Ewma_Absorbs_Proxy_Outlier()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var baseline = Stopwatch.GetTimestamp();

        for (var i = 0; i < 20; i++)
        {
            var sentAt = baseline + (i * TicksPerMs * 100);
            estimator.TryTrackOutbound(BuildHeartbeat(), sentAt);
            estimator.TryResolveInbound(ServerReply, sentAt + (TicksPerMs * 30), out _);
        }

        var outlierSent = baseline + (20 * TicksPerMs * 100);
        estimator.TryTrackOutbound(BuildHeartbeat(), outlierSent);
        Assert.True(estimator.TryResolveInbound(ServerReply, outlierSent + (TicksPerMs * 2), out var smoothed));

        Assert.InRange(smoothed, 28d, 32d);
    }

    [Fact]
    public void Rejects_Outbound_Not_Heartbeat()
    {
        var estimator = new HeartbeatRoundTripEstimator();

        Span<byte> shortPayload = stackalloc byte[10];
        shortPayload[0] = 0x0E;
        Assert.False(estimator.TryTrackOutbound(shortPayload, Stopwatch.GetTimestamp()));

        var wrongLead = BuildHeartbeat();
        wrongLead[0] = 0x0F;
        Assert.False(estimator.TryTrackOutbound(wrongLead, Stopwatch.GetTimestamp()));
    }

    [Fact]
    public void Clear_Resets_State()
    {
        var estimator = new HeartbeatRoundTripEstimator();
        var t = Stopwatch.GetTimestamp();

        estimator.TryTrackOutbound(BuildHeartbeat(), t);
        estimator.TryResolveInbound(ServerReply, t + (TicksPerMs * 25), out _);
        Assert.NotNull(estimator.CurrentMilliseconds);

        estimator.Clear();

        Assert.Null(estimator.CurrentMilliseconds);
        Assert.False(estimator.TryResolveInbound(ServerReply, Stopwatch.GetTimestamp(), out _));
    }

    private static byte[] BuildHeartbeat()
    {
        var payload = new byte[11];
        payload[0] = 0x0E;
        return payload;
    }
}
