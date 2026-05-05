using Cloris.Aion2Flow.Scene.Projection;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Tests.Scene;

public class CombatPairProjectionTests
{
    [Fact]
    public void Projection_FromCombatStore_BuildsCorrectly()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        store.ApplyCombat(200, 100, 100, 1, 1, 3000);

        var projection = CombatPairProjection.FromCombatStore(store);

        Assert.Equal(3, projection.Pairs.Count);
        Assert.Equal(3, projection.Combatants.Count);
    }

    [Fact]
    public void Projection_GetPair_ReturnsCorrectSnapshot()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var pair = projection.GetPair(100, 200);

        Assert.NotNull(pair);
        Assert.Equal(500, pair!.TotalDamage);
        Assert.Equal(1000, pair.LastSkillCode);
    }

    [Fact]
    public void Projection_GetCombatant_HasOutgoingAndIncoming()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(200, 100, 100, 1, 1, 2000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var source = projection.GetCombatant(100);

        Assert.NotNull(source);
        Assert.Equal(500, source!.OutgoingDamage);
        Assert.Equal(100, source.IncomingDamage);
    }

    [Fact]
    public void Projection_OutgoingPairs_ReturnsCorrectKeys()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var outgoing = projection.GetOutgoingPairs(100);

        Assert.Equal(2, outgoing.Count);
    }

    [Fact]
    public void Projection_IncomingPairs_ReturnsCorrectKeys()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(300, 200, 300, 1, 1, 2000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var incoming = projection.GetIncomingPairs(200);

        Assert.Equal(2, incoming.Count);
    }

    [Fact]
    public void Projection_Revision_MatchesStore()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 200, 300, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);

        Assert.Equal(store.Revision, projection.Revision);
    }

    [Fact]
    public void Projection_Rebuild_UpdatesOnNewData()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        Assert.Single(projection.Pairs);

        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        projection.Rebuild(store);

        Assert.Equal(2, projection.Pairs.Count);
        Assert.Equal(2, store.Revision);
    }
}
