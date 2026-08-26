namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public static class CooldownChargeObservationDetail
{
    public static long Encode(byte state, int availableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);
        return ((long)availableCount << 8) | state;
    }

    public static bool TryDecode(long detailRaw, out byte state, out int availableCount)
    {
        state = 0;
        availableCount = 0;
        if (detailRaw < 0 || detailRaw > ((long)int.MaxValue << 8) + byte.MaxValue)
            return false;

        state = (byte)detailRaw;
        availableCount = (int)(detailRaw >> 8);
        return true;
    }
}
