using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

public enum CapturePacketAdmissionKind : byte
{
    Rejected,
    UnlockedCandidate,
    LockedConnection
}

public readonly record struct CapturePacketAdmission(CapturePacketAdmissionKind Kind, long Generation, bool ReleasedLock)
{
    public bool IsAccepted => Kind != CapturePacketAdmissionKind.Rejected;
    public bool RequiresProcessPortMatch => Kind == CapturePacketAdmissionKind.UnlockedCandidate;
}

public static class CaptureConnectionGate
{
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(5);
    private static GateState _currentState = GateState.CreateUnlocked(0);

    public static bool IsLocked => TryGetLockedConnection(out _);

    public static CapturePacketAdmission EvaluatePacket(in TcpConnection connection, bool hasCloseFlag)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (!state.IsLocked)
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.UnlockedCandidate, state.Generation, ReleasedLock: false);
            }

            var now = Stopwatch.GetTimestamp();
            if (IsExpired(state, now))
            {
                if (TryRelease(state, "Connection idle timeout, unlocked", out var generation))
                {
                    return new CapturePacketAdmission(CapturePacketAdmissionKind.UnlockedCandidate, generation, ReleasedLock: true);
                }

                continue;
            }

            if (state.Connection != connection)
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.Rejected, state.Generation, ReleasedLock: false);
            }

            Interlocked.Exchange(ref state.LastActivityTicks, now);
            if (!hasCloseFlag)
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.LockedConnection, state.Generation, ReleasedLock: false);
            }

            if (TryRelease(state, "FIN/RST detected, unlocked", out var releasedGeneration))
            {
                return new CapturePacketAdmission(CapturePacketAdmissionKind.UnlockedCandidate, releasedGeneration, ReleasedLock: true);
            }
        }
    }

    public static void Unlock()
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            var unlocked = GateState.CreateUnlocked(state.Generation + 1);
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
                if (TryRelease(state, "Connection idle timeout, unlocked", out _))
                {
                    connection = default;
                    return false;
                }

                continue;
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
        if (!admission.IsAccepted)
        {
            return false;
        }

        var state = Volatile.Read(ref _currentState);
        return state.Generation == admission.Generation &&
               (!state.IsLocked || state.Connection == connection);
    }

    internal static bool TryLock(in TcpConnection connection, long generation, out bool acquired)
    {
        while (true)
        {
            var state = Volatile.Read(ref _currentState);
            if (state.Generation != generation)
            {
                acquired = false;
                return false;
            }

            if (state.IsLocked)
            {
                acquired = false;
                return state.Connection == connection;
            }

            var locked = GateState.CreateLocked(generation, connection);
            if (Interlocked.CompareExchange(ref _currentState, locked, state) == state)
            {
                acquired = true;
                return true;
            }
        }
    }

    private static bool IsExpired(GateState state, long now)
    {
        var lastActivity = Interlocked.Read(ref state.LastActivityTicks);
        return now >= lastActivity && Stopwatch.GetElapsedTime(lastActivity, now) > _idleTimeout;
    }

    private static bool TryRelease(GateState state, string message, out long generation)
    {
        generation = state.Generation + 1;
        var unlocked = GateState.CreateUnlocked(generation);
        if (Interlocked.CompareExchange(ref _currentState, unlocked, state) != state)
        {
            return false;
        }

        CaptureLog.Write(CaptureLogLevel.Info, message);
        return true;
    }

    private sealed class GateState(long generation, bool isLocked, TcpConnection connection, long lastActivityTicks)
    {
        public readonly long Generation = generation;
        public readonly bool IsLocked = isLocked;
        public readonly TcpConnection Connection = connection;
        public long LastActivityTicks = lastActivityTicks;

        public static GateState CreateUnlocked(long generation) => new(generation, false, default, 0);
        public static GateState CreateLocked(long generation, in TcpConnection connection) => new(generation, true, connection, Stopwatch.GetTimestamp());
    }
}
