namespace Cloris.Aion2Flow.Protocol.Combat;

public static class PacketDurationDecoder
{
    private const ulong MaximumUpperWord = 0x0000_FFFF_FFFF_FFFF;

    public static bool TryDecodeMilliseconds(ushort lowValue, ulong upperWord, out long durationMilliseconds)
    {
        if (lowValue == ushort.MaxValue || upperWord > MaximumUpperWord)
        {
            durationMilliseconds = 0;
            return false;
        }

        var packed = lowValue | (upperWord << 16);
        durationMilliseconds = packed > long.MaxValue ? long.MaxValue : (long)packed;
        return true;
    }
}
