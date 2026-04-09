using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class EncounterHeuristicEvaluatorTests
{
    [Fact]
    public void Treats_SceneActivation_Hint_As_Active_Encounter()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            PhaseHint = NpcRuntimePhaseHint.SceneActivation
        };

        var summary = EncounterHeuristicEvaluator.Evaluate(4370, 0, observation);

        Assert.True(summary.IsActive);
        Assert.False(summary.ShouldArchive);
        Assert.Equal("scene-activation-hint", summary.Reason);
    }

    [Fact]
    public void Treats_Teardown_Hint_As_Archive_Candidate_After_Combat()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            PhaseHint = NpcRuntimePhaseHint.Teardown,
            Hp = 156000
        };

        var summary = EncounterHeuristicEvaluator.Evaluate(4370, 10_000, observation);

        Assert.False(summary.IsActive);
        Assert.True(summary.ShouldArchive);
        Assert.Equal("teardown-hint", summary.Reason);
    }

    [Fact]
    public void Treats_BattleToggle_As_Active_When_No_Stronger_Hint_Exists()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            BattleToggledOn = true,
            PhaseHint = NpcRuntimePhaseHint.ActiveCombat
        };

        var summary = EncounterHeuristicEvaluator.Evaluate(4370, 0, observation);

        Assert.True(summary.IsActive);
        Assert.False(summary.ShouldArchive);
        Assert.Equal("battle-toggle", summary.Reason);
    }
}
