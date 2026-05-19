using System.Buffers;
using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.Capture.Streams;

public sealed class PacketTailBuffer(int capacity) : IDisposable
{
    private const int InitialBufferSize = 8 * 1024;
    private readonly int _capacity = ThrowIfNegativeOrZero(capacity);
    private IMemoryOwner<byte>? _bufferOwner;
    private int _bufferCapacity;

    public int Capacity => _capacity;

    public int Offset { get; private set; }

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Data => Length == 0 || _bufferOwner is null ? ReadOnlySpan<byte>.Empty : _bufferOwner.Memory.Span.Slice(Offset, Length);

    public void Dispose()
    {
        _bufferOwner?.Dispose();
        _bufferOwner = null;
        _bufferCapacity = 0;
        Clear();
    }

    public void Clear()
    {
        Offset = 0;
        Length = 0;
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (data.Length >= Capacity)
        {
            EnsureBuffer(Capacity);
            data[^Capacity..].CopyTo(Buffer);
            Offset = 0;
            Length = Capacity;
            return;
        }

        var requiredLength = Length + data.Length;
        if (requiredLength > Capacity)
        {
            var overflow = requiredLength - Capacity;
            Offset += overflow;
            Length -= overflow;
            requiredLength = Capacity;
        }

        EnsureBuffer(requiredLength);
        var buffer = Buffer;
        if (Offset + Length + data.Length > _bufferCapacity)
        {
            Data.CopyTo(buffer);
            Offset = 0;
        }

        data.CopyTo(buffer[(Offset + Length)..]);
        Length += data.Length;
    }

    public void Consume(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length);

        if (count == 0)
            return;

        Offset += count;
        Length -= count;

        if (Length == 0)
        {
            Offset = 0;
        }
    }

    private Span<byte> Buffer => _bufferOwner!.Memory.Span[.._bufferCapacity];

    private void EnsureBuffer(int requiredLength)
    {
        if (_bufferOwner is not null && _bufferCapacity >= requiredLength)
            return;

        var newCapacity = _bufferCapacity == 0 ? Math.Min(Capacity, Math.Max(InitialBufferSize, requiredLength)) : _bufferCapacity;
        while (newCapacity < requiredLength)
            newCapacity = Math.Min(Capacity, checked(newCapacity * 2));

        var previous = Data;
        var nextOwner = MemoryPool<byte>.Shared.Rent(newCapacity);
        previous.CopyTo(nextOwner.Memory.Span);
        _bufferOwner?.Dispose();
        _bufferOwner = nextOwner;
        _bufferCapacity = newCapacity;
        Offset = 0;
    }

    private static int ThrowIfNegativeOrZero(int value, [CallerArgumentExpression(nameof(value))] string name = default!)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
        return value;
    }
}
