using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal sealed class ProtocolRoundTripEstimator
{
    private const double Alpha = 0.1;
    private const int MaxPendingSamples = 128;
    private static readonly long SampleExpiryTicks = Stopwatch.Frequency * 120 / 1000;

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

    public void TrackOutboundFrame(int frameLength, long timestamp)
    {
        if (!IsCandidateOutboundFrameLength(frameLength))
        {
            return;
        }

        EvictExpired(timestamp);
        if (_pendingSamples.Count >= MaxPendingSamples)
        {
            _pendingSamples.Dequeue();
        }

        _pendingSamples.Enqueue(new PendingSample(timestamp, frameLength));
    }

    public bool TryResolveInboundEvent(string eventName, long timestamp, out double smoothedMilliseconds)
    {
        if (!IsCandidateInboundEvent(eventName))
        {
            smoothedMilliseconds = 0;
            return false;
        }

        EvictExpired(timestamp);
        if (_pendingSamples.Count == 0)
        {
            smoothedMilliseconds = 0;
            return false;
        }

        var sample = _pendingSamples.Dequeue();
        var elapsed = Stopwatch.GetElapsedTime(sample.Timestamp, timestamp).TotalMilliseconds;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        _smoothedMilliseconds = _smoothedMilliseconds <= 0
            ? elapsed
            : (_smoothedMilliseconds * (1.0 - Alpha)) + (elapsed * Alpha);
        Volatile.Write(ref _currentMilliseconds, _smoothedMilliseconds);
        smoothedMilliseconds = _smoothedMilliseconds;
        return true;
    }

    internal static bool IsCandidateOutboundFrameLength(int frameLength)
        => frameLength is 11 or 29 or 30 or 31 or 40 or 41 or 42;

    internal static bool IsCandidateInboundEvent(string eventName)
        => eventName is "state-1d37" or "remain-hp" or "compact-value" or "compact-0238" or "compact-0638" or "aux-2a38" or "aux-2c38";

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

    private readonly record struct PendingSample(long Timestamp, int FrameLength);
}
