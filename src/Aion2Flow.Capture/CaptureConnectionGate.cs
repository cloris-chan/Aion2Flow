using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

public enum CapturePacketAdmissionKind : byte
{
    Rejected,
    Candidate,
    ActiveConnection
}

public readonly record struct CapturePacketAdmission(CapturePacketAdmissionKind Kind, long Generation, bool ReleasedLock)
{
    public bool IsAccepted => Kind != CapturePacketAdmissionKind.Rejected;
    public bool RequiresClassification => Kind == CapturePacketAdmissionKind.Candidate;
}

public static class CaptureConnectionGate
{
    private const int RetiredConnectionLimit = 16;
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(5);
    private static GateState _currentState = GateState.CreateUnlocked(0, []);

    public static bool IsLocked => TryGetLockedConnection(out _);

    public static CapturePacketAdmission EvaluatePacket(in TcpConnection connection, bool hasStartFlag, bool hasCloseFlag) =>
        EvaluatePacket(in connection, hasStartFlag, hasCloseFlag, Stopwatch.GetTimestamp());

    internal static CapturePacketAdmission EvaluatePacket(
        in TcpConnection connection,
        bool hasStartFlag,
        bool hasCloseFlag,
        long observedTimestamp)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (hasStartFlag && state.IsRetired(connection))
            {
                var restarted = state.WithoutRetiredConnection(connection);
                if (Interlocked.CompareExchange(ref _currentState, restarted, state) != state)
                {
                    continue;
                }

                state = restarted;
            }

            if (state.IsLocked && state.Connection == connection)
            {
                Interlocked.Exchange(ref state.LastActivityTicks, observedTimestamp);
                if (!hasCloseFlag)
                {
                    return new CapturePacketAdmission(CapturePacketAdmissionKind.ActiveConnection, state.Generation, ReleasedLock: false);
                }

                if (TryRelease(state, "FIN/RST detected, unlocked", retireConnection: true, out var generation))
                {
                    return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, generation, ReleasedLock: true);
                }

