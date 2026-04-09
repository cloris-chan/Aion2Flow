using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal sealed class ProtocolRoundTripEstimator
{
    private const double Alpha = 0.1;
    private const int MaxPendingSamples = 128;
    private static readonly long SampleExpiryTicks = Stopwatch.Frequency * 120 / 1000;

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

    public void TrackOutboundFrame(int frameLength)
    {
        TrackOutboundFrame(frameLength, Stopwatch.GetTimestamp());
    }

    internal void TrackOutboundFrame(int frameLength, long timestamp)
    {
        if (!IsCandidateOutboundFrameLength(frameLength))
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

            _pendingSamples.Enqueue(new PendingSample(timestamp, frameLength));
        }
    }

    public bool TryResolveInboundEvent(string eventName, out double smoothedMilliseconds)
    {
        return TryResolveInboundEvent(eventName, Stopwatch.GetTimestamp(), out smoothedMilliseconds);
    }

    internal bool TryResolveInboundEvent(string eventName, long timestamp, out double smoothedMilliseconds)
    {
        if (!IsCandidateInboundEvent(eventName))
        {
            smoothedMilliseconds = 0;
            return false;
        }

        lock (_sync)
        {
            EvictExpired(timestamp);
            if (_pendingSamples.Count == 0)
            {
                smoothedMilliseconds = 0;
                return false;
            }

            var sample = _pendingSamples.Dequeue();
            var currentMilliseconds = Stopwatch.GetElapsedTime(sample.Timestamp, timestamp).TotalMilliseconds;
            _smoothedMilliseconds = _smoothedMilliseconds <= 0
                ? currentMilliseconds
                : (_smoothedMilliseconds * (1.0 - Alpha)) + (currentMilliseconds * Alpha);
            _currentMilliseconds = _smoothedMilliseconds;
            smoothedMilliseconds = _smoothedMilliseconds;
            return true;
        }
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
