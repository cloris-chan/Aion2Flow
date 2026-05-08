using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal sealed class TcpRoundTripEstimator
{
    private const double Alpha = 0.1;
    private const int MaxPendingSamples = 64;
    private static readonly long SampleExpiryTicks = Stopwatch.Frequency / 2;

    private readonly Queue<PendingSample> _pendingSamples = [];

    private double _smoothedMilliseconds;
    private double _currentMilliseconds = -1.0;

    public double? CurrentMilliseconds
    {
        get
        {
            var value = Volatile.Read(ref _currentMilliseconds);
            return value >= 0 ? value : null;
        }
    }

    public void Clear()
    {
        _pendingSamples.Clear();
        _smoothedMilliseconds = 0;
        Volatile.Write(ref _currentMilliseconds, -1.0);
    }

    public void TrackOutbound(uint sequenceNumber, int payloadLength, long timestamp)
    {
        if (payloadLength <= 0)
        {
            return;
        }

        EvictExpired(timestamp);
        if (_pendingSamples.Count >= MaxPendingSamples)
        {
            _pendingSamples.Dequeue();
        }

        _pendingSamples.Enqueue(new PendingSample(timestamp, sequenceNumber + (uint)payloadLength));
    }

    public bool TryResolveInbound(uint acknowledgmentNumber, long timestamp, out double smoothedMilliseconds)
    {
        EvictExpired(timestamp);

        while (_pendingSamples.Count > 0)
        {
            var sample = _pendingSamples.Peek();
            if (acknowledgmentNumber == sample.ExpectedAcknowledgment)
            {
                _pendingSamples.Dequeue();
                CommitSample(sample.Timestamp, timestamp);
                smoothedMilliseconds = _smoothedMilliseconds;
                return true;
            }

            if (SequenceLessThan(sample.ExpectedAcknowledgment, acknowledgmentNumber))
            {
                _pendingSamples.Dequeue();
                continue;
            }

            break;
        }

        smoothedMilliseconds = 0;
        return false;
    }

    private void CommitSample(long sentTimestamp, long receivedTimestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(sentTimestamp, receivedTimestamp).TotalMilliseconds;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        _smoothedMilliseconds = _smoothedMilliseconds <= 0
            ? elapsed
            : (_smoothedMilliseconds * (1.0 - Alpha)) + (elapsed * Alpha);
        Volatile.Write(ref _currentMilliseconds, _smoothedMilliseconds);
    }

    private void EvictExpired(long timestamp)
    {
        while (_pendingSamples.Count > 0)
        {
            var sample = _pendingSamples.Peek();
            if (timestamp - sample.Timestamp <= SampleExpiryTicks)
            {
                break;
            }

            _pendingSamples.Dequeue();
        }
    }

    internal static bool SequenceLessThan(uint left, uint right)
    {
        return unchecked((int)(left - right)) < 0;
    }

    private readonly record struct PendingSample(long Timestamp, uint ExpectedAcknowledgment);
}
