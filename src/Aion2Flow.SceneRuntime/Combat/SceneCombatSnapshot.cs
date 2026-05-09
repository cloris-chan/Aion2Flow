namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class SceneCombatSnapshot
{
    public Dictionary<int, SceneCombatantMetrics> Combatants { get; } = [];
    public Guid EncounterId { get; set; } = Guid.NewGuid();
    public long ReadModelRevision { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public long EncounterTime { get; set; }
    public long EncounterStartTime { get; set; }
    public long EncounterEndTime { get; set; }
    public NpcRuntimeObservation? TargetObservation { get; set; }
    public EncounterSummary Encounter { get; set; } = new();
    public List<SceneBossFocusSnapshot> BossFocuses { get; } = [];
    public uint MapId { get; set; }
    public uint MapInstanceId { get; set; }

    public SceneCombatSnapshot DeepClone()
    {
        var clone = new SceneCombatSnapshot
        {
            EncounterId = EncounterId,
            ReadModelRevision = ReadModelRevision,
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

        for (var i = 0; i < BossFocuses.Count; i++)
        {
            clone.BossFocuses.Add(BossFocuses[i]);
        }

        return clone;
    }
}

public readonly record struct SceneBossFocusSnapshot
{
    public int InstanceId { get; init; }
    public string DisplayName { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public long LastObservedAtMilliseconds { get; init; }
    public bool HasHp { get; init; }
}
