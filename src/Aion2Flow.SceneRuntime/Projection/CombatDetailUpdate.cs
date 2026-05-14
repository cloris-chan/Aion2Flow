using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public interface ICombatDetailEventWriter
{
    void Clear();
    void Add(in CombatDetailEvent detailEvent);
}

public readonly record struct CombatDetailUpdateResult
{
    public int CombatantId { get; init; }
    public long Revision { get; init; }
    public bool IsFullSnapshot { get; init; }
    public bool HasChanges { get; init; }
    public int AddedEventCount { get; init; }
    public CombatantSummary? Combatant { get; init; }

    public static CombatDetailUpdateResult None(int combatantId, long revision, CombatantSummary? combatant) => new()
    {
        CombatantId = combatantId,
        Revision = revision,
        Combatant = combatant
    };
}

internal readonly record struct CombatDetailContextKey(
    int CombatantId,
    Guid EncounterId,
    long EncounterStartTime,
    int TrackingTargetId,
    int TargetObservationId)
{
    public static CombatDetailContextKey From(SceneCombatSnapshot snapshot, int combatantId) => new(
        combatantId,
        snapshot.EncounterId,
        snapshot.EncounterStartTime,
        snapshot.Encounter.TrackingTargetId,
        snapshot.TargetObservation?.InstanceId ?? 0);
}

internal readonly record struct CombatDetailWriteResult(int Count, long Revision);
