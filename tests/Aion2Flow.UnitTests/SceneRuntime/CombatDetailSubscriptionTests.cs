using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatDetailSubscriptionTests
{
    private static readonly Guid TestEncounterId = Guid.Parse("8F3E5D78-0101-47C2-9DC5-FD6D52AF2E70");

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

    [Fact]
    public void Subscription_Update_WritesFullSnapshotOnColdStart()
    {
        var (store, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = new CombatDetailSubscription(store, 100);

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(update.IsFullSnapshot);
        Assert.True(update.HasChanges);
        Assert.Equal(2, update.AddedEventCount);
        Assert.Equal(2, writer.Events.Count);
        Assert.Equal(2, update.Revision);
    }

    [Fact]
    public void Subscription_Update_IgnoresIrrelevantWarmChanges()
    {
        var (store, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = new CombatDetailSubscription(store, 100);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        store.ApplyCombat(300, 200, 900, 1, 1, 3000);
        adapter = CreateAdapter(store);
        snapshot = adapter.CreateSnapshot();
        var beforeCount = writer.Events.Count;

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(update.HasChanges);
        Assert.Equal(beforeCount, writer.Events.Count);
    }

    [Fact]
    public void Subscription_Update_AppendsOnlyRelevantWarmEvents()
    {
        var (store, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = new CombatDetailSubscription(store, 100);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        store.ApplyCombat(100, 200, 300, 1, 1, 3000);
        adapter = CreateAdapter(store);
        snapshot = adapter.CreateSnapshot();

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(update.IsFullSnapshot);
        Assert.True(update.HasChanges);
        Assert.Equal(1, update.AddedEventCount);
        Assert.Equal(3, writer.Events.Count);
        Assert.Equal(3, update.Revision);
    }

    [Fact]
    public void Subscription_Update_RebuildsWhenContextChanges()
    {
        var (store, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = new CombatDetailSubscription(store, 100);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        adapter = CreateAdapter(store, Guid.NewGuid());
        snapshot = adapter.CreateSnapshot();

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(update.IsFullSnapshot);
        Assert.Equal(2, update.AddedEventCount);
        Assert.Equal(2, writer.ClearCount);
        Assert.Equal(2, writer.Events.Count);
    }

    [Fact]
    public void Subscription_Update_FromSnapshotKeepsHistoryWithoutReplayingHistoricalChanges()
    {
        var (store, adapter, snapshot) = CreateProjection();
        var restored = CombatStore.FromSnapshot(store.CreateSnapshot());
        adapter = CreateAdapter(restored);
        snapshot = adapter.CreateSnapshot();
        var writer = new TestDetailWriter();
        var sub = new CombatDetailSubscription(restored, 100);

        var full = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(full.IsFullSnapshot);
        Assert.Equal(2, full.AddedEventCount);
        Assert.Equal(2, writer.Events.Count);
        Assert.False(sub.Update(adapter, snapshot, forceRefresh: false, writer).HasChanges);

        restored.ApplyCombat(100, 200, 300, 1, 1, 3000);
        adapter = CreateAdapter(restored);
        snapshot = adapter.CreateSnapshot();
        var warm = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(warm.IsFullSnapshot);
        Assert.True(warm.HasChanges);
        Assert.Equal(1, warm.AddedEventCount);
        Assert.Equal(3, writer.Events.Count);
    }

    private static (CombatStore Store, SceneCombatSnapshotAdapter Adapter, SceneCombatSnapshot Snapshot) CreateProjection(Guid? encounterId = null)
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 200, 700, 1, 1, 2000);
        var adapter = CreateAdapter(store, encounterId);
        return (store, adapter, adapter.CreateSnapshot());
    }

    private static SceneCombatSnapshotAdapter CreateAdapter(CombatStore store, Guid? encounterId = null) =>
        new(new EntityStore(), store, new SceneBoundaryStore(), null, encounterId ?? TestEncounterId);

    private sealed class TestDetailWriter : ICombatDetailEventWriter
    {
        public List<CombatDetailEvent> Events { get; } = [];
        public int ClearCount { get; private set; }

        public void Clear()
        {
            ClearCount++;
            Events.Clear();
        }

        public void Add(in CombatDetailEvent detailEvent) => Events.Add(detailEvent);
    }
}
