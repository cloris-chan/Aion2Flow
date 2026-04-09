using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Battle.Runtime;

public sealed class DamageMeterSnapshot
{
    public Dictionary<int, CombatantMetrics> Combatants { get; } = [];
    public Guid BattleId { get; set; } = Guid.NewGuid();
    public string TargetName { get; set; } = string.Empty;
    public long BattleTime { get; set; }
    public long BattleStartTime { get; set; }
    public long BattleEndTime { get; set; }
    public NpcRuntimeObservation? TargetObservation { get; set; }
    public EncounterSummary Encounter { get; set; } = new();

    public DamageMeterSnapshot DeepClone()
    {
        var clone = new DamageMeterSnapshot
        {
            BattleId = BattleId,
            TargetName = TargetName,
            BattleTime = BattleTime,
            BattleStartTime = BattleStartTime,
            BattleEndTime = BattleEndTime,
            TargetObservation = TargetObservation?.DeepClone(),
            Encounter = Encounter.DeepClone()
        };

        foreach (var (id, combatant) in Combatants)
        {
            clone.Combatants[id] = combatant.DeepClone();
        }

        return clone;
    }
}
