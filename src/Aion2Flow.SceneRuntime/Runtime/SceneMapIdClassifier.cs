namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

internal static class SceneMapIdClassifier
{
    public static bool IsPacketMapEventId(uint value) => value != 0;

    public static bool IsAmbiguousSceneStateMapId(uint value)
        => value is (>= 20 and < 2000)
            or (>= 100000 and < 200000)
            or (>= 200000 and < 300000)
            or (>= 300000 and < 400000)
            or (>= 500000 and < 700000)
            or (>= 800000 and < 900000);
}
