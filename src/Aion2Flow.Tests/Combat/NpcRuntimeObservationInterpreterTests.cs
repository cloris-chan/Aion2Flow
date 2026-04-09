using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class NpcRuntimeObservationInterpreterTests
{
    [Fact]
    public void Infers_SceneActivation_From_200003_And_4636_79()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            Value2136 = 200003,
            State4636Value0 = 2,
            State4636Value1 = 79
        };

        var phase = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);

        Assert.Equal(NpcRuntimePhaseHint.SceneActivation, phase);
    }

    [Fact]
    public void Infers_Teardown_From_1010_And_2C38_Result7()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            Value0240 = 1010,
            Result2C38 = 7,
            State4636Value0 = 2,
            State4636Value1 = 0
        };

        var phase = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);

        Assert.Equal(NpcRuntimePhaseHint.Teardown, phase);
    }

    [Fact]
    public void Does_Not_Use_Mismatched_2C38_Sequence_For_2136_Teardown()
    {
        var observation = new NpcRuntimeObservation
        {
            InstanceId = 4370,
            Value2136 = 1010,
            Sequence2136 = 6,
            Sequence2C38 = 95,
            Result2C38 = 7,
            State4636Value0 = 2,
            State4636Value1 = 5
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
