using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Battle.Archive;

public sealed class ArchivedBattleRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BattleId { get; init; }
    public DateTimeOffset ArchivedAt { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public bool IsAutomatic { get; init; }
    public DamageMeterSnapshot Snapshot { get; init; } = new();
    public CombatMetricsStore Store { get; init; } = new();

    public string DisplayName
    {
        get
        {
            var battleSeconds = Snapshot.BattleTime / 1000d;
            return $"{ArchivedAt:HH:mm:ss} {ResolveSceneLabel(Snapshot)} ({battleSeconds:0.0}s)";
        }
    }

    private static string ResolveSceneLabel(DamageMeterSnapshot snapshot)
    {
        return snapshot.Encounter.PhaseHint switch
        {
            NpcRuntimePhaseHint.SceneActivation => "Scene Activation",
            NpcRuntimePhaseHint.ActiveCombat => "Combat Scene",
            NpcRuntimePhaseHint.Teardown => "Scene Teardown",
            _ => snapshot.Encounter.Reason switch
            {
                "scene-activation-hint" => "Scene Activation",
                "battle-time" or "battle-toggle" or "hp-observed" => "Combat Scene",
                "teardown-hint" => "Scene Teardown",
                _ => "Scene Archive"
            }
        };
    }
}
