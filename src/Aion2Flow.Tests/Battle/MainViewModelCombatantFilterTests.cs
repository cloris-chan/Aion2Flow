using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class MainViewModelCombatantFilterTests
{
    [Theory]
    [InlineData(1010, 0, 200003, 113515, true, "map-transition")]
    [InlineData(1010, 0, 1010, 0, false, "")]
    [InlineData(200003, 113515, 200003, 113515, false, "")]
    [InlineData(200003, 113515, 200003, 113526, true, "map-instance-transition")]
    [InlineData(0, 0, 1010, 0, false, "")]
    [InlineData(600002, 396972, 1010, 0, true, "map-transition")]
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
            MapInstanceId = previousInstanceId
        };
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
    public void Unknown_To_Known_Map_With_Existing_Battle_Selects_Automatic_Reset()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 0,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = 600091
        };

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.True(result);
        Assert.Equal("map-transition", reason);
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
