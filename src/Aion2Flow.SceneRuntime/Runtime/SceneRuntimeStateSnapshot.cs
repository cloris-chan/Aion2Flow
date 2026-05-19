using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public readonly record struct TimelineAnchor(
    long LastObservationOrdinal,
    long LastBatchOrdinal,
    long CapturedAtMilliseconds,
    TimelineStamp LastStamp)
{
    public JournalCursor CreateReplayCursor(ObservedEventJournal journal) =>
        journal.CreateCursor(LastObservationOrdinal + 1);
}

internal sealed class SceneRuntimeStateSnapshot(
    EntityStoreStateSnapshot entities,
    SceneBoundaryStoreStateSnapshot boundary,
    RuntimeMetadataRegistryStateSnapshot metadata,
    CombatStoreStateSnapshot combat,
    DomainProjectionStateSnapshot projection)
{
    public EntityStoreStateSnapshot Entities { get; } = entities;
    public SceneBoundaryStoreStateSnapshot Boundary { get; } = boundary;
    public RuntimeMetadataRegistryStateSnapshot Metadata { get; } = metadata;
    public CombatStoreStateSnapshot Combat { get; } = combat;
    public DomainProjectionStateSnapshot Projection { get; } = projection;

    public SceneRuntimeStateSnapshot DeepClone() =>
        new(
            Entities.DeepClone(),
            Boundary.DeepClone(),
            Metadata.DeepClone(),
            Combat.DeepClone(),
            Projection.DeepClone());
}
