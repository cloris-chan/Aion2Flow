using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal readonly record struct TcpDownstreamPacketResolution(
    bool IsExpectedDownstream,
    uint? InitialSequenceNumber,
    long ExpectedConnectionOrdinal,
    bool HasResolvedConnectionOrdinal,
    long ResolvedConnectionOrdinal);

internal sealed class TcpDownstreamConnectionTracker
{
    private const int ConnectionLimit = 256;
    private const uint SequenceProximityWindow = 16 * 1024 * 1024;
    private static readonly TimeSpan _expirationScanInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _pendingConnectionLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _previousConnectionLifetime = TimeSpan.FromMinutes(2);
    private readonly Dictionary<TcpConnection, ConnectionStart> _connections = [];
    private readonly Dictionary<TcpConnection, ConnectionStart> _previousConnections = [];
    private readonly Lock _gate = new();
    private bool _hasExpirationScanTimestamp;
    private long _lastExpirationScanTimestamp;

    public bool ObserveSyn(
        in TcpConnection connection,
        bool hasAcknowledgment,
        bool acceptUnpairedAcknowledgment,
        uint sequenceNumber,
        uint acknowledgmentNumber,
        long newConnectionOrdinal,
        long observedTimestamp)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newConnectionOrdinal);
        lock (_gate)
        {
            var downstreamConnection = hasAcknowledgment ? connection : connection.Reverse();
            ExpireRelevant(in downstreamConnection, observedTimestamp);
            ExpireIfDue(observedTimestamp);
            if (!hasAcknowledgment)
            {
                var downstream = downstreamConnection;
                if (_connections.TryGetValue(downstream, out var existing))
                {
                    var isSameHandshake = existing.ClientInitialSequenceNumber == sequenceNumber ||
                                          (existing.ClientInitialSequenceNumber is null && !existing.IsPromoted);
                    if (isSameHandshake)
                    {
                        _connections[downstream] = existing with
                        {
                            ObservedTimestamp = observedTimestamp,
                            ClientInitialSequenceNumber = sequenceNumber
                        };
                        return false;
                    }

                    RememberPrevious(in downstream, in existing);
                    _connections[downstream] = new ConnectionStart(
                        observedTimestamp,
                        sequenceNumber,
                        InitialSequenceNumber: null,
                        newConnectionOrdinal,
                        IsPromoted: false);
                    return true;
                }

                EnsureSlot();
                _connections[downstream] = new ConnectionStart(
                    observedTimestamp,
                    sequenceNumber,
                    InitialSequenceNumber: null,
                    newConnectionOrdinal,
                    IsPromoted: false);
                return true;
            }

            if (_connections.TryGetValue(connection, out var start))
            {
                var initialSequenceNumber = unchecked(sequenceNumber + 1);
                if (start.ClientInitialSequenceNumber is { } clientInitialSequenceNumber &&
                    acknowledgmentNumber != unchecked(clientInitialSequenceNumber + 1))
                {
                    if (_previousConnections.TryGetValue(connection, out var previous) &&
                        previous.ClientInitialSequenceNumber is { } previousClientInitialSequenceNumber &&
                        acknowledgmentNumber == unchecked(previousClientInitialSequenceNumber + 1))
                    {
                        _previousConnections[connection] = previous with
                        {
                            ObservedTimestamp = observedTimestamp,
                            InitialSequenceNumber = initialSequenceNumber
                        };
                    }

                    return false;
                }

                if (start.InitialSequenceNumber is null || start.InitialSequenceNumber == initialSequenceNumber)
                {
                    _connections[connection] = start with
                    {
                        ObservedTimestamp = observedTimestamp,
                        InitialSequenceNumber = initialSequenceNumber
                    };
                    return false;
                }

                RememberPrevious(in connection, in start);
                _connections[connection] = new ConnectionStart(
                    observedTimestamp,
                    ClientInitialSequenceNumber: null,
                    initialSequenceNumber,
                    newConnectionOrdinal,
                    IsPromoted: false);
                return true;
            }

            if (acceptUnpairedAcknowledgment)
            {
                EnsureSlot();
                _connections[connection] = new ConnectionStart(
                    observedTimestamp,
                    ClientInitialSequenceNumber: null,
                    InitialSequenceNumber: unchecked(sequenceNumber + 1),
                    newConnectionOrdinal,
                    IsPromoted: false);
                return true;
            }

            return false;
        }
    }

    public bool TryGet(
        in TcpConnection connection,
        long observedTimestamp,
        out uint? initialSequenceNumber,
        out long connectionOrdinal)
    {
        lock (_gate)
        {
            ExpireRelevant(in connection, observedTimestamp);
            ExpireIfDue(observedTimestamp);
            return TryGetPendingDownstream(
                in connection,
                out initialSequenceNumber,
                out connectionOrdinal);
        }
    }

    public TcpDownstreamPacketResolution ResolvePacket(
        in TcpConnection connection,
        uint sequenceNumber,
        bool hasAcknowledgment,
        uint acknowledgmentNumber,
        long observedTimestamp)
    {
        lock (_gate)
        {
            ExpireRelevant(in connection, observedTimestamp);
            ExpireIfDue(observedTimestamp);
            var isExpectedDownstream = TryGetPendingDownstream(
                in connection,
                out var initialSequenceNumber,
                out var expectedConnectionOrdinal);
            var hasResolvedConnectionOrdinal = TryResolvePacketOrdinalCore(
                in connection,
                sequenceNumber,
                hasAcknowledgment,
                acknowledgmentNumber,
                observedTimestamp,
                out var resolvedConnectionOrdinal);
            return new TcpDownstreamPacketResolution(
                isExpectedDownstream,
                initialSequenceNumber,
                expectedConnectionOrdinal,
                hasResolvedConnectionOrdinal,
                resolvedConnectionOrdinal);
        }
    }

    public void MarkPromoted(in TcpConnection connection, long expectedConnectionOrdinal)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(connection, out var start) &&
                start.ConnectionOrdinal == expectedConnectionOrdinal)
            {
                _connections[connection] = start with { IsPromoted = true };
            }
        }
    }

    public bool TryResolvePacketOrdinal(
        in TcpConnection connection,
        uint sequenceNumber,
        bool hasAcknowledgment,
        uint acknowledgmentNumber,
        long observedTimestamp,
        out long connectionOrdinal)
    {
        lock (_gate)
        {
            ExpireRelevant(in connection, observedTimestamp);
            ExpireIfDue(observedTimestamp);
            return TryResolvePacketOrdinalCore(
                in connection,
                sequenceNumber,
                hasAcknowledgment,
                acknowledgmentNumber,
                observedTimestamp,
                out connectionOrdinal);
        }
    }

    private bool TryGetPendingDownstream(
        in TcpConnection connection,
        out uint? initialSequenceNumber,
        out long connectionOrdinal)
    {
        if (_connections.TryGetValue(connection, out var start) && !start.IsPromoted)
        {
            initialSequenceNumber = start.InitialSequenceNumber;
            connectionOrdinal = start.ConnectionOrdinal;
            return true;
        }

        initialSequenceNumber = null;
        connectionOrdinal = 0;
        return false;
    }

    private bool TryResolvePacketOrdinalCore(
        in TcpConnection connection,
        uint sequenceNumber,
        bool hasAcknowledgment,
        uint acknowledgmentNumber,
        long observedTimestamp,
        out long connectionOrdinal)
    {
        var downstream = connection;
        var isDownstream = true;
        if (!_connections.TryGetValue(downstream, out var current))
        {
            downstream = connection.Reverse();
            isDownstream = false;
            if (!_connections.TryGetValue(downstream, out current))
            {
                connectionOrdinal = 0;
                return false;
            }
        }

        if (!_previousConnections.TryGetValue(downstream, out var previous))
        {
            connectionOrdinal = current.ConnectionOrdinal;
            _connections[downstream] = TouchPacket(in current, isDownstream, sequenceNumber, observedTimestamp);
            return true;
        }

        if (hasAcknowledgment &&
            TryResolveByAcknowledgment(
                in current,
                in previous,
                isDownstream,
                acknowledgmentNumber,
                out var acknowledgmentResolvesPrevious))
        {
            connectionOrdinal = acknowledgmentResolvesPrevious
                ? previous.ConnectionOrdinal
                : current.ConnectionOrdinal;
            if (acknowledgmentResolvesPrevious)
            {
                _previousConnections[downstream] = TouchPacket(
                    in previous,
                    isDownstream,
                    sequenceNumber,
                    observedTimestamp);
            }
            else
            {
                _connections[downstream] = TouchPacket(
                    in current,
                    isDownstream,
                    sequenceNumber,
                    observedTimestamp);
            }

            return true;
        }

        var hasCurrentDistance = TryGetSequenceDistance(in current, isDownstream, sequenceNumber, out var currentDistance);
        var hasPreviousDistance = TryGetSequenceDistance(in previous, isDownstream, sequenceNumber, out var previousDistance);
        var currentHasSequenceAnchor = isDownstream
            ? current.InitialSequenceNumber.HasValue
            : current.ClientInitialSequenceNumber.HasValue;
        var previousLastSequenceNumber = isDownstream
            ? previous.LastDownstreamSequenceNumber
            : previous.LastUpstreamSequenceNumber;
        var resolvedPrevious = !currentHasSequenceAnchor
            ? previousLastSequenceNumber is { } previousLast && IsNearSequence(sequenceNumber, previousLast)
            : hasPreviousDistance && (!hasCurrentDistance || previousDistance < currentDistance);
        if (resolvedPrevious)
        {
            connectionOrdinal = previous.ConnectionOrdinal;
            _previousConnections[downstream] = TouchPacket(in previous, isDownstream, sequenceNumber, observedTimestamp);
        }
        else
        {
            connectionOrdinal = current.ConnectionOrdinal;
            _connections[downstream] = TouchPacket(in current, isDownstream, sequenceNumber, observedTimestamp);
        }

        return true;
    }

    public void Remove(in TcpConnection connection)
    {
        lock (_gate)
        {
            _connections.Remove(connection);
            _connections.Remove(connection.Reverse());
            _previousConnections.Remove(connection);
            _previousConnections.Remove(connection.Reverse());
        }
    }

    public void Remove(in TcpConnection connection, long expectedConnectionOrdinal)
    {
        lock (_gate)
        {
            var downstream = _connections.ContainsKey(connection) ? connection : connection.Reverse();
            if (_connections.TryGetValue(downstream, out var current) &&
                current.ConnectionOrdinal == expectedConnectionOrdinal)
            {
                _connections.Remove(downstream);
                _previousConnections.Remove(downstream);
                return;
            }

            if (_previousConnections.TryGetValue(downstream, out var previous) &&
                previous.ConnectionOrdinal == expectedConnectionOrdinal)
            {
                // Keep the immediately previous attempt as a tombstone. Repeated FIN/RST
                // packets must continue to resolve to the stale ordinal instead of the
                // current same-tuple attempt.
                return;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _connections.Clear();
            _previousConnections.Clear();
            _hasExpirationScanTimestamp = false;
            _lastExpirationScanTimestamp = 0;
        }
    }

    private void ExpireIfDue(long observedTimestamp)
    {
        if (_hasExpirationScanTimestamp &&
            observedTimestamp >= _lastExpirationScanTimestamp &&
            Stopwatch.GetElapsedTime(_lastExpirationScanTimestamp, observedTimestamp) < _expirationScanInterval)
        {
            return;
        }

        Expire(observedTimestamp);
        _lastExpirationScanTimestamp = observedTimestamp;
        _hasExpirationScanTimestamp = true;
    }

    private void ExpireRelevant(in TcpConnection connection, long observedTimestamp)
    {
        ExpireRelevantDirection(in connection, observedTimestamp);
        var reverseConnection = connection.Reverse();
        ExpireRelevantDirection(in reverseConnection, observedTimestamp);
    }

    private void ExpireRelevantDirection(in TcpConnection downstream, long observedTimestamp)
    {
        if (_connections.TryGetValue(downstream, out var current) &&
            !current.IsPromoted &&
            IsExpired(current.ObservedTimestamp, observedTimestamp, _pendingConnectionLifetime))
        {
            ExpirePendingConnection(in downstream, observedTimestamp);
            return;
        }

        if (_previousConnections.TryGetValue(downstream, out var previous) &&
            IsExpired(previous.ObservedTimestamp, observedTimestamp, _previousConnectionLifetime))
        {
            _previousConnections.Remove(downstream);
        }
    }

    private void Expire(long observedTimestamp)
    {
        List<TcpConnection>? expired = null;
        foreach (var (connection, start) in _connections)
        {
            if (!start.IsPromoted &&
                IsExpired(start.ObservedTimestamp, observedTimestamp, _pendingConnectionLifetime))
            {
                expired ??= [];
                expired.Add(connection);
            }
        }

        if (expired is not null)
        {
            foreach (var connection in expired)
            {
                ExpirePendingConnection(in connection, observedTimestamp);
            }
        }

        expired = null;
        foreach (var (connection, start) in _previousConnections)
        {
            if (IsExpired(start.ObservedTimestamp, observedTimestamp, _previousConnectionLifetime))
            {
                expired ??= [];
                expired.Add(connection);
            }
        }

        if (expired is not null)
        {
            foreach (var connection in expired)
            {
                _previousConnections.Remove(connection);
            }
        }
    }

    private void ExpirePendingConnection(in TcpConnection connection, long observedTimestamp)
    {
        if (!_connections.TryGetValue(connection, out var expiredCurrent) ||
            expiredCurrent.IsPromoted ||
            !IsExpired(expiredCurrent.ObservedTimestamp, observedTimestamp, _pendingConnectionLifetime))
        {
            return;
        }

        _connections.Remove(connection);
        if (!_previousConnections.Remove(connection, out var previous))
        {
            return;
        }

        var previousLifetime = previous.IsPromoted
            ? _previousConnectionLifetime
            : _pendingConnectionLifetime;
        if (IsExpired(previous.ObservedTimestamp, observedTimestamp, previousLifetime))
        {
            return;
        }

        // The newer handshake failed without replacing the still-valid older attempt.
        // Restore that attempt as current and retain the failed attempt as a tombstone,
        // so a delayed FIN/RST can still be rejected by ordinal.
        _connections[connection] = previous;
        _previousConnections[connection] = expiredCurrent;
    }

    private void EnsureSlot()
    {
        if (_connections.Count < ConnectionLimit)
        {
            return;
        }

        var oldestConnection = default(TcpConnection);
        var oldestTimestamp = long.MaxValue;
        var selectedPromotedConnection = true;
        foreach (var (connection, start) in _connections)
        {
            if ((!start.IsPromoted && selectedPromotedConnection) ||
                (start.IsPromoted == selectedPromotedConnection && start.ObservedTimestamp < oldestTimestamp))
            {
                oldestConnection = connection;
                oldestTimestamp = start.ObservedTimestamp;
                selectedPromotedConnection = start.IsPromoted;
            }
        }

        _connections.Remove(oldestConnection);
        _previousConnections.Remove(oldestConnection);
    }

    private static bool IsExpired(long startTimestamp, long observedTimestamp, TimeSpan lifetime) =>
        observedTimestamp >= startTimestamp &&
        Stopwatch.GetElapsedTime(startTimestamp, observedTimestamp) > lifetime;

    private void RememberPrevious(in TcpConnection connection, in ConnectionStart start)
    {
        _previousConnections[connection] = start;
    }

    private static bool TryGetSequenceDistance(
        in ConnectionStart start,
        bool isDownstream,
        uint sequenceNumber,
        out uint distance)
    {
        var initialSequenceNumber = isDownstream
            ? start.InitialSequenceNumber
            : start.ClientInitialSequenceNumber is { } clientInitialSequenceNumber
                ? unchecked(clientInitialSequenceNumber + 1)
                : null;
        if (initialSequenceNumber is not { } initial)
        {
            distance = 0;
            return false;
        }

        distance = unchecked(sequenceNumber - initial);
        return distance < 0x80000000u;
    }

    private static bool TryResolveByAcknowledgment(
        in ConnectionStart current,
        in ConnectionStart previous,
        bool isDownstream,
        uint acknowledgmentNumber,
        out bool resolvesPrevious)
    {
        var hasCurrentDistance = TryGetAcknowledgmentDistance(
            in current,
            isDownstream,
            acknowledgmentNumber,
            out var currentDistance);
        var hasPreviousDistance = TryGetAcknowledgmentDistance(
            in previous,
            isDownstream,
            acknowledgmentNumber,
            out var previousDistance);
        if (!hasCurrentDistance && !hasPreviousDistance)
        {
            resolvesPrevious = false;
            return false;
        }

        resolvesPrevious = hasPreviousDistance &&
            (!hasCurrentDistance || previousDistance < currentDistance);
        return true;
    }

    private static bool TryGetAcknowledgmentDistance(
        in ConnectionStart start,
        bool isDownstream,
        uint acknowledgmentNumber,
        out uint distance)
    {
        var acknowledgedInitialSequenceNumber = isDownstream
            ? start.ClientInitialSequenceNumber is { } clientInitialSequenceNumber
                ? unchecked(clientInitialSequenceNumber + 1)
                : null
            : start.InitialSequenceNumber;
        if (acknowledgedInitialSequenceNumber is not { } initial)
        {
            distance = 0;
            return false;
        }

        distance = unchecked(acknowledgmentNumber - initial);
        return distance <= SequenceProximityWindow;
    }

    private static ConnectionStart TouchPacket(
        in ConnectionStart start,
        bool isDownstream,
        uint sequenceNumber,
        long observedTimestamp) =>
        isDownstream
            ? start with
            {
                ObservedTimestamp = observedTimestamp,
                LastDownstreamSequenceNumber = sequenceNumber
            }
            : start with
            {
                ObservedTimestamp = observedTimestamp,
                LastUpstreamSequenceNumber = sequenceNumber
            };

    private static bool IsNearSequence(uint sequenceNumber, uint referenceSequenceNumber) =>
        Math.Min(
            unchecked(sequenceNumber - referenceSequenceNumber),
            unchecked(referenceSequenceNumber - sequenceNumber)) <= SequenceProximityWindow;

    private readonly record struct ConnectionStart(
        long ObservedTimestamp,
        uint? ClientInitialSequenceNumber,
        uint? InitialSequenceNumber,
        long ConnectionOrdinal,
        bool IsPromoted,
        uint? LastDownstreamSequenceNumber = null,
        uint? LastUpstreamSequenceNumber = null);
}
