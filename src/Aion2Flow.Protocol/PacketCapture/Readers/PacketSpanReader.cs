namespace Cloris.Aion2Flow.PacketCapture.Readers;

public ref struct PacketSpanReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;
    public int Offset { get; private set; }

    public readonly int Remaining => _buffer.Length - Offset;

    public readonly ReadOnlySpan<byte> RemainingSpan => _buffer[Offset..];

    public bool TryAdvance(int count)
    {
        if ((uint)count > (uint)Remaining) return false;
        Offset += count;
        return true;
    }

    public bool TryReadByte(out byte value)
    {
        if (Remaining <= 0)
        {
            value = 0;
            return false;
        }

        value = _buffer[Offset++];
        return true;
    }

    public bool TryReadUInt16Le(out int value)
    {
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }

        var b0 = _buffer[Offset];
        var b1 = _buffer[Offset + 1];
        Offset += 2;
        value = (b0 & 0xff) | ((b1 & 0xff) << 8);
        return true;
    }

    public bool TryReadUInt32Le(out int value)
    {
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }

        value = (_buffer[Offset] & 0xff)
            | ((_buffer[Offset + 1] & 0xff) << 8)
            | ((_buffer[Offset + 2] & 0xff) << 16)
            | ((_buffer[Offset + 3] & 0xff) << 24);
        Offset += 4;
        return true;
    }

    public bool TryReadVarInt(out int value)
    {
        value = 0;
        var shift = 0;
        var count = 0;

        while (true)
        {
            if (Remaining <= 0)
                return false;

            var byteVal = _buffer[Offset++] & 0xff;
            count++;

            value |= (byteVal & 0x7f) << shift;

            if ((byteVal & 0x80) == 0)
                return true;

            shift += 7;
            if (shift >= 32 || count > 5)
                return false;
        }
    }
}
