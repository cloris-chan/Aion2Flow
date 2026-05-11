using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPairProjectionTests
{
    [Fact]
    public void Projection_BuildSnapshotMaps_BuildsCorrectly()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        store.ApplyCombat(200, 100, 100, 1, 1, 3000);

        var pairs = CombatPairProjection.BuildPairSnapshotMap(store);
        var combatants = CombatPairProjection.BuildCombatantSummaryMap(store);

        Assert.Equal(3, pairs.Count);
        Assert.Equal(3, combatants.Count);
    }

    [Fact]
    public void Projection_GetPair_ReturnsCorrectSnapshot()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var pair = CombatPairProjection.GetPair(store, 100, 200);

        Assert.NotNull(pair);
        Assert.Equal(500, pair.Value.TotalDamage);
        Assert.Equal(1000, pair.Value.LastSkillCode);
    }

    [Fact]
    public void Projection_GetCombatant_HasOutgoingAndIncoming()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(200, 100, 100, 1, 1, 2000);

        var source = CombatPairProjection.GetCombatant(store, 100);

        Assert.NotNull(source);
        Assert.Equal(500, source.Value.OutgoingDamage);
        Assert.Equal(100, source.Value.IncomingDamage);
    }

    [Fact]
    public void Projection_OutgoingPairs_ReturnsCorrectKeys()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);

        var outgoing = CombatPairProjection.GetOutgoingPairs(store, 100);

        Assert.Equal(2, outgoing.Count);
    }

    [Fact]
    public void Projection_IncomingPairs_ReturnsCorrectKeys()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(300, 200, 300, 1, 1, 2000);

        var incoming = CombatPairProjection.GetIncomingPairs(store, 200);

        Assert.Equal(2, incoming.Count);
    }

    [Fact]
    public void Projection_Revision_MatchesStore()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 200, 300, 1, 1, 1000);

        var pair = CombatPairProjection.GetPair(store, 100, 200);

        Assert.Equal(store.Revision, pair!.Value.Revision);
    }

    [Fact]
    public void Projection_Rebuild_UpdatesOnNewData()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        Assert.Single(CombatPairProjection.BuildPairSnapshotMap(store));

        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        var pairs = CombatPairProjection.BuildPairSnapshotMap(store);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(2, store.Revision);
    }
}
