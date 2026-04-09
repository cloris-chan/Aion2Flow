using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal sealed class TcpRoundTripEstimator
{
    private const double Alpha = 0.1;
    private const int MaxPendingSamples = 64;
    private static readonly long SampleExpiryTicks = Stopwatch.Frequency / 2;

    private readonly Lock _sync = new();
    private readonly Queue<PendingSample> _pendingSamples = [];

    private double? _currentMilliseconds;
    private double _smoothedMilliseconds;

    public double? CurrentMilliseconds
    {
        get
        {
            lock (_sync)
            {
                return _currentMilliseconds;
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _pendingSamples.Clear();
            _currentMilliseconds = null;
            _smoothedMilliseconds = 0;
        }
    }

    public void TrackOutbound(uint sequenceNumber, int payloadLength)
    {
        TrackOutbound(sequenceNumber, payloadLength, Stopwatch.GetTimestamp());
    }

    internal void TrackOutbound(uint sequenceNumber, int payloadLength, long timestamp)
    {
        if (payloadLength <= 0)
        {
            return;
        }

        lock (_sync)
        {
            EvictExpired(timestamp);
            if (_pendingSamples.Count >= MaxPendingSamples)
            {
                _pendingSamples.Dequeue();
            }

            _pendingSamples.Enqueue(new PendingSample(timestamp, sequenceNumber + (uint)payloadLength));
        }
    }

    public bool TryResolveInbound(uint acknowledgmentNumber, out double smoothedMilliseconds)
    {
        return TryResolveInbound(acknowledgmentNumber, Stopwatch.GetTimestamp(), out smoothedMilliseconds);
    }

    internal bool TryResolveInbound(uint acknowledgmentNumber, long timestamp, out double smoothedMilliseconds)
    {
        lock (_sync)
        {
            EvictExpired(timestamp);

            while (_pendingSamples.Count > 0)
            {
                var sample = _pendingSamples.Peek();
                if (acknowledgmentNumber == sample.ExpectedAcknowledgment)
                {
                    _pendingSamples.Dequeue();

                    var currentMilliseconds = Stopwatch.GetElapsedTime(sample.Timestamp, timestamp).TotalMilliseconds;
                    _smoothedMilliseconds = _smoothedMilliseconds <= 0
                        ? currentMilliseconds
                        : (_smoothedMilliseconds * (1.0 - Alpha)) + (currentMilliseconds * Alpha);
                    _currentMilliseconds = _smoothedMilliseconds;
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
        }

        smoothedMilliseconds = 0;
        return false;
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

    private static bool SequenceLessThan(uint left, uint right)
    {
        return unchecked((int)(left - right)) < 0;
    }

    private readonly record struct PendingSample(long Timestamp, uint ExpectedAcknowledgment);
}
