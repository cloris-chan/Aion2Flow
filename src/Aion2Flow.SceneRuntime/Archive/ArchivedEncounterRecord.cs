using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class ArchivedEncounterRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EncounterId { get; init; }
    public DateTimeOffset ArchivedAt { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public bool IsAutomatic { get; init; }
    public SceneCombatSnapshot Snapshot { get; init; } = new();
    public required SceneArchivePayload ScenePayload { get; init; }
}
