namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class ArchivedEncounterRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset ArchivedAt { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public bool IsAutomatic { get; init; }
    public required SceneArchivePayload ScenePayload { get; init; }
}
