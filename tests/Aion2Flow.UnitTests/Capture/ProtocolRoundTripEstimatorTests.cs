using System.Diagnostics;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ProtocolRoundTripEstimatorTests
{
    private static readonly TcpConnection Connection = new(1, 2, 3, 4);

    [Fact]
    public void ObserveEcho_Uses_Echoed_Client_Timestamp_Directly()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();

        var resolved = estimator.TryObserveEcho(in Connection, 1_000, 1_078, observedTimestamp, out var roundTripMilliseconds);

        Assert.True(resolved);
        Assert.Equal(78, roundTripMilliseconds);
        Assert.Equal(78, estimator.GetCurrentMilliseconds(in Connection, observedTimestamp));
    }

    [Fact]
    public void ObserveEcho_Replaces_Previous_Sample_Without_Smoothing()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(in Connection, 1_000, 1_120, observedTimestamp, out _);

        estimator.TryObserveEcho(in Connection, 11_000, 11_055, observedTimestamp + 1, out var roundTripMilliseconds);

        Assert.Equal(55, roundTripMilliseconds);
        Assert.Equal(55, estimator.GetCurrentMilliseconds(in Connection, observedTimestamp + 1));
    }

    [Theory]
    [InlineData(1_001, 1_000)]
    [InlineData(1_000, 11_001)]
    [InlineData(-1, 1_000)]
    public void ObserveEcho_Rejects_Implausible_Time_Ranges(long clientSentUnixMilliseconds, long arrivalUnixMilliseconds)
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();

        var resolved = estimator.TryObserveEcho(in Connection, clientSentUnixMilliseconds, arrivalUnixMilliseconds, observedTimestamp, out _);

        Assert.False(resolved);
        Assert.Null(estimator.GetCurrentMilliseconds(in Connection, observedTimestamp));
    }

    [Fact]
    public void CurrentSample_Expires_After_Thirty_Seconds()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(in Connection, 1_000, 1_080, observedTimestamp, out _);

        Assert.Equal(80, estimator.GetCurrentMilliseconds(in Connection, observedTimestamp + 30 * Stopwatch.Frequency));
        Assert.Null(estimator.GetCurrentMilliseconds(in Connection, observedTimestamp + 30 * Stopwatch.Frequency + 1));
    }

    [Fact]
    public void Clear_Removes_Current_Sample()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        estimator.TryObserveEcho(in Connection, 1_000, 1_080, observedTimestamp, out _);

        estimator.Clear();

        Assert.Null(estimator.GetCurrentMilliseconds(in Connection, observedTimestamp));
    }

    [Fact]
    public void CurrentSample_Is_Not_Reused_For_Another_Connection()
    {
        var estimator = new ProtocolRoundTripEstimator();
        var observedTimestamp = Stopwatch.GetTimestamp();
        var otherConnection = new TcpConnection(5, 6, 7, 8);
        estimator.TryObserveEcho(in Connection, 1_000, 1_080, observedTimestamp, out _);

        Assert.Null(estimator.GetCurrentMilliseconds(in otherConnection, observedTimestamp));
    }
}
