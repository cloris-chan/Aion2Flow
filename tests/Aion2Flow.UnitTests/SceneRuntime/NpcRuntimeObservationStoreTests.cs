using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class NpcRuntimeObservationStoreTests
{
    [Fact]
    public void Stores_Boss_Runtime_Observation_Hints_Per_Instance()
    {
        using var scene = new SceneTestHarness();

        scene.AppendNpc2136State(4370, 6, 200003);
        scene.AppendNpc0140Value(4370, 200003);
        scene.AppendNpc0240Value(4370, 200003);
        scene.AppendNpc4636State(4370, 2, 79);
        scene.Sink.RegisterObservation2C38(4370, 95, 7, 0, 0, 0);

        Assert.True(scene.TryGetNpcRuntimeState(4370, out var state));
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
    public void AppendSummon_Marks_Instance_As_Summon_Npc()
    {
        using var scene = new SceneTestHarness();

        scene.AppendSummon(12115, 18345);

        scene.Owner.Refresh();
        Assert.True(scene.Owner.Entities.TryGet(18345, out var summon));
        Assert.Equal(12115, summon.OwnerEntityId);
        Assert.Equal(NpcKind.Summon, summon.Kind);
    }

    [Fact]
    public void Observed_Boss_Is_Cleared_When_Remain_Hp_Reaches_Zero()
    {
        using var scene = new SceneTestHarness();

        scene.AppendNpcKind(3518, NpcKind.Boss);
        scene.SetNpcBattle(3518, true, 900);
        scene.AppendNpcHp(3518, 157_000, 1_000);

        scene.Owner.Refresh();
        Assert.True(scene.Owner.BossFocus.TryGetObservedBoss(10_000, 2_000, out _));

        scene.AppendNpcHp(3518, 0, 1_100);
        scene.SetNpcBattle(3518, true, 1_200);
        scene.ToggleNpcBattle(3518);

        scene.Owner.Refresh();
        Assert.False(scene.Owner.BossFocus.TryGetObservedBoss(1_300, 2_000, out _));
        Assert.True(scene.TryGetNpcRuntimeState(3518, out var state));
        Assert.Equal(0, state.Hp);
        Assert.False(state.BattleToggledOn);
    }
}
