using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal sealed class LatestTcpAcknowledgmentTracker
{
    private readonly Lock _gate = new();
    private Snapshot _latest;
    private bool _hasLatest;

    private long _version;
    private bool _notificationPending;

    public bool Observe(
        in TcpConnection connection,
        long generation,
        long connectionOrdinal,
        uint acknowledgmentNumber,
        long captureOrdinal = 0)
    {
        if (generation <= 0 || connectionOrdinal <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_hasLatest &&
                _latest.Connection == connection &&
                _latest.Generation == generation &&
                _latest.ConnectionOrdinal == connectionOrdinal &&
                TcpSequence.IsBefore(acknowledgmentNumber, _latest.AcknowledgmentNumber))
            {
                return false;
            }

            var version = checked(++_version);
            _latest = new Snapshot(
                connection,
                generation,
                connectionOrdinal,
                acknowledgmentNumber,
                captureOrdinal,
                version);
            _hasLatest = true;
            if (_notificationPending)
            {
                return false;
            }

            _notificationPending = true;
            return true;
        }
    }

    public bool TryGet(
        in TcpConnection connection,
        long generation,
        long connectionOrdinal,
        out uint acknowledgmentNumber)
    {
        lock (_gate)
        {
            if (!_hasLatest ||
                _latest.Connection != connection ||
                _latest.Generation != generation ||
                _latest.ConnectionOrdinal != connectionOrdinal)
            {
                acknowledgmentNumber = 0;
                return false;
            }

            acknowledgmentNumber = _latest.AcknowledgmentNumber;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _latest = default;
            _hasLatest = false;
            _notificationPending = false;
        }
    }

    public bool TryGetLatest(out LatestTcpAcknowledgment acknowledgment)
    {
        lock (_gate)
        {
            if (!_hasLatest)
            {
                acknowledgment = default;
                return false;
            }

            acknowledgment = new LatestTcpAcknowledgment(
                _latest.Connection,
                _latest.Generation,
                _latest.ConnectionOrdinal,
                _latest.AcknowledgmentNumber,
                _latest.CaptureOrdinal,
                _latest.Version);
            return true;
        }
    }

    public bool CompleteNotification(long observedVersion)
    {
        lock (_gate)
        {
            if (_hasLatest && _latest.Version != observedVersion)
            {
                return false;
            }

            _notificationPending = false;
            return true;
        }
    }

    public void CancelNotification()
    {
        lock (_gate)
        {
            _notificationPending = false;
        }
    }

    private readonly record struct Snapshot(
        TcpConnection Connection,
        long Generation,
        long ConnectionOrdinal,
        uint AcknowledgmentNumber,
        long CaptureOrdinal,
        long Version);
}

internal readonly record struct LatestTcpAcknowledgment(
    TcpConnection Connection,
    long Generation,
    long ConnectionOrdinal,
    uint AcknowledgmentNumber,
    long CaptureOrdinal,
    long Version);
