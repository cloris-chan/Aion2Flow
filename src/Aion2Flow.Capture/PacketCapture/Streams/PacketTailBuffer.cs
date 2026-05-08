using System.Buffers;
using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.PacketCapture.Streams;

public sealed class PacketTailBuffer(int capacity) : IDisposable
{
    private readonly int _capacity = ThrowIfNegativeOrZero(capacity);
    private readonly IMemoryOwner<byte> _bufferOwner = MemoryPool<byte>.Shared.Rent(ThrowIfNegativeOrZero(capacity));

    public int Capacity => _capacity;

    public int Offset { get; private set; }

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Data => Length == 0 ? ReadOnlySpan<byte>.Empty : _bufferOwner.Memory.Span.Slice(Offset, Length);

    public void Dispose()
    {
        _bufferOwner.Dispose();
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
            data[^Capacity..].CopyTo(_bufferOwner.Memory.Span);
            Offset = 0;
            Length = Capacity;
            return;
        }

        int freeSize = Capacity - (Offset + Length);
        var buffer = _bufferOwner.Memory.Span[..Capacity];
        if (data.Length > freeSize)
        {
            var overflow = Length + data.Length - Capacity;
            if (overflow > 0)
            {
                Offset += overflow;
                Length -= overflow;
            }

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

    private static int ThrowIfNegativeOrZero(int value, [CallerArgumentExpression(nameof(value))] string name = default!)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
        return value;
    }
}
