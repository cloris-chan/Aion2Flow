using System.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Capture;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class ProtocolRoundTripEstimatorTests
{
    [Fact]
    public void Resolves_Candidate_Frame_Length_To_Inbound_Event_Sample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 40);

        estimator.TrackOutboundFrame(frameLength: 31, startedAt);

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
    public void Resolves_Aux_Events()
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
    public void Ignores_NonCandidate_Frame_Length()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();

        estimator.TrackOutboundFrame(frameLength: 99, startedAt);

        var resolved = estimator.TryResolveInboundEvent("remain-hp", startedAt + Stopwatch.Frequency / 40, out _);

        Assert.False(resolved);
        Assert.Null(estimator.CurrentMilliseconds);
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

    [Fact]
    public void WinDivertCaptureService_Tracks_Local_Outbound_Payload_Length_For_Protocol_Rtt()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();
        var resolvedAt = startedAt + (Stopwatch.Frequency / 40);

        WinDivertCaptureService.TrackLocalProtocolOutboundPayload(estimator, payloadLength: 31, startedAt);

        var resolved = estimator.TryResolveInboundEvent("compact-0638", resolvedAt, out var smoothedMilliseconds);

        Assert.True(resolved);
        Assert.True(smoothedMilliseconds >= 20d);
    }

    [Fact]
    public void WinDivertCaptureService_Ignores_Zero_Length_Local_Outbound_Payload()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var startedAt = Stopwatch.GetTimestamp();

        WinDivertCaptureService.TrackLocalProtocolOutboundPayload(estimator, payloadLength: 0, startedAt);

        var resolved = estimator.TryResolveInboundEvent("remain-hp", startedAt + Stopwatch.Frequency / 40, out _);

        Assert.False(resolved);
        Assert.Null(estimator.CurrentMilliseconds);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(42)]
    public void Accepts_All_Candidate_Frame_Lengths(int frameLength)
    {
        Assert.True(ProtocolRoundTripEstimator.IsCandidateOutboundFrameLength(frameLength));
    }

    [Fact]
    public void Rejects_Heartbeat_Length_Outbound_Frame()
    {
        Assert.False(ProtocolRoundTripEstimator.IsCandidateOutboundFrameLength(11));
    }

    [Fact]
    public void Resolves_Newest_Pending_When_Multiple_Outbound_Frames_Are_Queued()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var t0 = Stopwatch.GetTimestamp();
        var olderSampleAt = t0;
        var newerSampleAt = t0 + (Stopwatch.Frequency / 50);
        var inboundAt = newerSampleAt + (Stopwatch.Frequency / 100);

        estimator.TrackOutboundFrame(frameLength: 31, olderSampleAt);
        estimator.TrackOutboundFrame(frameLength: 31, newerSampleAt);

        var resolved = estimator.TryResolveInboundEvent("state-1d37", inboundAt, out var smoothedMilliseconds);

        Assert.True(resolved);
        Assert.True(smoothedMilliseconds < 20d, $"expected smoothed < 20 ms, got {smoothedMilliseconds}");
    }

    [Theory]
    [InlineData("state-1d37")]
    [InlineData("remain-hp")]
    [InlineData("compact-value")]
    [InlineData("compact-0238")]
    [InlineData("compact-0638")]
    [InlineData("aux-2a38")]
    [InlineData("aux-2c38")]
    public void Accepts_All_Candidate_Inbound_Events(string eventName)
    {
        Assert.True(ProtocolRoundTripEstimator.IsCandidateInboundEvent(eventName));
    }
}
