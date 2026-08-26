namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public static class CooldownStartObservationDetail
{
    public static long Encode(int mode, int? availableCountAfterControl)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mode);
        if (availableCountAfterControl is int availableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(availableCount);
        }

        var encodedAvailableCount = availableCountAfterControl is int count
            ? (ulong)count + 1
            : 0;
        return unchecked((long)((encodedAvailableCount << 32) | (uint)mode));
    }

    public static bool TryDecode(long detailRaw, out int mode, out int? availableCountAfterControl)
    {
        var encoded = unchecked((ulong)detailRaw);
        var rawMode = encoded & uint.MaxValue;
        var rawAvailableCount = encoded >> 32;
        if (rawMode > int.MaxValue || rawAvailableCount > (ulong)int.MaxValue + 1)
        {
            mode = 0;
            availableCountAfterControl = null;
            return false;
        }

        mode = (int)rawMode;
        availableCountAfterControl = rawAvailableCount == 0
            ? null
            : (int)(rawAvailableCount - 1);
        return true;
    }
}
