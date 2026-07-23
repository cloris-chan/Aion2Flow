using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture;

internal sealed class ProtocolRoundTripEstimator
{
    internal const long SampleStaleAfterMilliseconds = 30_000;

    private RoundTripSample? _current;

    public double? GetCurrentMilliseconds(in TcpConnection connection)
        => GetCurrentMilliseconds(in connection, Stopwatch.GetTimestamp());

    public void Clear() => Volatile.Write(ref _current, null);

    public bool TryObserveEcho(
        in TcpConnection connection,
        long clientSentUnixMilliseconds,
        long arrivalUnixMilliseconds,
        long arrivalTimestamp,
        out double roundTripMilliseconds)
    {
        return TryObserveEcho(
            in connection,
            clientSentUnixMilliseconds,
            arrivalUnixMilliseconds,
            arrivalTimestamp,
            Stopwatch.GetTimestamp(),
            out roundTripMilliseconds);
    }

    internal bool TryObserveEcho(
        in TcpConnection connection,
        long clientSentUnixMilliseconds,
        long arrivalUnixMilliseconds,
        long arrivalTimestamp,
        long nowTimestamp,
        out double roundTripMilliseconds)
    {
        if (!IsFreshArrival(arrivalTimestamp, nowTimestamp) ||
            !Packet0336RoundTripParser.IsPlausibleClientEcho(clientSentUnixMilliseconds, arrivalUnixMilliseconds))
        {
            roundTripMilliseconds = 0;
            return false;
        }

        var elapsedMilliseconds = arrivalUnixMilliseconds - clientSentUnixMilliseconds;
        roundTripMilliseconds = elapsedMilliseconds;
        var next = new RoundTripSample(connection, roundTripMilliseconds, arrivalTimestamp);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (current is not null &&
                current.Connection == connection &&
                current.ArrivalTimestamp > arrivalTimestamp)
            {
                roundTripMilliseconds = 0;
                return false;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, current), current))
            {
                return true;
            }
        }
    }

    internal double? GetCurrentMilliseconds(in TcpConnection connection, long nowTimestamp)
    {
        var current = Volatile.Read(ref _current);
        if (current is null || current.Connection != connection || nowTimestamp < current.ArrivalTimestamp)
        {
            return null;
        }

        return Stopwatch.GetElapsedTime(current.ArrivalTimestamp, nowTimestamp).TotalMilliseconds <= SampleStaleAfterMilliseconds
            ? current.RoundTripMilliseconds
            : null;
    }

    private static bool IsFreshArrival(long arrivalTimestamp, long nowTimestamp)
    {
        return arrivalTimestamp > 0 &&
               nowTimestamp >= arrivalTimestamp &&
               Stopwatch.GetElapsedTime(arrivalTimestamp, nowTimestamp).TotalMilliseconds <= SampleStaleAfterMilliseconds;
    }

    private sealed record RoundTripSample(TcpConnection Connection, double RoundTripMilliseconds, long ArrivalTimestamp);
}
