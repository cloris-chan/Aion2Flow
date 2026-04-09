using Cloris.Aion2Flow.Divert.Network;
using Cloris.Aion2Flow.PacketCapture.Streams;
using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

public static class CaptureConnectionGate
{
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(5);

    public static bool IsLocked => _currentState != null;

    private static volatile LockState? _currentState;


    public static bool ShouldProcessPacket(in TcpConnection connection, TcpControlBits flags, out bool isReversed)
    {
        var state = _currentState;

        if (state == null)
        {
            isReversed = false;
            return true;
        }

        long now = Stopwatch.GetTimestamp();

        long lastActivity = Interlocked.Read(ref state.LastActivityTicks);
        if (Stopwatch.GetElapsedTime(lastActivity, now) > _idleTimeout)
        {
            if (Interlocked.CompareExchange(ref _currentState, null, state) == state)
            {
                Console.WriteLine("[Locker] 连接闲置超时，已安全解锁。");
            }
            isReversed = false;
            return true;
        }

        if (state.Connection.IsSameConnection(in connection, out isReversed))
        {
            Interlocked.Exchange(ref state.LastActivityTicks, now);

            if ((flags & TcpControlBits.FIN) != 0 || (flags & TcpControlBits.RST) != 0)
            {
                if (Interlocked.CompareExchange(ref _currentState, null, state) == state)
                {
                    Console.WriteLine("[Locker] 检测到挥手/重置，已安全解锁。");
                }
            }
            return true;
        }

        return false;
    }

    public static void LockOn(in TcpConnection targetSession)
    {
        var newState = new LockState(targetSession);

        _currentState = newState;
    }

    public static void Unlock()
    {
        _currentState = null;
    }

    public static bool TryGetLockedConnection(out TcpConnection connection)
    {
        var state = _currentState;
        if (state is null)
        {
            connection = default;
            return false;
        }

        connection = state.Connection;
        return true;
    }

    private sealed class LockState(TcpConnection connection)
    {
        public readonly TcpConnection Connection = connection;
        public long LastActivityTicks = Stopwatch.GetTimestamp();
    }
}
