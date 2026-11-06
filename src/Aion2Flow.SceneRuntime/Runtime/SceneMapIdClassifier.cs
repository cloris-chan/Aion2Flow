namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

internal static class SceneMapIdClassifier
{
    public static bool IsKnownMapId(uint value, SceneClientContext clientContext) =>
        clientContext.Resources.Maps.ContainsKey(value);
}
