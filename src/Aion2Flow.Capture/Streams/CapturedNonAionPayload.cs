namespace Cloris.Aion2Flow.Capture.Streams;

internal static class CapturedNonAionPayload
{
    private const int TlsHeaderLength = 5;
    private const int MaxTlsCiphertextLength = 18_432;

    public static bool TryReadSkippableLength(ReadOnlySpan<byte> bytes, out int recordLength)
    {
        recordLength = 0;
        if (!TryReadTlsLength(bytes, out var tlsLength))
        {
            return false;
        }

        recordLength = tlsLength;
        return true;
    }

    public static bool IsPotentialSkippablePrefix(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or >= TlsHeaderLength)
        {
            return false;
        }

        return IsTlsContentType(bytes[0]) &&
               (bytes.Length < 2 || bytes[1] == 0x03) &&
               (bytes.Length < 3 || bytes[2] is >= 0x01 and <= 0x04);
    }

    private static bool TryReadTlsLength(ReadOnlySpan<byte> bytes, out int recordLength)
    {
        recordLength = 0;
        if (bytes.Length < TlsHeaderLength)
        {
            return false;
        }

        if (!IsTlsContentType(bytes[0]) || bytes[1] != 0x03 || bytes[2] is < 0x01 or > 0x04)
        {
            return false;
        }

        var payloadLength = (bytes[3] << 8) | bytes[4];
        if (payloadLength > MaxTlsCiphertextLength)
        {
            return false;
        }

        recordLength = TlsHeaderLength + payloadLength;
        return true;
    }

    private static bool IsTlsContentType(byte value)
        => value is 0x14 or 0x15 or 0x16 or 0x17;
}
