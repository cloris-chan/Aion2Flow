namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class NpcRuntimeObservationInterpreterTests
{
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)5)]
    [InlineData((byte)79)]
    public void Leaves_Unresolved_State_And_Result_Markers_Unknown(byte state)
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            Sequence2C38 = 95,
            Result2C38 = 7,
            State4636Value0 = 2,
            State4636Value1 = state
        };

        var phase = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);

        Assert.Equal(NpcRuntimePhaseHint.Unknown, phase);
    }

    [Fact]
    public void Infers_ActiveCombat_From_BattleToggle_When_No_Stronger_Hint_Exists()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            BattleToggledOn = true
        };

        var phase = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);

        Assert.Equal(NpcRuntimePhaseHint.ActiveCombat, phase);
    }
}
