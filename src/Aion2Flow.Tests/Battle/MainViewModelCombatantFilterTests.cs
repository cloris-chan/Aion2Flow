using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class MainViewModelCombatantFilterTests
{
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
