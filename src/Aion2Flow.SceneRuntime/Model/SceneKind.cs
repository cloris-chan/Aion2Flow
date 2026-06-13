namespace Cloris.Aion2Flow.SceneRuntime.Model;

public enum SceneKind : byte
{
    Standard,
    Boss
}

public enum BossSceneState : byte
{
    Waiting,
    Recording,
    Frozen
}
