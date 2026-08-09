using System.Buffers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal delegate void TcpReassembledChunkHandler<TState>(uint sequenceNumber, ReadOnlySpan<byte> chunk, CapturedPacketTimestamp timestamp, ref TState state);

internal sealed class TcpStreamReassembler : IDisposable
{
    private readonly SortedDictionary<uint, PendingSegment> _pending = [];

    private bool _hasExpectedSequence;
    private uint _nextExpectedSequence;
    private int _pendingBytes;
    private CapturedPacketTimestamp _lastEmittedTimestamp;
    private bool _hasEmittedTimestamp;

    public void StartAt(uint nextExpectedSequence)
    {
        Reset();
        _hasExpectedSequence = true;
        _nextExpectedSequence = nextExpectedSequence;
    }

    public bool TryGetAcknowledgedGap(uint acknowledgmentNumber, out TcpReassemblyGap gap)
    {
        if (!_hasExpectedSequence ||
            !TryGetNearestFuturePending(out var resumeSequence, out _) ||
            !TcpSequence.IsAtOrAfter(acknowledgmentNumber, resumeSequence))
        {
            gap = default;
            return false;
        }

        var byteCount = TcpSequence.ForwardDistance(_nextExpectedSequence, resumeSequence);
        gap = new TcpReassemblyGap(
            _nextExpectedSequence,
            resumeSequence,
            acknowledgmentNumber,
            byteCount,
            _pending.Count,
            _pendingBytes);
        return true;
    }

    public void SkipGapAndDrain<TState>(
        in TcpReassemblyGap gap,
        ref TState state,
        TcpReassembledChunkHandler<TState> handler)
    {
        if (!_hasExpectedSequence ||
            _nextExpectedSequence != gap.ExpectedSequence ||
            !TryGetNearestFuturePending(out var resumeSequence, out _) ||
            resumeSequence != gap.ResumeSequence)
        {
            return;
        }

        _nextExpectedSequence = resumeSequence;
        DrainPending(ref state, handler);
    }

    public void Feed<TState>(uint sequenceNumber, ReadOnlySpan<byte> payload, CapturedPacketTimestamp timestamp, ref TState state, TcpReassembledChunkHandler<TState> handler)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        if (!_hasExpectedSequence)
        {
            _hasExpectedSequence = true;
            _nextExpectedSequence = sequenceNumber;
        }

        if (sequenceNumber == _nextExpectedSequence)
        {
            Emit(sequenceNumber, payload, timestamp, ref state, handler);
            DrainPending(ref state, handler);
            return;
        }

        if (TcpSequence.IsBefore(sequenceNumber, _nextExpectedSequence))
        {
            var overlap = (int)(_nextExpectedSequence - sequenceNumber);
            if (overlap >= payload.Length)
            {
                return;
            }

            Emit(_nextExpectedSequence, payload[overlap..], timestamp, ref state, handler);
            DrainPending(ref state, handler);
            return;
        }

