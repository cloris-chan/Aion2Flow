using System.Diagnostics;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture;

internal sealed class ProtocolRoundTripEstimator
{
    internal const long SampleStaleAfterMilliseconds = 30_000;

    private RoundTripSample? _current;

    public double? GetCurrentMilliseconds(long sessionGeneration)
        => GetCurrentMilliseconds(sessionGeneration, Stopwatch.GetTimestamp());

    public void Clear() => Volatile.Write(ref _current, null);

    public bool TryObserveEcho(
        long sessionGeneration,
        long clientSentUnixMilliseconds,
        long arrivalUnixMilliseconds,
        long arrivalTimestamp,
        out double roundTripMilliseconds)
    {
        return TryObserveEcho(
            sessionGeneration,
            clientSentUnixMilliseconds,
            arrivalUnixMilliseconds,
            arrivalTimestamp,
            Stopwatch.GetTimestamp(),
            out roundTripMilliseconds);
    }

    internal bool TryObserveEcho(
        long sessionGeneration,
        long clientSentUnixMilliseconds,
        long arrivalUnixMilliseconds,
        long arrivalTimestamp,
        long nowTimestamp,
        out double roundTripMilliseconds)
    {
        if (sessionGeneration <= 0 ||
            !IsFreshArrival(arrivalTimestamp, nowTimestamp) ||
            !Packet0336RoundTripParser.IsPlausibleClientEcho(clientSentUnixMilliseconds, arrivalUnixMilliseconds))
        {
            roundTripMilliseconds = 0;
            return false;
        }

        var elapsedMilliseconds = arrivalUnixMilliseconds - clientSentUnixMilliseconds;
        roundTripMilliseconds = elapsedMilliseconds;
        var next = new RoundTripSample(sessionGeneration, roundTripMilliseconds, arrivalTimestamp);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (current is not null &&
                current.SessionGeneration == sessionGeneration &&
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

    internal double? GetCurrentMilliseconds(long sessionGeneration, long nowTimestamp)
    {
        var current = Volatile.Read(ref _current);
        if (current is null ||
            current.SessionGeneration != sessionGeneration ||
            nowTimestamp < current.ArrivalTimestamp)
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

    private sealed record RoundTripSample(
        long SessionGeneration,
        double RoundTripMilliseconds,
        long ArrivalTimestamp);
}
