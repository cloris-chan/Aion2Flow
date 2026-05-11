using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatDetailSubscriptionTests
{
    [Fact]
    public void Subscription_Poll_ReturnsDeltaWhenRelevantCombatChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var sub = new CombatDetailSubscription(store, 100);

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

        var sub = new CombatDetailSubscription(store, 100);

        sub.Poll();

        var delta = sub.Poll();
        Assert.Null(delta);
    }

    [Fact]
    public void Subscription_Poll_ReturnsDeltaWhenTargetIsRelevant()
    {
        var store = new CombatStore();
        var sub = new CombatDetailSubscription(store, 200);

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

        var sub = new CombatDetailSubscription(store, 999);

        var delta = sub.Poll();
        Assert.Null(delta);
        Assert.Equal(0, sub.LastAppliedRevision);
    }

    [Fact]
    public void Subscription_MultiplePolls_AccumulateChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var sub = new CombatDetailSubscription(store, 100);

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

        var sub = new CombatDetailSubscription(store, 100);

        var delta1 = sub.Poll();
        Assert.Equal(500, delta1!.Combatant!.Value.OutgoingDamage);

        store.ApplyCombat(100, 200, 300, 1, 1, 1000);
        var delta2 = sub.Poll();

        Assert.Equal(800, delta2!.Combatant!.Value.OutgoingDamage);
    }

    [Fact]
    public void Subscription_DoesNotAdvanceRevisionForIrrelevantCombat()
    {
        var store = new CombatStore();
        var sub = new CombatDetailSubscription(store, 100);

        store.ApplyCombat(300, 400, 700, 1, 1, 3000);

        var delta = sub.Poll();

        Assert.Null(delta);
        Assert.Equal(0, sub.LastAppliedRevision);
        Assert.Equal(0, store.GetCombatantDetailRevision(100));
    }

    [Fact]
    public void Subscription_CatchesRelevantChangeAfterLargeIrrelevantBurst()
    {
        var store = new CombatStore();
        var sub = new CombatDetailSubscription(store, 100);

        for (int i = 0; i < 80; i++)
            store.ApplyCombat(300 + i, 400 + i, 1, 1, 1, 3000 + i);

        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var delta = sub.Poll();

        Assert.NotNull(delta);
        Assert.Equal(store.GetCombatantDetailRevision(100), delta!.Revision);
        Assert.Single(delta.OutgoingPairs);
    }
}
