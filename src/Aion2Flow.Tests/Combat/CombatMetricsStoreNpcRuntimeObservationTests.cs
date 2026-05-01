using Cloris.Aion2Flow.Battle.Model;
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

    [Fact]
    public void Observed_Boss_Tracks_Remain_Hp_And_Max_Hp_Within_Visibility_Window()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 900);
        store.AppendNpcHp(3518, 156_500, 1_000);
        store.AppendNpcHp(3518, 167_000, 1_100);
        store.AppendNpcHp(3518, 152_000, 1_200);

        Assert.True(store.TryGetObservedBoss(2_000, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(152_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
        Assert.Equal(1_200, boss.LastObservedAtMilliseconds);

        store.AppendBossFocusExit(3518);
        Assert.False(store.TryGetObservedBoss(3_500, 2_000, out _));
    }

    [Fact]
    public void Observed_Boss_Uses_Explicit_Max_Hp_From_Spawn()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendNpcHp(3518, 49_200, 49_200, 900);
        store.AppendBossFocusEnter(3518, 1_000);
        store.AppendNpcHp(3518, 22_847, 1_100);

        Assert.True(store.TryGetObservedBoss(1_200, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(22_847, boss.Hp);
        Assert.Equal(49_200, boss.MaxHp);
    }

    [Fact]
    public void Boss_Focus_Pulse_Does_Not_Persist_After_Visibility_Window()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendNpcHp(3518, 49_200, 49_200, 900);
        store.ObserveBossFocusPulse(3518, 1_000);

        Assert.True(store.TryGetObservedBoss(1_500, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(49_200, boss.MaxHp);
        Assert.False(store.TryGetObservedBoss(3_001, 2_000, out _));
    }

    [Fact]
    public void Observed_Boss_Promotes_Existing_Hp_When_Active_Npc_Is_Later_Identified_As_Boss()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcHp(3518, 156_500, 1_000);

        Assert.False(store.TryGetObservedBoss(1_000, 2_000, out _));

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 1_100);

        Assert.True(store.TryGetObservedBoss(1_500, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(156_500, boss.Hp);
        Assert.Equal(156_500, boss.MaxHp);
    }

    [Fact]
    public void Observed_Boss_Activity_Can_Show_Name_Before_Hp_Is_Known()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 900);

        Assert.True(store.TryGetObservedBoss(950, 2_000, out var unknownHpBoss));
        Assert.False(unknownHpBoss.HasHp);
        Assert.Equal(3518, unknownHpBoss.InstanceId);

        store.AppendNpcHp(3518, 157_000, 1_000);
        store.AppendNpcHp(3518, 167_000, 1_100);

        Assert.True(store.TryGetObservedBoss(1_500, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(167_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
        Assert.Equal(1_100, boss.LastObservedAtMilliseconds);
    }

    [Fact]
    public void Observed_Boss_Exit_Clears_And_Ignores_Later_Hp_Until_Reentered()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 900);
        store.AppendNpcHp(3518, 157_000, 1_000);
        store.AppendBossFocusExit(3518);
        store.AppendNpcHp(3518, 166_500, 1_100);

        Assert.False(store.TryGetObservedBoss(1_200, 2_000, out _));

        store.AppendBossFocusEnter(3518, 1_300);
        store.AppendNpcHp(3518, 167_000, 1_400);

        Assert.True(store.TryGetObservedBoss(1_500, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(167_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
    }

    [Fact]
    public void Observed_Boss_Is_Cleared_By_Later_NonBoss_Kind()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 900);
        store.AppendNpcHp(3518, 157_000, 1_000);
        store.AppendNpcKind(3518, NpcKind.Monster);

        Assert.False(store.TryGetObservedBoss(1_100, 2_000, out _));
    }

    [Fact]
    public void Observed_Boss_Activity_Follows_Lifecycle_Rebind()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(3518, NpcKind.Boss);
        store.AppendBossFocusEnter(3518, 900);
        store.AppendNpcHp(3518, 157_000, 1_000);
        var reboundId = store.RebindInstanceLifecycle(3518);
        store.AppendNpcHp(3518, 156_500, 1_100);

        Assert.True(store.TryGetObservedBoss(1_200, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(reboundId, boss.InstanceId);
        Assert.Equal(156_500, boss.Hp);
        Assert.Equal(156_500, boss.MaxHp);
    }

    [Fact]
    public void Observed_Boss_Ignores_NonBoss_Hp()
    {
        var store = new CombatMetricsStore();

        store.AppendNpcKind(20420, NpcKind.Monster);
        store.AppendNpcHp(20420, 74_432, 1_000);

        Assert.False(store.TryGetObservedBoss(1_000, 2_000, out _));
    }
}
