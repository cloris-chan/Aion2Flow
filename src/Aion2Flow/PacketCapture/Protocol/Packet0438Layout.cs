namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class Packet0438Layout
{
    private const int DetailLayoutMask = 0x0f;
    private const int MultiHitFlag = 0x20;

    public static int GetDetailLayoutKey(int layoutTag) => layoutTag & DetailLayoutMask;

    public static bool HasMultiHitData(int layoutTag) => (layoutTag & MultiHitFlag) != 0;

    public static bool TryGetDetailLength(int layoutTag, out int detailLength)
    {
        detailLength = GetDetailLayoutKey(layoutTag) switch
        {
            4 => 8,
            5 => 12,
            6 => 10,
            7 => 14,
            _ => 0
        };

        return detailLength > 0;
    }
}
