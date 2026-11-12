namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class BossFocusHeuristicEvaluatorTests
{
    [Fact]
    public void Treats_Observed_Hp_As_Active_Encounter()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            Hp = 156000
        };

        var summary = BossFocusHeuristicEvaluator.Evaluate(4370, 0, observation);

        Assert.True(summary.IsActive);
        Assert.False(summary.ShouldArchive);
        Assert.Equal("hp-observed", summary.Reason);
    }

    [Fact]
    public void Treats_Combat_Duration_As_Active_Without_Npc_Metadata()
    {
        var summary = BossFocusHeuristicEvaluator.Evaluate(4370, 10_000, null);

        Assert.True(summary.IsActive);
        Assert.False(summary.ShouldArchive);
        Assert.Equal("encounter-time", summary.Reason);
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

        var summary = BossFocusHeuristicEvaluator.Evaluate(4370, 0, observation);

        Assert.True(summary.IsActive);
        Assert.False(summary.ShouldArchive);
        Assert.Equal("battle-toggle", summary.Reason);
    }
}
