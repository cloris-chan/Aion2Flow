namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class SceneCombatSnapshot
{
    public Dictionary<int, SceneCombatantMetrics> Combatants { get; } = [];
    public Guid EncounterId { get; set; } = Guid.NewGuid();
    public string TargetName { get; set; } = string.Empty;
    public long EncounterTime { get; set; }
    public long EncounterStartTime { get; set; }
    public long EncounterEndTime { get; set; }
    public NpcRuntimeObservation? TargetObservation { get; set; }
    public EncounterSummary Encounter { get; set; } = new();
    public uint MapId { get; set; }
    public uint MapInstanceId { get; set; }

    public SceneCombatSnapshot DeepClone()
    {
        var clone = new SceneCombatSnapshot
        {
            EncounterId = EncounterId,
            TargetName = TargetName,
            EncounterTime = EncounterTime,
            EncounterStartTime = EncounterStartTime,
            EncounterEndTime = EncounterEndTime,
            TargetObservation = TargetObservation?.DeepClone(),
            Encounter = Encounter.DeepClone(),
            MapId = MapId,
            MapInstanceId = MapInstanceId
        };

        foreach (var (id, combatant) in Combatants)
        {
            clone.Combatants[id] = combatant.DeepClone();
        }

        return clone;
    }
}
