using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneRuntimeCheckpoint
{
    internal SceneRuntimeCheckpoint(
        Guid encounterId,
        DateTimeOffset sceneStarted,
        long capturedAtMilliseconds,
        JournalCursor cursor,
        long appliedObservationOrdinal,
        long appliedBatchOrdinal,
        CombatStoreStateSnapshot combatState)
    {
        EncounterId = encounterId;
        SceneStarted = sceneStarted;
        CapturedAtMilliseconds = capturedAtMilliseconds;
        Cursor = cursor;
        AppliedObservationOrdinal = appliedObservationOrdinal;
        AppliedBatchOrdinal = appliedBatchOrdinal;
        CombatState = combatState;
    }

    public Guid EncounterId { get; }
    public DateTimeOffset SceneStarted { get; }
    public long CapturedAtMilliseconds { get; }
    public JournalCursor Cursor { get; }
    public long AppliedObservationOrdinal { get; }
    public long AppliedBatchOrdinal { get; }
    internal CombatStoreStateSnapshot CombatState { get; }

    public SceneRuntimeCheckpoint DeepClone() =>
        new(
            EncounterId,
            SceneStarted,
            CapturedAtMilliseconds,
            Cursor,
            AppliedObservationOrdinal,
            AppliedBatchOrdinal,
            CombatState.DeepClone());
}
