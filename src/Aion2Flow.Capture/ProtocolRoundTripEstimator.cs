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

    public bool TryObserveEcho(in TcpConnection connection, long clientSentUnixMilliseconds, long arrivalUnixMilliseconds, out double roundTripMilliseconds)
        => TryObserveEcho(in connection, clientSentUnixMilliseconds, arrivalUnixMilliseconds, Stopwatch.GetTimestamp(), out roundTripMilliseconds);

    internal bool TryObserveEcho(in TcpConnection connection, long clientSentUnixMilliseconds, long arrivalUnixMilliseconds, long observedTimestamp, out double roundTripMilliseconds)
    {
        if (!Packet0336RoundTripParser.IsPlausibleClientEcho(clientSentUnixMilliseconds, arrivalUnixMilliseconds))
        {
            roundTripMilliseconds = 0;
            return false;
        }

        var elapsedMilliseconds = arrivalUnixMilliseconds - clientSentUnixMilliseconds;
        roundTripMilliseconds = elapsedMilliseconds;
        Volatile.Write(ref _current, new RoundTripSample(connection, roundTripMilliseconds, observedTimestamp));
        return true;
    }

    internal double? GetCurrentMilliseconds(in TcpConnection connection, long nowTimestamp)
    {
        var current = Volatile.Read(ref _current);
        if (current is null || current.Connection != connection || nowTimestamp < current.ObservedTimestamp)
        {
            return null;
        }

        return Stopwatch.GetElapsedTime(current.ObservedTimestamp, nowTimestamp).TotalMilliseconds <= SampleStaleAfterMilliseconds
            ? current.RoundTripMilliseconds
            : null;
    }

    private sealed record RoundTripSample(TcpConnection Connection, double RoundTripMilliseconds, long ObservedTimestamp);
}
