using System.Buffers;

namespace Cloris.Aion2Flow.PacketCapture.Streams;

internal delegate void TcpReassembledChunkHandler<TState>(uint sequenceNumber, ReadOnlySpan<byte> chunk, ref TState state);

internal sealed class TcpStreamReassembler : IDisposable
{
    private const int MaxPendingSegments = 256;

    private readonly SortedDictionary<uint, PendingSegment> _pending = [];

    private bool _hasExpectedSequence;
    private uint _nextExpectedSequence;

    public void Feed<TState>(uint sequenceNumber, ReadOnlySpan<byte> payload, ref TState state, TcpReassembledChunkHandler<TState> handler)
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
            Emit(sequenceNumber, payload, ref state, handler);
            DrainPending(ref state, handler);
            return;
        }

        if (SequenceLessThan(sequenceNumber, _nextExpectedSequence))
        {
            var overlap = (int)(_nextExpectedSequence - sequenceNumber);
            if (overlap >= payload.Length)
            {
                return;
            }

            Emit(_nextExpectedSequence, payload[overlap..], ref state, handler);
            DrainPending(ref state, handler);
            return;
        }

        BufferPending(sequenceNumber, payload);
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
    }

    public void Dispose()
    {
        Reset();
    }

    private void Emit<TState>(uint sequenceNumber, ReadOnlySpan<byte> payload, ref TState state, TcpReassembledChunkHandler<TState> handler)
    {
        _nextExpectedSequence = sequenceNumber + (uint)payload.Length;
        handler(sequenceNumber, payload, ref state);
    }

    private void DrainPending<TState>(ref TState state, TcpReassembledChunkHandler<TState> handler)
    {
        while (TryTakeNextPending(out var sequenceNumber, out var nextChunk, out var offset))
        {
            try
            {
                Emit(sequenceNumber, nextChunk.AsSpan()[offset..], ref state, handler);
            }
            finally
            {
                nextChunk.Dispose();
            }
        }
    }

    private bool TryTakeNextPending(out uint sequenceNumber, out PendingSegment chunk, out int offset)
    {
        while (TryGetFirstPending(out var pendingSequenceNumber, out chunk))
        {
            if (pendingSequenceNumber == _nextExpectedSequence)
            {
                _pending.Remove(pendingSequenceNumber);
                sequenceNumber = pendingSequenceNumber;
                offset = 0;
                return true;
            }

            if (!SequenceLessThan(pendingSequenceNumber, _nextExpectedSequence))
            {
                break;
            }

            offset = (int)(_nextExpectedSequence - pendingSequenceNumber);
            _pending.Remove(pendingSequenceNumber);
            if (offset >= chunk.Length)
            {
                chunk.Dispose();
                continue;
            }

            sequenceNumber = _nextExpectedSequence;
            return true;
        }

        sequenceNumber = 0;
        chunk = default;
        offset = 0;
        return false;
    }

    private void BufferPending(uint sequenceNumber, ReadOnlySpan<byte> payload)
    {
        if (_pending.TryGetValue(sequenceNumber, out var existing))
        {
            if (existing.Length >= payload.Length)
            {
                return;
            }

            existing.Dispose();
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        _pending[sequenceNumber] = new PendingSegment(owner, payload.Length);

        while (_pending.Count > MaxPendingSegments)
        {
            DropFirstPending();
        }
    }

    private void DropFirstPending()
    {
        if (!TryGetFirstPending(out var sequenceNumber, out var segment))
        {
            return;
        }

        _pending.Remove(sequenceNumber);
        segment.Dispose();
    }

    private bool TryGetFirstPending(out uint sequenceNumber, out PendingSegment segment)
    {
        using var enumerator = _pending.GetEnumerator();
        if (enumerator.MoveNext())
        {
            sequenceNumber = enumerator.Current.Key;
            segment = enumerator.Current.Value;
            return true;
        }

        sequenceNumber = 0;
        segment = default;
        return false;
    }

    private static bool SequenceLessThan(uint left, uint right)
    {
        return unchecked((int)(left - right)) < 0;
    }

    private readonly struct PendingSegment(IMemoryOwner<byte>? owner, int length)
    {
        private readonly IMemoryOwner<byte>? _owner = owner;

        public int Length { get; } = length;

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
