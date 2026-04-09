using Cloris.Aion2Flow.PacketCapture;
using Cloris.Aion2Flow.PacketCapture.Capture;
using System.Diagnostics;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class ProtocolRoundTripEstimatorTests
{
    [Fact]
    public void Resolves_Candidate_Frame_Length_To_Inbound_Event_Sample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 40);

        estimator.TrackOutboundFrame(frameLength: 11, startedAt);

        var resolved = estimator.TryResolveInboundEvent("state-1d37", resolvedAt, out var smoothedMilliseconds);

        Assert.True(resolved);
        Assert.True(smoothedMilliseconds >= 20d);
        Assert.True(estimator.CurrentMilliseconds >= 20d);
    }

    [Fact]
    public void Ignores_NonCandidate_Frame_Lengths_And_Events()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var checkedAt = startedAt + (Stopwatch.Frequency / 40);

        estimator.TrackOutboundFrame(frameLength: 29, startedAt);

        var resolved = estimator.TryResolveInboundEvent("compressed-container", checkedAt, out _);

        Assert.False(resolved);
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Fact]
    public void Resolves_Idle_Scene_Aux_Events()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 20);

        estimator.TrackOutboundFrame(frameLength: 41, startedAt);

        var resolved = estimator.TryResolveInboundEvent("aux-2c38", resolvedAt, out var smoothedMilliseconds);

        Assert.True(resolved);
        Assert.True(smoothedMilliseconds >= 40d);
    }

    [Fact]
    public void Clears_Current_Value()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 40);

        estimator.TrackOutboundFrame(frameLength: 31, startedAt);
        estimator.TryResolveInboundEvent("remain-hp", resolvedAt, out _);

        estimator.Clear();

        Assert.Null(estimator.CurrentMilliseconds);
    }
}
