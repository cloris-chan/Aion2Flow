namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneRuntimeCheckpoint
{
    internal SceneRuntimeCheckpoint(
        Guid encounterId,
        DateTimeOffset sceneStarted,
        TimelineAnchor anchor,
        SceneRuntimeStateSnapshot state)
    {
        EncounterId = encounterId;
        SceneStarted = sceneStarted;
        Anchor = anchor;
        State = state;
    }

    public Guid EncounterId { get; }
    public DateTimeOffset SceneStarted { get; }
    public TimelineAnchor Anchor { get; }
    public long CapturedAtMilliseconds => Anchor.CapturedAtMilliseconds;
    internal SceneRuntimeStateSnapshot State { get; }

    public SceneRuntimeCheckpoint DeepClone() =>
        new(
            EncounterId,
            SceneStarted,
            Anchor,
            State.DeepClone());
}
