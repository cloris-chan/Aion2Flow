using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class SceneMapIdClassifier
{
    private static readonly Lazy<HashSet<uint>> KnownResourceMapIds = new(LoadKnownResourceMapIds);

    public static bool IsSceneStateMapId(uint value)
        => value != 0 && (KnownResourceMapIds.Value.Contains(value) || IsRuntimeMapIdRange(value));

    private static bool IsRuntimeMapIdRange(uint value)
        => value is (>= 1000 and < 2000)
            or (>= 200000 and < 300000)
            or (>= 500000 and < 700000);

    private static HashSet<uint> LoadKnownResourceMapIds()
    {
        try
        {
            return [.. ResourceDatabase.LoadMaps().Keys];
        }
        catch
        {
            return [];
        }
    }
}
