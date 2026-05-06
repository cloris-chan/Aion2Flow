using Cloris.Aion2Flow.Scene.Projection;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Tests.Scene;

public class CombatDetailSubscriptionTests
{
    [Fact]
    public void Subscription_Poll_ReturnsDeltaWhenRelevantCombatChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 100);

        var delta = sub.Poll();
        Assert.NotNull(delta);
        Assert.Equal(100, delta!.CombatantId);
        Assert.Equal(1, delta.Revision);
        Assert.Single(delta.OutgoingPairs);
    }

    [Fact]
    public void Subscription_Poll_ReturnsNullWhenNoNewChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 100);

        sub.Poll();

        var delta = sub.Poll();
        Assert.Null(delta);
    }

    [Fact]
    public void Subscription_Poll_ReturnsDeltaWhenTargetIsRelevant()
    {
        var store = new CombatStore();
        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 200);

        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var delta = sub.Poll();
        Assert.NotNull(delta);
        Assert.Single(delta!.IncomingPairs);
    }

    [Fact]
    public void Subscription_Poll_ReturnsNullForIrrelevantCombat()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 999);

        var delta = sub.Poll();
        Assert.Null(delta);
    }

    [Fact]
    public void Subscription_MultiplePolls_AccumulateChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 100);

        sub.Poll();

        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        var delta = sub.Poll();

        Assert.NotNull(delta);
        Assert.Equal(2, delta!.OutgoingPairs.Count);
        Assert.Equal(2, sub.LastAppliedRevision);
    }

    [Fact]
    public void Subscription_CombatantSummary_UpdatesAcrossPolls()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var projection = CombatPairProjection.FromCombatStore(store);
        var sub = new CombatDetailSubscription(store, projection, 100);

        var delta1 = sub.Poll();
        Assert.Equal(500, delta1!.Combatant!.OutgoingDamage);

        store.ApplyCombat(100, 200, 300, 1, 1, 1000);
        var delta2 = sub.Poll();

        Assert.Equal(800, delta2!.Combatant!.OutgoingDamage);
    }
}
