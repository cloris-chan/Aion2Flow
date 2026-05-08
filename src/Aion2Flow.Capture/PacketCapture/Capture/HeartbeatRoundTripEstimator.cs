using System.Diagnostics;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal sealed class HeartbeatRoundTripEstimator
{
    internal const int HeartbeatPayloadLength = 11;
    internal const byte HeartbeatLeadByte = 0x0E;
    internal const int HeartbeatReplyPayloadLength = 3;
    internal const double SmoothingFactor = 0.1;

    private static readonly byte[] HeartbeatReplyPayload = [0x06, 0x00, 0x36];
    private const double DampenedFactor = SmoothingFactor * 0.2;
    private const double OutlierThreshold = 0.3;
    private const int WarmUpCount = 5;

    private long _pendingTimestamp;
    private double _bestCandidateMs = -1.0;
    private double _ema = -1.0;
    private int _resolvedCount;
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
        _pendingTimestamp = 0;
        _bestCandidateMs = -1.0;
        _ema = -1.0;
        _resolvedCount = 0;
        Volatile.Write(ref _currentMilliseconds, -1.0);
    }

    public bool TryTrackOutbound(ReadOnlySpan<byte> payload, long timestamp)
    {
        if (!IsHeartbeatPayload(payload))
        {
            return false;
        }

        if (_bestCandidateMs >= 0)
        {
            CommitSample(_bestCandidateMs);
        }

        _pendingTimestamp = timestamp;
        _bestCandidateMs = -1.0;
        return true;
    }

    public bool TryResolveInbound(ReadOnlySpan<byte> payload, long timestamp, out double smoothedMilliseconds)
    {
        if (_pendingTimestamp == 0 || !EndsWithHeartbeatReply(payload))
        {
            smoothedMilliseconds = 0;
            return false;
        }

        var elapsed = Stopwatch.GetElapsedTime(_pendingTimestamp, timestamp).TotalMilliseconds;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        if (elapsed <= _bestCandidateMs)
        {
            smoothedMilliseconds = 0;
            return false;
        }

        _bestCandidateMs = elapsed;

        var provisional = _ema >= 0 ? _ema : elapsed;
        Volatile.Write(ref _currentMilliseconds, provisional);
        smoothedMilliseconds = provisional;
        return true;
    }

    private void CommitSample(double elapsed)
    {
        _resolvedCount++;

        if (_ema < 0)
        {
            _ema = elapsed;
        }
        else if (_resolvedCount <= WarmUpCount)
        {
            _ema = Math.Max(_ema, elapsed);
        }
        else
        {
            var alpha = elapsed >= _ema * OutlierThreshold ? SmoothingFactor : DampenedFactor;
            _ema = (alpha * elapsed) + ((1.0 - alpha) * _ema);
        }

        Volatile.Write(ref _currentMilliseconds, _ema);
    }

    internal static bool IsHeartbeatPayload(ReadOnlySpan<byte> payload)
        => payload.Length == HeartbeatPayloadLength && payload[0] == HeartbeatLeadByte;

    internal static bool EndsWithHeartbeatReply(ReadOnlySpan<byte> payload)
        => payload.Length >= HeartbeatReplyPayloadLength
           && payload[^HeartbeatReplyPayloadLength..].SequenceEqual(HeartbeatReplyPayload);
}