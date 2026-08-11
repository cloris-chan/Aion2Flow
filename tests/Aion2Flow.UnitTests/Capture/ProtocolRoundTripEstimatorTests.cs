using System.Diagnostics;
using Cloris.Aion2Flow.Capture;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ProtocolRoundTripEstimatorTests
{
    private const long SessionGeneration = 1;

    [Fact]
    public void ObserveEcho_Uses_Echoed_Client_Timestamp_Directly()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();

        var resolved = estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_078,
            observedTimestamp,
            observedTimestamp,
            out var roundTripMilliseconds);

        Assert.True(resolved);
        Assert.Equal(78, roundTripMilliseconds);
        Assert.Equal(78, estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp));
    }

    [Fact]
    public void ObserveEcho_Replaces_Previous_Sample_Without_Smoothing()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(SessionGeneration, 1_000, 1_120, observedTimestamp, observedTimestamp, out _);

        estimator.TryObserveEcho(
            SessionGeneration,
            11_000,
            11_055,
            observedTimestamp + 1,
            observedTimestamp + 1,
            out var roundTripMilliseconds);

        Assert.Equal(55, roundTripMilliseconds);
        Assert.Equal(55, estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp + 1));
    }

    [Theory]
    [InlineData(1_001, 1_000)]
    [InlineData(1_000, 11_001)]
    [InlineData(-1, 1_000)]
    public void ObserveEcho_Rejects_Implausible_Time_Ranges(long clientSentUnixMilliseconds, long arrivalUnixMilliseconds)
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();

        var resolved = estimator.TryObserveEcho(
            SessionGeneration,
            clientSentUnixMilliseconds,
            arrivalUnixMilliseconds,
            observedTimestamp,
            observedTimestamp,
            out _);

        Assert.False(resolved);
        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp));
    }

    [Fact]
    public void CurrentSample_Expires_After_Thirty_Seconds()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(SessionGeneration, 1_000, 1_080, observedTimestamp, observedTimestamp, out _);

        Assert.Equal(80, estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp + 30 * Stopwatch.Frequency));
        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp + 30 * Stopwatch.Frequency + 1));
    }

    [Fact]
    public void Clear_Removes_Current_Sample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(SessionGeneration, 1_000, 1_080, observedTimestamp, observedTimestamp, out _);

        estimator.Clear();

        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration, observedTimestamp));
    }

    [Fact]
    public void CurrentSampleIsNotReusedForAnotherSessionGeneration()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(SessionGeneration, 1_000, 1_080, observedTimestamp, observedTimestamp, out _);

        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration + 1, observedTimestamp));
    }

    [Fact]
    public void DelayedSampleOlderThanThirtySecondsDoesNotReplaceCurrent()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var nowTimestamp = 100 * Stopwatch.Frequency;
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            nowTimestamp,
            nowTimestamp,
            out _));

        var delayedArrivalTimestamp = nowTimestamp - 31 * Stopwatch.Frequency;
        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            2_000,
            2_120,
            delayedArrivalTimestamp,
            nowTimestamp,
            out _));
        Assert.Equal(80, estimator.GetCurrentMilliseconds(SessionGeneration, nowTimestamp));
    }

    [Fact]
    public void OlderArrivalDoesNotReplaceNewerSample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var nowTimestamp = 100 * Stopwatch.Frequency;
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            nowTimestamp,
            nowTimestamp,
            out _));

        var olderArrivalTimestamp = nowTimestamp - Stopwatch.Frequency;
        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            2_000,
            2_120,
            olderArrivalTimestamp,
            nowTimestamp,
            out _));
        Assert.Equal(80, estimator.GetCurrentMilliseconds(SessionGeneration, nowTimestamp));
    }

    [Fact]
    public void EchoesFromSameCapturedChunkUseParseOrder()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = 100 * Stopwatch.Frequency;
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            arrivalTimestamp,
            arrivalTimestamp,
            out _));

        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            2_000,
            2_055,
            arrivalTimestamp,
            arrivalTimestamp,
            out var roundTripMilliseconds));
        Assert.Equal(55, roundTripMilliseconds);
        Assert.Equal(55, estimator.GetCurrentMilliseconds(SessionGeneration, arrivalTimestamp));
    }

    [Fact]
    public void SampleAtStaleBoundaryIsAccepted()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = 100 * Stopwatch.Frequency;
        var nowTimestamp = arrivalTimestamp + 30 * Stopwatch.Frequency;

        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            arrivalTimestamp,
            nowTimestamp,
            out _));
    }

    [Fact]
    public void SampleBeyondStaleBoundaryIsRejected()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = 100 * Stopwatch.Frequency;
        var nowTimestamp = arrivalTimestamp + 30 * Stopwatch.Frequency + 1;

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            arrivalTimestamp,
            nowTimestamp,
            out _));
    }

    [Fact]
    public void FutureArrivalTimestampIsRejected()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var nowTimestamp = 100 * Stopwatch.Frequency;

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            1_000,
            1_080,
            nowTimestamp + 1,
            nowTimestamp,
            out _));
    }
}