        BufferPending(sequenceNumber, payload, timestamp);
    }

    public void Reset()
    {
        foreach (var segment in _pending.Values)
        {
            segment.Dispose();
        }

        _pending.Clear();
        _hasExpectedSequence = false;
        _nextExpectedSequence = 0;
        _pendingBytes = 0;
        _lastEmittedTimestamp = default;
        _hasEmittedTimestamp = false;
    }

    public void Dispose()
    {
        Reset();
    }

    private void Emit<TState>(uint sequenceNumber, ReadOnlySpan<byte> payload, CapturedPacketTimestamp timestamp, ref TState state, TcpReassembledChunkHandler<TState> handler)
    {
        _nextExpectedSequence = sequenceNumber + (uint)payload.Length;
        timestamp = ResolveDeliveryTimestamp(timestamp);
        handler(sequenceNumber, payload, timestamp, ref state);
    }

    private void DrainPending<TState>(ref TState state, TcpReassembledChunkHandler<TState> handler)
    {
        while (TryTakeNextPending(out var sequenceNumber, out var nextChunk, out var offset))
        {
            try
            {
                Emit(sequenceNumber, nextChunk.AsSpan()[offset..], nextChunk.Timestamp, ref state, handler);
            }
            finally
            {
                nextChunk.Dispose();
            }
        }
    }

    private bool TryTakeNextPending(out uint sequenceNumber, out PendingSegment chunk, out int offset)
    {
        while (_pending.Count != 0)
        {
            var foundUsable = false;
            var foundConsumed = false;
            var selectedSequenceNumber = 0u;
            var selectedChunk = default(PendingSegment);
            var selectedOffset = 0;
            var selectedRemainingLength = 0;
            var consumedSequenceNumber = 0u;
            var consumedChunk = default(PendingSegment);

            foreach (var (pendingSequenceNumber, pendingChunk) in _pending)
            {
                if (pendingSequenceNumber == _nextExpectedSequence)
                {
                    foundUsable = true;
                    selectedSequenceNumber = pendingSequenceNumber;
                    selectedChunk = pendingChunk;
                    selectedOffset = 0;
                    break;
                }

                if (!TcpSequence.IsBefore(pendingSequenceNumber, _nextExpectedSequence))
                {
                    continue;
                }

                var overlap = unchecked(_nextExpectedSequence - pendingSequenceNumber);
                if (overlap >= (uint)pendingChunk.Length)
                {
                    if (!foundConsumed)
                    {
                        foundConsumed = true;
                        consumedSequenceNumber = pendingSequenceNumber;
                        consumedChunk = pendingChunk;
                    }

                    continue;
                }

                var remainingLength = pendingChunk.Length - (int)overlap;
                if (!foundUsable || remainingLength > selectedRemainingLength)
                {
                    foundUsable = true;
                    selectedSequenceNumber = pendingSequenceNumber;
                    selectedChunk = pendingChunk;
                    selectedOffset = (int)overlap;
                    selectedRemainingLength = remainingLength;
                }
            }

            if (foundUsable)
            {
                _pending.Remove(selectedSequenceNumber);
                _pendingBytes -= selectedChunk.Length;
                sequenceNumber = selectedOffset == 0
                    ? selectedSequenceNumber
                    : _nextExpectedSequence;
                chunk = selectedChunk;
                offset = selectedOffset;
                return true;
            }

            if (foundConsumed)
            {
                _pending.Remove(consumedSequenceNumber);
                _pendingBytes -= consumedChunk.Length;
                consumedChunk.Dispose();
                continue;
            }

            break;
        }

        sequenceNumber = 0;
        chunk = default;
        offset = 0;
        return false;
    }

    private void BufferPending(uint sequenceNumber, ReadOnlySpan<byte> payload, CapturedPacketTimestamp timestamp)
    {
        if (_pending.TryGetValue(sequenceNumber, out var existing))
        {
            if (existing.Length >= payload.Length)
            {
                return;
            }

            existing.Dispose();
            _pendingBytes -= existing.Length;
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        _pending[sequenceNumber] = new PendingSegment(owner, payload.Length, timestamp);
        _pendingBytes += payload.Length;

        while (_pending.Count > CaptureBufferLimits.ReassemblyPendingSegmentLimit || _pendingBytes > CaptureBufferLimits.ReassemblyPendingByteLimit)
        {
            DropFarthestPending();
        }
    }

    private void DropFarthestPending()
    {
        if (!TryGetFarthestPending(out var sequenceNumber, out var segment))
        {
            return;
        }

        _pending.Remove(sequenceNumber);
        _pendingBytes -= segment.Length;
        segment.Dispose();
    }

    private bool TryGetNearestFuturePending(out uint sequenceNumber, out PendingSegment segment)
    {
        var found = false;
        var nearestDistance = uint.MaxValue;
        sequenceNumber = 0;
        segment = default;
        foreach (var entry in _pending)
        {
            var distance = TcpSequence.ForwardDistance(_nextExpectedSequence, entry.Key);
            if (distance == 0 || distance >= 0x80000000u || distance >= nearestDistance)
            {
                continue;
            }

            found = true;
            nearestDistance = distance;
            sequenceNumber = entry.Key;
            segment = entry.Value;
        }

        return found;
    }

    private bool TryGetFarthestPending(out uint sequenceNumber, out PendingSegment segment)
    {
        var found = false;
        var farthestDistance = 0u;
        sequenceNumber = 0;
        segment = default;
        foreach (var entry in _pending)
        {
            var distance = TcpSequence.ForwardDistance(_nextExpectedSequence, entry.Key);
            if (distance == 0 || distance >= 0x80000000u ||
                (found && distance <= farthestDistance))
            {
                continue;
            }

            found = true;
            farthestDistance = distance;
            sequenceNumber = entry.Key;
            segment = entry.Value;
        }

        return found;
    }

    private CapturedPacketTimestamp ResolveDeliveryTimestamp(CapturedPacketTimestamp timestamp)
    {
        if (!_hasEmittedTimestamp)
        {
            _hasEmittedTimestamp = true;
            _lastEmittedTimestamp = timestamp;
            return timestamp;
        }

        var unixMilliseconds = Math.Max(timestamp.UnixMilliseconds, _lastEmittedTimestamp.UnixMilliseconds);
        var monotonicTimestamp = timestamp.MonotonicTimestamp == 0
            ? 0
            : Math.Max(timestamp.MonotonicTimestamp, _lastEmittedTimestamp.MonotonicTimestamp);
        _lastEmittedTimestamp = new CapturedPacketTimestamp(unixMilliseconds, monotonicTimestamp);
        return _lastEmittedTimestamp;
    }

    private readonly struct PendingSegment(IMemoryOwner<byte>? owner, int length, CapturedPacketTimestamp timestamp)
    {
        private readonly IMemoryOwner<byte>? _owner = owner;

        public int Length { get; } = length;
        public CapturedPacketTimestamp Timestamp { get; } = timestamp;

        public ReadOnlySpan<byte> AsSpan() =>
            _owner is null || Length == 0
                ? ReadOnlySpan<byte>.Empty
                : _owner.Memory.Span[..Length];

        public void Dispose()
        {
            _owner?.Dispose();
        }
    }
}

internal readonly record struct TcpReassemblyGap(
    uint ExpectedSequence,
    uint ResumeSequence,
    uint AcknowledgmentNumber,
    uint ByteCount,
    int PendingSegmentCount,
    int PendingByteCount);

internal static class TcpSequence
{
    public static bool IsBefore(uint left, uint right) => unchecked((int)(left - right)) < 0;

    public static bool IsAtOrAfter(uint left, uint right) => !IsBefore(left, right);

    public static uint ForwardDistance(uint start, uint end) => unchecked(end - start);
}
