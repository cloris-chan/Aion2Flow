using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsStoreNpcIdentityTests
{
    [Fact]
    public void AppendSummon_Marks_Instance_As_Summon_Npc()
    {
        var store = new CombatMetricsStore();

        store.AppendSummon(12115, 18345);

        Assert.True(store.SummonOwnerByInstance.TryGetValue(18345, out var ownerId));
        Assert.Equal(12115, ownerId);
        Assert.True(store.TryGetNpcRuntimeState(18345, out var state));
        Assert.Equal(NpcKind.Summon, state.Kind);
    }
}
