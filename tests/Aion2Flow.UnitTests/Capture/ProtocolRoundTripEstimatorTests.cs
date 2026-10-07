using System.Diagnostics;
using Cloris.Aion2Flow.Capture;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ProtocolRoundTripEstimatorTests
{
    private const long SessionGeneration = 1;
    private const long ClientClockModulusMilliseconds = 1L << 24;
    private const uint ClientClockMask = 0x00ff_ffff;

    [Fact]
    public void ObserveEcho_UsesClientMonotonicTimestampAndCaptureQpc()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        var resolved = estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 78),
            arrivalTimestamp,
            arrivalTimestamp,
            out var roundTripMilliseconds);

        Assert.True(resolved);
        Assert.Equal(78, roundTripMilliseconds, 2);
        Assert.Equal(78, estimator.GetCurrentMilliseconds(SessionGeneration, arrivalTimestamp)!.Value, 2);
    }

    [Fact]
    public void ObserveEcho_HandlesClientClockWraparound()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = ClientClockModulusMilliseconds + 9;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        var resolved = estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(ClientClockModulusMilliseconds - 41),
            arrivalTimestamp,
            arrivalTimestamp,
            out var roundTripMilliseconds);

        Assert.True(resolved);
        Assert.Equal(50, roundTripMilliseconds, 2);
    }

    [Fact]
    public void ObserveEcho_RejectsRoundTripLongerThanTenSeconds()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 10_001),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));
        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration, arrivalTimestamp));
    }

    [Fact]
    public void ObserveEcho_RejectsTimestampOutsideTwentyFourBitCounter()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = ToTimestamp(100_000);

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientClockMask + 1,
            arrivalTimestamp,
            arrivalTimestamp,
            out _));
    }

    [Fact]
    public void ObserveEcho_RejectsClientTimestampInTheFuture()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds + 1),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));
    }

    [Fact]
    public void ObserveEcho_ReplacesPreviousSampleWithoutSmoothing()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long firstArrivalMilliseconds = 100_000;
        var firstArrivalTimestamp = ToTimestamp(firstArrivalMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(firstArrivalMilliseconds - 120),
            firstArrivalTimestamp,
            firstArrivalTimestamp,
            out _));

        const long secondArrivalMilliseconds = 101_000;
        var secondArrivalTimestamp = ToTimestamp(secondArrivalMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(secondArrivalMilliseconds - 55),
            secondArrivalTimestamp,
            secondArrivalTimestamp,
            out var roundTripMilliseconds));

        Assert.Equal(55, roundTripMilliseconds, 2);
        Assert.Equal(55, estimator.GetCurrentMilliseconds(SessionGeneration, secondArrivalTimestamp)!.Value, 2);
    }

    [Fact]
    public void CurrentSampleExpiresAfterThirtySeconds()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 80),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));

        Assert.NotNull(estimator.GetCurrentMilliseconds(
            SessionGeneration,
            arrivalTimestamp + 30 * Stopwatch.Frequency));
        Assert.Null(estimator.GetCurrentMilliseconds(
            SessionGeneration,
            arrivalTimestamp + 30 * Stopwatch.Frequency + 1));
    }

    [Fact]
    public void ClearRemovesCurrentSample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = ToTimestamp(100_000);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(99_920),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));

        estimator.Clear();

        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration, arrivalTimestamp));
    }

    [Fact]
    public void CurrentSampleIsNotReusedForAnotherSessionGeneration()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var arrivalTimestamp = ToTimestamp(100_000);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(99_920),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));

        Assert.Null(estimator.GetCurrentMilliseconds(SessionGeneration + 1, arrivalTimestamp));
    }

    [Fact]
    public void DelayedSampleOlderThanThirtySecondsDoesNotReplaceCurrent()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long nowMilliseconds = 100_000;
        var nowTimestamp = ToTimestamp(nowMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(nowMilliseconds - 80),
            nowTimestamp,
            nowTimestamp,
            out _));

        const long delayedArrivalMilliseconds = nowMilliseconds - 31_000;
        var delayedArrivalTimestamp = ToTimestamp(delayedArrivalMilliseconds);
        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(delayedArrivalMilliseconds - 120),
            delayedArrivalTimestamp,
            nowTimestamp,
            out _));
        Assert.Equal(80, estimator.GetCurrentMilliseconds(SessionGeneration, nowTimestamp)!.Value, 2);
    }

    [Fact]
    public void OlderArrivalDoesNotReplaceNewerSample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long nowMilliseconds = 100_000;
        var nowTimestamp = ToTimestamp(nowMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(nowMilliseconds - 80),
            nowTimestamp,
            nowTimestamp,
            out _));

        const long olderArrivalMilliseconds = nowMilliseconds - 1_000;
        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(olderArrivalMilliseconds - 120),
            ToTimestamp(olderArrivalMilliseconds),
            nowTimestamp,
            out _));
        Assert.Equal(80, estimator.GetCurrentMilliseconds(SessionGeneration, nowTimestamp)!.Value, 2);
    }

    [Fact]
    public void EchoesFromSameCapturedChunkUseParseOrder()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 80),
            arrivalTimestamp,
            arrivalTimestamp,
            out _));

        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 55),
            arrivalTimestamp,
            arrivalTimestamp,
            out var roundTripMilliseconds));
        Assert.Equal(55, roundTripMilliseconds, 2);
        Assert.Equal(55, estimator.GetCurrentMilliseconds(SessionGeneration, arrivalTimestamp)!.Value, 2);
    }

    [Fact]
    public void SampleAtStaleBoundaryIsAccepted()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        Assert.True(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 80),
            arrivalTimestamp,
            arrivalTimestamp + 30 * Stopwatch.Frequency,
            out _));
    }

    [Fact]
    public void SampleBeyondStaleBoundaryIsRejected()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long arrivalMilliseconds = 100_000;
        var arrivalTimestamp = ToTimestamp(arrivalMilliseconds);

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(arrivalMilliseconds - 80),
            arrivalTimestamp,
            arrivalTimestamp + 30 * Stopwatch.Frequency + 1,
            out _));
    }

    [Fact]
    public void FutureArrivalTimestampIsRejected()
    {
        var estimator = new ProtocolRoundTripEstimator();
        const long nowMilliseconds = 100_000;
        var nowTimestamp = ToTimestamp(nowMilliseconds);

        Assert.False(estimator.TryObserveEcho(
            SessionGeneration,
            ClientTimestampAt(nowMilliseconds + 1 - 80),
            ToTimestamp(nowMilliseconds + 1),
            nowTimestamp,
            out _));
    }

    private static uint ClientTimestampAt(long milliseconds)
        => (uint)(milliseconds & ClientClockMask);

    private static long ToTimestamp(long milliseconds)
        => checked((long)Math.Ceiling(milliseconds * (double)Stopwatch.Frequency / 1_000));
}
