namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class Packet0438Layout
{
    private const int DetailLayoutMask = 0x0f;
    private const int DetailBaseFlag = 0x04;
    private const int DetailExtraFourFlag = 0x01;
    private const int DetailExtraTwoFlag = 0x02;
    private const int DetailBaseLength = 8;
    private const int MultiHitFlag = 0x20;

    public static int GetDetailLayoutKey(int layoutTag) => layoutTag & DetailLayoutMask;

    public static bool HasMultiHitData(int layoutTag) => (layoutTag & MultiHitFlag) != 0;

    public static bool TryGetDetailLength(int layoutTag, out int detailLength)
    {
        var key = GetDetailLayoutKey(layoutTag);
        if ((key & DetailBaseFlag) == 0)
        {
            detailLength = 0;
            return false;
        }

        detailLength = DetailBaseLength
            + ((key & DetailExtraFourFlag) != 0 ? 4 : 0)
            + ((key & DetailExtraTwoFlag) != 0 ? 2 : 0);
        return true;
    }
}