                continue;
            }

            if (state.IsLocked && hasCloseFlag && state.Connection.Reverse() == connection)
            {
                if (TryRelease(state, "FIN/RST detected, unlocked", retireConnection: true, out var generation))
                {
                    return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, generation, ReleasedLock: true);
                }

                continue;
            }

            if (state.IsLocked && IsExpired(state, observedTimestamp))
            {
                return hasCloseFlag
                    ? new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, state.Generation, ReleasedLock: false)
                    : new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, state.Generation, ReleasedLock: false);
            }

            if (hasCloseFlag || state.IsRetired(connection))
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, state.Generation, ReleasedLock: false);
            }

            return new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, state.Generation, ReleasedLock: false);
        }
    }

    public static void Unlock()
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            var unlocked = GateState.CreateUnlocked(state.Generation + 1, []);
            if (Interlocked.CompareExchange(ref _currentState, unlocked, state) == state)
            {
                return;
            }
        }
    }

    public static bool TryGetLockedConnection(out TcpConnection connection)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsLocked)
            {
                connection = default;
                return false;
            }

            if (IsExpired(state, Stopwatch.GetTimestamp()))
            {
                connection = default;
                return false;
            }

            if (ReferenceEquals(state, Volatile.Read(ref _currentState)))
            {
                connection = state.Connection;
                return true;
            }
        }
    }

    internal static bool IsAdmissionCurrent(in TcpConnection connection, in CapturePacketAdmission admission)
    {
        if (admission.Kind != CapturePacketAdmissionKind.ActiveConnection)
        {
            return false;
        }

        var state = Volatile.Read(ref _currentState);
        return state.IsLocked &&
               state.Generation == admission.Generation &&
               state.Connection == connection;
    }

    internal static void ObserveConnectionStart(in TcpConnection connection)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsRetired(connection))
            {
                return;
            }

            var restarted = state.WithoutRetiredConnection(connection);
            if (Interlocked.CompareExchange(ref _currentState, restarted, state) == state)
            {
                return;
            }
        }
    }

    internal static bool TryGetActiveAdmission(in TcpConnection connection, out CapturePacketAdmission admission)
    {
        return TryGetActiveAdmission(in connection, expectedConnectionOrdinal: 0, out admission);
    }

    internal static bool TryGetActiveAdmission(
        in TcpConnection connection,
        long expectedConnectionOrdinal,
        out CapturePacketAdmission admission)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsLocked)
            {
                admission = default;
                return false;
            }

            if (state.Connection != connection ||
                (expectedConnectionOrdinal > 0 && state.ConnectionOrdinal != expectedConnectionOrdinal) ||
                !ReferenceEquals(state, Volatile.Read(ref _currentState)))
            {
                admission = default;
                return false;
            }

            admission = new CapturePacketAdmission(
                CapturePacketAdmissionKind.ActiveConnection,
                state.Generation,
                ReleasedLock: false);
            return true;
        }
    }

    internal static bool TryGetActiveConnectionOrdinal(in TcpConnection connection, out long connectionOrdinal)
    {
        var state = Volatile.Read(ref _currentState);
        if (state.IsLocked &&
            (state.Connection == connection || state.Connection.Reverse() == connection) &&
            ReferenceEquals(state, Volatile.Read(ref _currentState)))
        {
            connectionOrdinal = state.ConnectionOrdinal;
            return true;
        }

        connectionOrdinal = 0;
        return false;
    }

    internal static bool TryPromote(
        in TcpConnection connection,
        out CapturePacketAdmission admission,
        out bool replacedConnection,
        bool forceNewGeneration = false,
        long connectionOrdinal = 0)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (state.IsRetired(connection))
            {
                if (connectionOrdinal <= 0)
                {
                    admission = new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, state.Generation, ReleasedLock: false);
                    replacedConnection = false;
                    return false;
                }

                var restarted = state.WithoutRetiredConnection(connection);
                if (Interlocked.CompareExchange(ref _currentState, restarted, state) != state)
                {
                    continue;
                }

                state = restarted;
            }

            if (state.IsLocked && state.Connection == connection && !forceNewGeneration)
            {
                Interlocked.Exchange(ref state.LastActivityTicks, Stopwatch.GetTimestamp());
                admission = new CapturePacketAdmission(CapturePacketAdmissionKind.ActiveConnection, state.Generation, ReleasedLock: false);
                replacedConnection = false;
                return true;
            }

            var nextGeneration = state.Generation + 1;
            var replacedSameConnection = state.IsLocked && state.Connection == connection;
            var retiredConnections = state.RetiredConnections;
            if (state.IsLocked && !replacedSameConnection)
            {
                retiredConnections = AddRetiredConnection(retiredConnections, state.Connection);
                retiredConnections = AddRetiredConnection(retiredConnections, state.Connection.Reverse());
            }

            // Keep the reverse direction tombstoned so an outbound packet cannot
            // become the next world candidate while this downstream stream is active.
            retiredConnections = AddRetiredConnection(retiredConnections, connection.Reverse());
            var promoted = GateState.CreateLocked(nextGeneration, connection, retiredConnections, connectionOrdinal);
            if (Interlocked.CompareExchange(ref _currentState, promoted, state) != state)
            {
                continue;
            }

            replacedConnection = state.IsLocked;
            admission = new CapturePacketAdmission(CapturePacketAdmissionKind.ActiveConnection, nextGeneration, ReleasedLock: false);
            CaptureLog.Write(
                CaptureLogLevel.Info,
                replacedConnection ? "Confirmed replacement game connection" : "Confirmed game connection");
            return true;
        }
    }

    internal static bool TryClose(
        in TcpConnection observedConnection,
        long expectedGeneration,
        long expectedConnectionOrdinal,
        out TcpConnection closedConnection)
    {
        if (expectedGeneration <= 0 && expectedConnectionOrdinal <= 0)
        {
            closedConnection = default;
            return false;
        }

        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsLocked ||
                (state.Connection != observedConnection && state.Connection.Reverse() != observedConnection) ||
                (expectedConnectionOrdinal > 0 && state.ConnectionOrdinal != expectedConnectionOrdinal) ||
                (expectedConnectionOrdinal <= 0 && expectedGeneration > 0 && state.Generation != expectedGeneration))
            {
                closedConnection = default;
                return false;
            }

            if (!TryRelease(state, "FIN/RST detected, unlocked", retireConnection: true, out _))
            {
                continue;
            }

            closedConnection = state.Connection;
            return true;
        }
    }

    private static bool IsExpired(GateState state, long now)
    {
        var lastActivity = Interlocked.Read(ref state.LastActivityTicks);
        return now >= lastActivity && Stopwatch.GetElapsedTime(lastActivity, now) > _idleTimeout;
    }

    private static bool TryRelease(GateState state, string message, bool retireConnection, out long generation)
    {
        generation = state.Generation + 1;
        var retiredConnections = state.RetiredConnections;
        if (retireConnection && state.IsLocked)
        {
            retiredConnections = AddRetiredConnection(retiredConnections, state.Connection);
            retiredConnections = AddRetiredConnection(retiredConnections, state.Connection.Reverse());
        }
        var unlocked = GateState.CreateUnlocked(generation, retiredConnections);
        if (Interlocked.CompareExchange(ref _currentState, unlocked, state) != state)
        {
            return false;
        }

        CaptureLog.Write(CaptureLogLevel.Info, message);
        return true;
    }

    private static TcpConnection[] AddRetiredConnection(TcpConnection[] retiredConnections, in TcpConnection connection)
    {
        if (Array.IndexOf(retiredConnections, connection) >= 0)
        {
            return retiredConnections;
        }

        var retainedCount = Math.Min(retiredConnections.Length, RetiredConnectionLimit - 1);
        var result = new TcpConnection[retainedCount + 1];
        if (retainedCount != 0)
        {
            retiredConnections.AsSpan(retiredConnections.Length - retainedCount).CopyTo(result);
        }

        result[^1] = connection;
        return result;
    }

    private sealed class GateState(
        long generation,
        bool isLocked,
        TcpConnection connection,
        TcpConnection[] retiredConnections,
        long connectionOrdinal,
        long lastActivityTicks)
    {
        public readonly long Generation = generation;
        public readonly bool IsLocked = isLocked;
        public readonly TcpConnection Connection = connection;
        public readonly TcpConnection[] RetiredConnections = retiredConnections;
        public readonly long ConnectionOrdinal = connectionOrdinal;
        public long LastActivityTicks = lastActivityTicks;

        public bool IsRetired(in TcpConnection connection) => Array.IndexOf(RetiredConnections, connection) >= 0;

        public GateState WithoutRetiredConnection(in TcpConnection connection)
        {
            var index = Array.IndexOf(RetiredConnections, connection);
            if (index < 0)
            {
                return this;
            }

            var retired = new TcpConnection[RetiredConnections.Length - 1];
            RetiredConnections.AsSpan(0, index).CopyTo(retired);
            RetiredConnections.AsSpan(index + 1).CopyTo(retired.AsSpan(index));
            return new GateState(
                Generation,
                IsLocked,
                Connection,
                retired,
                ConnectionOrdinal,
                Interlocked.Read(ref LastActivityTicks));
        }

        public static GateState CreateUnlocked(long generation, TcpConnection[] retiredConnections) =>
            new(generation, false, default, retiredConnections, 0, 0);

        public static GateState CreateLocked(
            long generation,
            in TcpConnection connection,
            TcpConnection[] retiredConnections,
            long connectionOrdinal) =>
            new(generation, true, connection, retiredConnections, connectionOrdinal, Stopwatch.GetTimestamp());
    }
}
