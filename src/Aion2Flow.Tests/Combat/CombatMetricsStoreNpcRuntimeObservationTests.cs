using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsStoreNpcRuntimeObservationTests
{
    [Fact]
    public void Stores_Boss_Runtime_Observation_Hints_Per_Instance()
    {
        var store = new CombatMetricsStore();

        store.AppendNpc2136State(4370, 6, 200003);
        store.AppendNpc0140Value(4370, 200003);
        store.AppendNpc0240Value(4370, 200003);
        store.AppendNpc4636State(4370, 2, 79);
        store.AppendNpc2C38State(4370, 95, 7);

        Assert.True(store.TryGetNpcRuntimeState(4370, out var state));
        Assert.Equal((uint)6, state.Sequence2136);
        Assert.Equal((uint)200003, state.Value2136);
        Assert.Equal((uint)200003, state.Value0140);
        Assert.Equal((uint)200003, state.Value0240);
        Assert.Equal((byte)2, state.State4636?.State0);
        Assert.Equal((byte)79, state.State4636?.State1);
        Assert.Equal(95, state.Latest2C38?.SequenceId);
        Assert.Equal(7, state.Latest2C38?.ResultCode);
    }

    [Fact]
    public void Resolves_2C38_State_By_Preferred_Sequence_Without_Falling_Back_To_Mismatched_Latest()
    {
        var store = new CombatMetricsStore();

        store.AppendNpc2C38State(4370, 95, 7);
        store.AppendNpc2C38State(4370, 96, 1);

        Assert.True(store.TryGetNpc2C38State(4370, 95, out var matchedSequence, out var matchedResult));
        Assert.Equal(95, matchedSequence);
        Assert.Equal(7, matchedResult);

        Assert.False(store.TryGetNpc2C38State(4370, 6, out _, out _));

        Assert.True(store.TryGetNpc2C38State(4370, null, out var latestSequence, out var latestResult));
        Assert.Equal(96, latestSequence);
        Assert.Equal(1, latestResult);
    }
}
