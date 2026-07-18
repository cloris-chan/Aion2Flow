using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public interface ICombatDetailEventWriter
{
    void Clear();
    void AddMetric(in CombatMetricDetailEvent detailEvent);
    void AddMechanic(in CombatMechanicDetailEvent detailEvent);
    void AddResource(in CombatResourceDetailEvent detailEvent);
}

public readonly record struct CombatDetailUpdateResult
{
    public int CombatantId { get; init; }
    public long Revision { get; init; }
    public bool IsFullSnapshot { get; init; }
    public bool HasChanges { get; init; }
    public int AddedMetricEventCount { get; init; }
    public int AddedMechanicEventCount { get; init; }
    public int AddedResourceEventCount { get; init; }
    public CombatantSummary? Combatant { get; init; }

    public static CombatDetailUpdateResult None(int combatantId, long revision, CombatantSummary? combatant) => new()
    {
        CombatantId = combatantId,
        Revision = revision,
        Combatant = combatant
    };
}

internal readonly record struct CombatDetailContextKey(int CombatantId, Guid EncounterId, long EncounterStartTime, int TrackingTargetId, int TargetObservationId)
{
    public static CombatDetailContextKey From(SceneCombatSnapshot snapshot, int combatantId) => new()
    {
        CombatantId = combatantId,
        EncounterId = snapshot.EncounterId,
        EncounterStartTime = snapshot.EncounterStartTime,
        TrackingTargetId = snapshot.Encounter.TrackingTargetId,
        TargetObservationId = snapshot.TargetObservation?.InstanceId ?? 0
    };
}

internal enum CombatDetailProjectionScope
{
    EncounterWindow,
    CurrentFrame
}

internal readonly record struct CombatDetailWriteResult(
    int MetricEventCount,
    int MechanicEventCount,
    int ResourceEventCount,
    long Revision)
{
    public int TotalEventCount => MetricEventCount + MechanicEventCount + ResourceEventCount;
}

public readonly record struct CombatDetailEventSet(
    IReadOnlyList<CombatMetricDetailEvent> MetricEvents,
    IReadOnlyList<CombatMechanicDetailEvent> MechanicEvents,
    IReadOnlyList<CombatResourceDetailEvent> ResourceEvents)
{
    public static CombatDetailEventSet Empty { get; } = new([], [], []);
}
