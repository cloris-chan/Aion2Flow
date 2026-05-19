using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketTransportCodec
{
    public static ReadOnlySpan<byte> Pattern => [0x06, 0x00, 0x36];

    public static bool TryWriteVarInt(int value, Span<byte> destination, out int written)
    {
        written = 0;
        var num = value;
        while ((uint)num > 0x7fu)
        {
            if (written >= destination.Length) return false;
            destination[written++] = (byte)((num & 0x7f) | 0x80);
            num >>= 7;
        }

        if (written >= destination.Length) return false;
        destination[written++] = (byte)num;
        return true;
    }

    public static bool TryReadVarInt(ReadOnlySpan<byte> bytes, int offset, out PacketVarIntReadResult result)
    {
        var value = 0;
        var shift = 0;
        var count = 0;

        while (true)
        {
            if (offset + count >= bytes.Length)
            {
                result = default;
                return false;
            }

            var byteVal = bytes[offset + count] & 0xff;
            count++;

            value |= (byteVal & 0x7f) << shift;

            if ((byteVal & 0x80) == 0)
            {
                result = new PacketVarIntReadResult(value, count);
                return true;
            }

            shift += 7;
            if (shift >= 32 || count > 5)
            {
                result = default;
                return false;
            }
        }
    }

    public static bool TryReadTransportLength(ReadOnlySpan<byte> bytes, int offset, out int packetLength)
    {
        packetLength = 0;
        if (!TryReadVarInt(bytes, offset, out var result))
        {
            return false;
        }

        var totalLength = result.Value + result.ByteCount - 4;
        if (totalLength <= 0)
        {
            return false;
        }

        packetLength = totalLength;
        return true;
    }
}
