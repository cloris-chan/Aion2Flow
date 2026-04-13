namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class Packet0438Layout
{
    private const int SpecialsLayoutMask = 0x0f;
    private const int MultiHitFlag = 0x20;

    public static int GetSpecialsLayoutKey(int layoutTag) => layoutTag & SpecialsLayoutMask;

    public static bool HasMultiHitData(int layoutTag) => (layoutTag & MultiHitFlag) != 0;

    public static bool TryGetSpecialsLength(int layoutTag, out int specialsLength)
    {
        specialsLength = GetSpecialsLayoutKey(layoutTag) switch
        {
            4 => 8,
            5 => 12,
            6 => 10,
            7 => 14,
            _ => 0
        };

        return specialsLength > 0;
    }
}
