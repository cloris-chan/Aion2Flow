using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class MainViewModelCombatantFilterTests
{
    [Theory]
    [InlineData(1010u, 0u, 200003u, 113515u, true, "map-transition")]
    [InlineData(1010u, 0u, 1010u, 0u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113515u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113526u, true, "map-instance-transition")]
    [InlineData(200003u, 0u, 200003u, 113515u, true, "map-instance-transition")]
    [InlineData(0u, 0u, 1010u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 50u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 0u, 0u, false, "")]
    [InlineData(600002u, 396972u, 1010u, 0u, true, "map-transition")]
    public void Map_Transitions_Select_Automatic_Reset_Scope(
        uint previousMapId,
        uint previousInstanceId,
        uint latestMapId,
        uint latestInstanceId,
        bool expected,
        string expectedReason)
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = previousMapId,
            MapInstanceId = previousInstanceId,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = latestMapId,
            MapInstanceId = latestInstanceId
        };

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.Equal(expected, result);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void Map_Change_Without_Battle_Does_Not_Trigger_Reset()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 600002
        };

        var latest = new DamageMeterSnapshot
        {
            MapId = 1010
        };

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.False(result);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Predictive_MapId_Flip_Without_Confirmation_Does_Not_Archive()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 1010,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = 1010
        };

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Sub_Instance_Boss_Room_Does_Not_Archive()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 910036,
            MapInstanceId = 113515,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = 910036,
            MapInstanceId = 113515
        };

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void ShouldDisplayCombatant_Hides_Known_Npc_Even_If_Class_Was_Previously_Inferred()
    {
        var store = new CombatMetricsStore();
        const int npcInstanceId = 19945;
        store.AppendNpcCode(npcInstanceId, 2100350);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        var combatant = new CombatantMetrics("Torbas Forest Talekun")
        {
            CharacterClass = CharacterClass.Elementalist
        };

        Assert.False(MainViewModel.ShouldDisplayCombatant(store, npcInstanceId, combatant));
    }

    [Fact]
    public void ShouldDisplayCombatant_Hides_Combatants_Without_Player_Class()
    {
        var store = new CombatMetricsStore();
        var combatant = new CombatantMetrics("Unknown");

        Assert.False(MainViewModel.ShouldDisplayCombatant(store, 38924, combatant));
    }

    [Fact]
    public void ShouldDisplayCombatant_Keeps_Player_Class_When_Not_Npc()
    {
        var store = new CombatMetricsStore();
        var combatant = new CombatantMetrics("Player")
        {
            CharacterClass = CharacterClass.Chanter
        };

        Assert.True(MainViewModel.ShouldDisplayCombatant(store, 12669, combatant));
    }
}
