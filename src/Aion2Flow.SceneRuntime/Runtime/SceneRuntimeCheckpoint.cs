using Cloris.Aion2Flow.SceneRuntime.Identity;
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
        EntityStore entities,
        SceneBoundaryStore boundary,
        RuntimeMetadataRegistry metadataRegistry,
        CombatStore combat,
        DomainEventApplier applier)
    {
        EncounterId = encounterId;
        SceneStarted = sceneStarted;
        CapturedAtMilliseconds = capturedAtMilliseconds;
        Cursor = cursor;
        AppliedObservationOrdinal = appliedObservationOrdinal;
        AppliedBatchOrdinal = appliedBatchOrdinal;
        Entities = entities;
        Boundary = boundary;
        MetadataRegistry = metadataRegistry;
        Combat = combat;
        Applier = applier;
    }

    public Guid EncounterId { get; }
    public DateTimeOffset SceneStarted { get; }
    public long CapturedAtMilliseconds { get; }
    public JournalCursor Cursor { get; }
    public long AppliedObservationOrdinal { get; }
    public long AppliedBatchOrdinal { get; }

    internal EntityStore Entities { get; }
    internal SceneBoundaryStore Boundary { get; }
    internal RuntimeMetadataRegistry MetadataRegistry { get; }
    internal CombatStore Combat { get; }
    internal DomainEventApplier Applier { get; }

    public SceneRuntimeCheckpoint DeepClone()
    {
        var entities = Entities.DeepClone();
        var boundary = Boundary.DeepClone();
        var metadataRegistry = MetadataRegistry.DeepClone();
        var combat = Combat.DeepClone();
        var applier = Applier.DeepClone(entities, boundary, metadataRegistry, combat);
        return new SceneRuntimeCheckpoint(
            EncounterId,
            SceneStarted,
            CapturedAtMilliseconds,
            Cursor,
            AppliedObservationOrdinal,
            AppliedBatchOrdinal,
            entities,
            boundary,
            metadataRegistry,
            combat,
            applier);
    }

}
