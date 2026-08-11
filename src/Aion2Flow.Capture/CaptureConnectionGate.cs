using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

public enum CapturePacketAdmissionKind : byte
{
    Rejected,
    Candidate,
    ActiveConnection
}

public enum CaptureConnectionRole : byte
{
    Primary,
    Supplemental
}

public readonly record struct CapturePacketAdmission(
    CapturePacketAdmissionKind Kind,
    long Generation,
    bool ReleasedLock,
    CaptureConnectionRole Role = CaptureConnectionRole.Primary,
    long ConnectionOrdinal = 0)
{
    public bool IsAccepted => Kind != CapturePacketAdmissionKind.Rejected;
    public bool RequiresClassification => Kind == CapturePacketAdmissionKind.Candidate;
}

internal readonly record struct CaptureConnectionEviction(
    TcpConnection Connection,
    long ConnectionOrdinal)
{
    public bool HasValue => ConnectionOrdinal > 0;
}

public static class CaptureConnectionGate
{
    private const int RetiredConnectionLimit = 16;
    private const int RetiredAttemptLimit = CaptureBufferLimits.CandidateStreamCountLimit;
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
                    return new CapturePacketAdmission(
                        CapturePacketAdmissionKind.ActiveConnection,
                        state.Generation,
                        ReleasedLock: false,
                        CaptureConnectionRole.Primary,
                        state.ConnectionOrdinal);
                }

                if (TryRelease(state, "FIN/RST detected, unlocked", retireConnection: true, out var generation))
                {
                    return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, generation, ReleasedLock: true);
                }

                continue;
            }

            if (state.TryGetSupplemental(in connection, out var supplemental))
            {
                Interlocked.Exchange(ref supplemental.LastActivityTicks, observedTimestamp);
                return new CapturePacketAdmission(
                    CapturePacketAdmissionKind.ActiveConnection,
                    state.Generation,
                    ReleasedLock: false,
                    CaptureConnectionRole.Supplemental,
                    supplemental.ConnectionOrdinal);
            }

            if (state.TryGetSupplementalAnyDirection(in connection, out _))
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, state.Generation, ReleasedLock: false);
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

    internal static bool IsPrimaryConnection(in TcpConnection connection)
    {
        var state = Volatile.Read(ref _currentState);
        return state.IsLocked &&
               (state.Connection == connection || state.Connection.Reverse() == connection) &&
               ReferenceEquals(state, Volatile.Read(ref _currentState));
    }

    internal static bool IsAdmissionCurrent(in TcpConnection connection, in CapturePacketAdmission admission)
    {
        if (admission.Kind != CapturePacketAdmissionKind.ActiveConnection)
        {
            return false;
        }

        var state = Volatile.Read(ref _currentState);
        if (!state.IsLocked || state.Generation != admission.Generation)
            return false;

        if (state.Connection == connection)
            return state.ConnectionOrdinal == admission.ConnectionOrdinal;

        return state.TryGetSupplemental(in connection, out var supplemental) &&
               supplemental.ConnectionOrdinal == admission.ConnectionOrdinal;
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

            if (state.Connection == connection &&
                (expectedConnectionOrdinal <= 0 || state.ConnectionOrdinal == expectedConnectionOrdinal) &&
                ReferenceEquals(state, Volatile.Read(ref _currentState)))
            {
                admission = new CapturePacketAdmission(
                    CapturePacketAdmissionKind.ActiveConnection,
                    state.Generation,
                    ReleasedLock: false,
                    CaptureConnectionRole.Primary,
                    state.ConnectionOrdinal);
                return true;
            }

            if (state.TryGetSupplemental(in connection, out var supplemental) &&
                (expectedConnectionOrdinal <= 0 || supplemental.ConnectionOrdinal == expectedConnectionOrdinal) &&
                ReferenceEquals(state, Volatile.Read(ref _currentState)))
            {
                admission = new CapturePacketAdmission(
                    CapturePacketAdmissionKind.ActiveConnection,
                    state.Generation,
                    ReleasedLock: false,
                    CaptureConnectionRole.Supplemental,
                    supplemental.ConnectionOrdinal);
                return true;
            }

            admission = default;
            return false;
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

        if (state.IsLocked &&
            state.TryGetSupplemental(in connection, out var supplemental) &&
            ReferenceEquals(state, Volatile.Read(ref _currentState)))
        {
            connectionOrdinal = supplemental.ConnectionOrdinal;
            return true;
        }

        var reverse = connection.Reverse();
        if (state.IsLocked &&
            state.TryGetSupplemental(in reverse, out supplemental) &&
            ReferenceEquals(state, Volatile.Read(ref _currentState)))
        {
            connectionOrdinal = supplemental.ConnectionOrdinal;
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
            if (state.TryGetRetiredAttempt(in connection, out var retiredAttempt))
            {
                if (connectionOrdinal <= retiredAttempt.ConnectionOrdinal)
                {
                    admission = new CapturePacketAdmission(
                        CapturePacketAdmissionKind.Rejected,
                        state.Generation,
                        ReleasedLock: false);
                    replacedConnection = false;
                    return false;
                }

                var restarted = state.WithoutRetiredAttempt(in connection);
                if (Interlocked.CompareExchange(ref _currentState, restarted, state) != state)
                    continue;

                state = restarted;
            }
            else if (state.IsRetired(connection))
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
                admission = new CapturePacketAdmission(
                    CapturePacketAdmissionKind.ActiveConnection,
                    state.Generation,
                    ReleasedLock: false,
                    CaptureConnectionRole.Primary,
                    state.ConnectionOrdinal);
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

            if (state.IsLocked)
            {
                foreach (var supplemental in state.SupplementalConnections)
                {
                    retiredConnections = AddRetiredConnection(retiredConnections, supplemental.Connection);
                    retiredConnections = AddRetiredConnection(retiredConnections, supplemental.Connection.Reverse());
                }
            }

            retiredConnections = AddRetiredConnection(retiredConnections, connection.Reverse());
            var promoted = GateState.CreateLocked(
                nextGeneration,
                connection,
                retiredConnections,
                connectionOrdinal,
                state.RetiredAttempts);
            if (Interlocked.CompareExchange(ref _currentState, promoted, state) != state)
            {
                continue;
            }

            replacedConnection = state.IsLocked;
            admission = new CapturePacketAdmission(
                CapturePacketAdmissionKind.ActiveConnection,
                nextGeneration,
                ReleasedLock: false,
                CaptureConnectionRole.Primary,
                connectionOrdinal);
            CaptureLog.Write(
                CaptureLogLevel.Info,
                replacedConnection ? "Confirmed replacement game connection" : "Confirmed game connection");
            return true;
        }
    }

    internal static bool TryPromoteSupplemental(
        in TcpConnection connection,
        long connectionOrdinal,
        out CapturePacketAdmission admission) =>
        TryPromoteSupplemental(
            in connection,
            connectionOrdinal,
            out admission,
            out _);

    internal static bool TryPromoteSupplemental(
        in TcpConnection connection,
        long connectionOrdinal,
        out CapturePacketAdmission admission,
        out CaptureConnectionEviction eviction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionOrdinal);

        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsLocked ||
                state.Connection == connection ||
                state.Connection.Reverse() == connection)
            {
                admission = default;
                eviction = default;
                return false;
            }

            if (state.TryGetRetiredAttempt(in connection, out var retiredAttempt))
            {
                if (connectionOrdinal <= retiredAttempt.ConnectionOrdinal)
                {
                    admission = default;
                    eviction = default;
                    return false;
                }

                var restarted = state.WithoutRetiredAttempt(in connection);
                if (Interlocked.CompareExchange(ref _currentState, restarted, state) != state)
                    continue;

                state = restarted;
            }
            else if (state.IsRetired(connection))
            {
                var restarted = state.WithoutRetiredConnection(connection);
                if (Interlocked.CompareExchange(ref _currentState, restarted, state) != state)
                    continue;

                state = restarted;
            }

            if (state.TryGetSupplementalAnyDirection(in connection, out var current))
            {
                if (connectionOrdinal < current.ConnectionOrdinal)
                {
                    admission = default;
                    eviction = default;
                    return false;
                }

                if (connectionOrdinal == current.ConnectionOrdinal)
                {
                    Interlocked.Exchange(ref current.LastActivityTicks, Stopwatch.GetTimestamp());
                    admission = new CapturePacketAdmission(
                        CapturePacketAdmissionKind.ActiveConnection,
                        state.Generation,
                        ReleasedLock: false,
                        CaptureConnectionRole.Supplemental,
                        current.ConnectionOrdinal);
                    eviction = default;
                    return true;
                }
            }

            var promoted = state.WithSupplemental(connection, connectionOrdinal, out var pendingEviction);
            if (Interlocked.CompareExchange(ref _currentState, promoted, state) != state)
                continue;

            admission = new CapturePacketAdmission(
                CapturePacketAdmissionKind.ActiveConnection,
                promoted.Generation,
                ReleasedLock: false,
                CaptureConnectionRole.Supplemental,
                connectionOrdinal);
            eviction = pendingEviction;
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
            if (!state.IsLocked)
            {
                closedConnection = default;
                return false;
            }

            var closesPrimary = state.Connection == observedConnection || state.Connection.Reverse() == observedConnection;
            if (closesPrimary)
            {
                if ((expectedConnectionOrdinal > 0 && state.ConnectionOrdinal != expectedConnectionOrdinal) ||
                    (expectedConnectionOrdinal <= 0 && expectedGeneration > 0 && state.Generation != expectedGeneration))
                {
                    closedConnection = default;
                    return false;
                }

                var replacement = state.PromoteMostRecentSupplemental(out var nextState);
                if (Interlocked.CompareExchange(ref _currentState, nextState, state) != state)
                    continue;

                CaptureLog.Write(
                    CaptureLogLevel.Info,
                    replacement
                        ? "FIN/RST detected, retained a supplemental game connection"
                        : "FIN/RST detected, unlocked");
                closedConnection = state.Connection;
                return true;
            }

            if (!state.TryGetSupplementalAnyDirection(in observedConnection, out var supplemental))
            {
                closedConnection = default;
                return false;
            }

            if ((expectedConnectionOrdinal > 0 && supplemental.ConnectionOrdinal != expectedConnectionOrdinal) ||
                (expectedConnectionOrdinal <= 0 && expectedGeneration > 0 && state.Generation != expectedGeneration))
            {
                closedConnection = default;
                return false;
            }

            var withoutSupplemental = state.WithoutSupplemental(in observedConnection);
            if (ReferenceEquals(withoutSupplemental, state) ||
                Interlocked.CompareExchange(ref _currentState, withoutSupplemental, state) != state)
            {
                if (ReferenceEquals(withoutSupplemental, state))
                {
                    closedConnection = default;
                    return false;
                }

                continue;
            }

            closedConnection = supplemental.Connection;
            return true;
        }
    }

    private static bool IsExpired(GateState state, long now)
    {
        var lastActivity = state.LatestActivityTicks();
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
            foreach (var supplemental in state.SupplementalConnections)
            {
                retiredConnections = AddRetiredConnection(retiredConnections, supplemental.Connection);
                retiredConnections = AddRetiredConnection(retiredConnections, supplemental.Connection.Reverse());
            }
        }
        var unlocked = GateState.CreateUnlocked(generation, retiredConnections, state.RetiredAttempts);
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

    private static TcpConnection[] RemoveRetiredConnection(
        TcpConnection[] retiredConnections,
        in TcpConnection connection)
    {
        var index = Array.IndexOf(retiredConnections, connection);
        if (index < 0)
            return retiredConnections;

        var result = new TcpConnection[retiredConnections.Length - 1];
        retiredConnections.AsSpan(0, index).CopyTo(result);
        retiredConnections.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }

    private static RetiredConnectionAttempt[] AddRetiredAttempt(
        RetiredConnectionAttempt[] retiredAttempts,
        in TcpConnection connection,
        long connectionOrdinal)
    {
        for (var index = 0; index < retiredAttempts.Length; index++)
        {
            var existing = retiredAttempts[index];
            if (existing.Connection != connection && existing.Connection.Reverse() != connection)
                continue;

            if (existing.ConnectionOrdinal >= connectionOrdinal)
                return retiredAttempts;

            var updated = (RetiredConnectionAttempt[])retiredAttempts.Clone();
            updated[index] = new RetiredConnectionAttempt(existing.Connection, connectionOrdinal);
            return updated;
        }

        var retainedCount = Math.Min(retiredAttempts.Length, RetiredAttemptLimit - 1);
        var result = new RetiredConnectionAttempt[retainedCount + 1];
        if (retainedCount != 0)
            retiredAttempts.AsSpan(retiredAttempts.Length - retainedCount).CopyTo(result);

        result[^1] = new RetiredConnectionAttempt(connection, connectionOrdinal);
        return result;
    }

    private sealed class GateState(
        long generation,
        bool isLocked,
        TcpConnection connection,
        TcpConnection[] retiredConnections,
        RetiredConnectionAttempt[] retiredAttempts,
        SupplementalConnection[] supplementalConnections,
        long connectionOrdinal,
        long lastActivityTicks)
    {
        public readonly long Generation = generation;
        public readonly bool IsLocked = isLocked;
        public readonly TcpConnection Connection = connection;
        public readonly TcpConnection[] RetiredConnections = retiredConnections;
        public readonly RetiredConnectionAttempt[] RetiredAttempts = retiredAttempts;
        public readonly SupplementalConnection[] SupplementalConnections = supplementalConnections;
        public readonly long ConnectionOrdinal = connectionOrdinal;
        public long LastActivityTicks = lastActivityTicks;

        public bool IsRetired(in TcpConnection connection) => Array.IndexOf(RetiredConnections, connection) >= 0;

        public bool TryGetRetiredAttempt(
            in TcpConnection connection,
            out RetiredConnectionAttempt retiredAttempt)
        {
            for (var index = 0; index < RetiredAttempts.Length; index++)
            {
                var candidate = RetiredAttempts[index];
                if (candidate.Connection == connection || candidate.Connection.Reverse() == connection)
                {
                    retiredAttempt = candidate;
                    return true;
                }
            }

            retiredAttempt = default;
            return false;
        }

        public bool TryGetSupplemental(in TcpConnection connection, out SupplementalConnection supplemental)
        {
            for (var index = 0; index < SupplementalConnections.Length; index++)
            {
                var candidate = SupplementalConnections[index];
                if (candidate.Connection == connection)
                {
                    supplemental = candidate;
                    return true;
                }
            }

            supplemental = null!;
            return false;
        }

        public bool TryGetSupplementalAnyDirection(in TcpConnection connection, out SupplementalConnection supplemental)
        {
            if (TryGetSupplemental(in connection, out supplemental))
                return true;

            var reverse = connection.Reverse();
            return TryGetSupplemental(in reverse, out supplemental);
        }

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
                RetiredAttempts,
                SupplementalConnections,
                ConnectionOrdinal,
                Interlocked.Read(ref LastActivityTicks));
        }

        public GateState WithoutRetiredAttempt(in TcpConnection connection)
        {
            var index = -1;
            for (var candidateIndex = 0; candidateIndex < RetiredAttempts.Length; candidateIndex++)
            {
                var candidate = RetiredAttempts[candidateIndex];
                if (candidate.Connection == connection || candidate.Connection.Reverse() == connection)
                {
                    index = candidateIndex;
                    break;
                }
            }

            if (index < 0)
                return this;

            var retiredAttempts = new RetiredConnectionAttempt[RetiredAttempts.Length - 1];
            RetiredAttempts.AsSpan(0, index).CopyTo(retiredAttempts);
            RetiredAttempts.AsSpan(index + 1).CopyTo(retiredAttempts.AsSpan(index));
            var retiredConnections = RetiredConnections;
            retiredConnections = RemoveRetiredConnection(retiredConnections, connection);
            retiredConnections = RemoveRetiredConnection(retiredConnections, connection.Reverse());
            return new GateState(
                Generation,
                IsLocked,
                Connection,
                retiredConnections,
                retiredAttempts,
                SupplementalConnections,
                ConnectionOrdinal,
                Interlocked.Read(ref LastActivityTicks));
        }

        public static GateState CreateUnlocked(
            long generation,
            TcpConnection[] retiredConnections,
            RetiredConnectionAttempt[]? retiredAttempts = null) =>
            new(generation, false, default, retiredConnections, retiredAttempts ?? [], [], 0, 0);

        public static GateState CreateLocked(
            long generation,
            in TcpConnection connection,
            TcpConnection[] retiredConnections,
            long connectionOrdinal,
            RetiredConnectionAttempt[]? retiredAttempts = null) =>
            new(
                generation,
                true,
                connection,
                retiredConnections,
                retiredAttempts ?? [],
                [],
                connectionOrdinal,
                Stopwatch.GetTimestamp());

        public GateState WithSupplemental(
            in TcpConnection connection,
            long connectionOrdinal,
            out CaptureConnectionEviction eviction)
        {
            var replacedIndex = -1;
            for (var index = 0; index < SupplementalConnections.Length; index++)
            {
                var existing = SupplementalConnections[index];
                if (existing.Connection == connection || existing.Connection.Reverse() == connection)
                {
                    replacedIndex = index;
                    break;
                }
            }

            var evictedIndex = -1;
            if (replacedIndex < 0 &&
                SupplementalConnections.Length >= CaptureBufferLimits.CandidateStreamCountLimit - 1)
            {
                evictedIndex = FindLeastRecentlyUsedSupplemental();
            }

            var removedIndex = replacedIndex >= 0 ? replacedIndex : evictedIndex;
            var nextLength = removedIndex >= 0
                ? SupplementalConnections.Length
                : SupplementalConnections.Length + 1;
            var next = new SupplementalConnection[nextLength];
            var count = 0;
            for (var index = 0; index < SupplementalConnections.Length; index++)
            {
                if (index == removedIndex)
                    continue;

                next[count++] = SupplementalConnections[index];
            }

            next[count] = new SupplementalConnection(connection, connectionOrdinal, Stopwatch.GetTimestamp());
            eviction = evictedIndex >= 0
                ? new CaptureConnectionEviction(
                    SupplementalConnections[evictedIndex].Connection,
                    SupplementalConnections[evictedIndex].ConnectionOrdinal)
                : default;

            var retiredConnections = RetiredConnections;
            var retiredAttempts = RetiredAttempts;
            if (evictedIndex >= 0)
            {
                var evicted = SupplementalConnections[evictedIndex];
                var evictedConnection = evicted.Connection;
                retiredConnections = AddRetiredConnection(retiredConnections, evictedConnection);
                retiredConnections = AddRetiredConnection(retiredConnections, evictedConnection.Reverse());
                retiredAttempts = AddRetiredAttempt(
                    retiredAttempts,
                    in evictedConnection,
                    evicted.ConnectionOrdinal);
            }

            return new GateState(
                Generation,
                IsLocked,
                Connection,
                retiredConnections,
                retiredAttempts,
                next,
                ConnectionOrdinal,
                Interlocked.Read(ref LastActivityTicks));
        }

        private int FindLeastRecentlyUsedSupplemental()
        {
            var selectedIndex = 0;
            var selectedActivity = Interlocked.Read(ref SupplementalConnections[0].LastActivityTicks);
            for (var index = 1; index < SupplementalConnections.Length; index++)
            {
                var candidateActivity = Interlocked.Read(ref SupplementalConnections[index].LastActivityTicks);
                if (candidateActivity >= selectedActivity)
                    continue;

                selectedIndex = index;
                selectedActivity = candidateActivity;
            }

            return selectedIndex;
        }

        public GateState WithoutSupplemental(in TcpConnection connection)
        {
            var index = -1;
            for (var candidateIndex = 0; candidateIndex < SupplementalConnections.Length; candidateIndex++)
            {
                var candidate = SupplementalConnections[candidateIndex];
                if (candidate.Connection == connection || candidate.Connection.Reverse() == connection)
                {
                    index = candidateIndex;
                    break;
                }
            }

            if (index < 0)
                return this;

            var next = new SupplementalConnection[SupplementalConnections.Length - 1];
            SupplementalConnections.AsSpan(0, index).CopyTo(next);
            SupplementalConnections.AsSpan(index + 1).CopyTo(next.AsSpan(index));
            var retired = AddRetiredConnection(RetiredConnections, SupplementalConnections[index].Connection);
            retired = AddRetiredConnection(retired, SupplementalConnections[index].Connection.Reverse());
            return new GateState(
                Generation,
                IsLocked,
                Connection,
                retired,
                RetiredAttempts,
                next,
                ConnectionOrdinal,
                Interlocked.Read(ref LastActivityTicks));
        }

        public bool PromoteMostRecentSupplemental(out GateState nextState)
        {
            if (SupplementalConnections.Length == 0)
            {
                var retired = AddRetiredConnection(RetiredConnections, Connection);
                retired = AddRetiredConnection(retired, Connection.Reverse());
                nextState = CreateUnlocked(Generation + 1, retired, RetiredAttempts);
                return false;
            }

            var selectedIndex = 0;
            var selected = SupplementalConnections[0];
            for (var index = 1; index < SupplementalConnections.Length; index++)
            {
                var candidate = SupplementalConnections[index];
                if (candidate.LastActivityTicks > selected.LastActivityTicks ||
                    candidate.LastActivityTicks == selected.LastActivityTicks &&
                    candidate.ConnectionOrdinal > selected.ConnectionOrdinal)
                {
                    selectedIndex = index;
                    selected = candidate;
                }
            }

            var remaining = new SupplementalConnection[SupplementalConnections.Length - 1];
            SupplementalConnections.AsSpan(0, selectedIndex).CopyTo(remaining);
            SupplementalConnections.AsSpan(selectedIndex + 1).CopyTo(remaining.AsSpan(selectedIndex));
            var retiredConnections = AddRetiredConnection(RetiredConnections, Connection);
            retiredConnections = AddRetiredConnection(retiredConnections, Connection.Reverse());
            nextState = new GateState(
                Generation,
                true,
                selected.Connection,
                retiredConnections,
                RetiredAttempts,
                remaining,
                selected.ConnectionOrdinal,
                selected.LastActivityTicks);
            return true;
        }

        public long LatestActivityTicks()
        {
            var latest = Interlocked.Read(ref LastActivityTicks);
            for (var index = 0; index < SupplementalConnections.Length; index++)
                latest = Math.Max(latest, Interlocked.Read(ref SupplementalConnections[index].LastActivityTicks));

            return latest;
        }
    }

    private sealed class SupplementalConnection(
        TcpConnection connection,
        long connectionOrdinal,
        long lastActivityTicks)
    {
        public TcpConnection Connection { get; } = connection;
        public long ConnectionOrdinal { get; } = connectionOrdinal;
        public long LastActivityTicks = lastActivityTicks;
    }

    private readonly record struct RetiredConnectionAttempt(
        TcpConnection Connection,
        long ConnectionOrdinal);
}
