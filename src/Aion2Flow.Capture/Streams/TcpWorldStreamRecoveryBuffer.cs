using System.Buffers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal delegate bool TcpRecoveredStreamSegmentHandler(
    uint sequenceNumber,
    ReadOnlySpan<byte> payload,
    CapturedPacketTimestamp timestamp);

internal sealed class TcpWorldStreamRecoveryBuffer : IDisposable
{
    private readonly TcpWorldStreamClassifier _classifier = new(allowMidstreamRecovery: true);
    private readonly List<Segment> _segments = [];
    private int _bufferedBytes;

    public TcpWorldStreamRecoveryResult Append(
        uint sequenceNumber,
        ReadOnlySpan<byte> payload,
        CapturedPacketTimestamp timestamp)
    {
        if (payload.IsEmpty)
        {
            return TcpWorldStreamRecoveryResult.Pending;
        }

        if (_segments.Count >= CaptureBufferLimits.CandidateStreamSegmentLimit ||
            payload.Length > CaptureBufferLimits.CandidateStreamByteLimit - _bufferedBytes)
        {
            return TcpWorldStreamRecoveryResult.Rejected;
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        _segments.Add(new Segment(owner, sequenceNumber, payload.Length, timestamp));
        _bufferedBytes += payload.Length;

        return _classifier.Append(payload, timestamp.UnixMilliseconds) switch
        {
            TcpWorldStreamClassification.Confirmed => TcpWorldStreamRecoveryResult.Confirmed,
            TcpWorldStreamClassification.Rejected => TcpWorldStreamRecoveryResult.Rejected,
            _ => TcpWorldStreamRecoveryResult.Pending
        };
    }

    public bool Replay(TcpRecoveredStreamSegmentHandler handler)
    {
        var remainingDiscard = _classifier.ReplayStartByteOffset;
        var parsed = false;
        foreach (var segment in _segments)
        {
            if (remainingDiscard >= segment.Length)
            {
                remainingDiscard -= segment.Length;
                continue;
            }

            var offset = remainingDiscard;
            remainingDiscard = 0;
            parsed |= handler(
                unchecked(segment.SequenceNumber + (uint)offset),
                segment.Payload[offset..],
                segment.Timestamp);
        }

        return parsed;
    }

    public void Dispose()
    {
        _classifier.Dispose();
        foreach (var segment in _segments)
        {
            segment.Dispose();
        }

        _segments.Clear();
        _bufferedBytes = 0;
    }

    private readonly struct Segment(
        IMemoryOwner<byte> owner,
        uint sequenceNumber,
        int length,
        CapturedPacketTimestamp timestamp)
    {
        public uint SequenceNumber { get; } = sequenceNumber;
        public int Length { get; } = length;
        public CapturedPacketTimestamp Timestamp { get; } = timestamp;
        public ReadOnlySpan<byte> Payload => owner.Memory.Span[..Length];

        public void Dispose() => owner.Dispose();
    }
}

internal enum TcpWorldStreamRecoveryResult : byte
{
    Pending,
    Confirmed,
    Rejected
}
