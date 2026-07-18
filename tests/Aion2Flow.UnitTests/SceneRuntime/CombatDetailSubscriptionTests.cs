using Cloris.Aion2Flow.SceneRuntime.Observation;
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
        var mechanics = new MechanicStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var sub = CreateSubscription(store, mechanics, 100);

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
        var mechanics = new MechanicStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var sub = CreateSubscription(store, mechanics, 100);

        sub.Poll();

        var delta = sub.Poll();
        Assert.Null(delta);
    }

    [Fact]
    public void Subscription_Poll_ReturnsDeltaWhenTargetIsRelevant()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        var sub = CreateSubscription(store, mechanics, 200);

        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var delta = sub.Poll();
        Assert.NotNull(delta);
        Assert.Single(delta!.IncomingPairs);
    }

    [Fact]
    public void Subscription_Poll_ReturnsNullForIrrelevantCombat()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var sub = CreateSubscription(store, mechanics, 999);

        var delta = sub.Poll();
        Assert.Null(delta);
        Assert.Equal(0, sub.LastAppliedRevision);
    }

    [Fact]
    public void Subscription_MultiplePolls_AccumulateChanges()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var sub = CreateSubscription(store, mechanics, 100);

        sub.Poll();

        ApplyMetricDamage(store, 100, 300, 300, 2000);
        var delta = sub.Poll();

        Assert.NotNull(delta);
        Assert.Equal(2, delta!.OutgoingPairs.Count);
        Assert.Equal(2, sub.LastAppliedRevision);
    }

    [Fact]
    public void Subscription_CombatantSummary_UpdatesAcrossPolls()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var sub = CreateSubscription(store, mechanics, 100);

        var delta1 = sub.Poll();
        Assert.Equal(500, delta1!.Combatant!.Value.OutgoingDamage);

        ApplyMetricDamage(store, 100, 200, 300, 1000);
        var delta2 = sub.Poll();

        Assert.Equal(800, delta2!.Combatant!.Value.OutgoingDamage);
    }

    [Fact]
    public void Subscription_DoesNotAdvanceRevisionForIrrelevantCombat()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        var sub = CreateSubscription(store, mechanics, 100);

        ApplyMetricDamage(store, 300, 400, 700, 3000);

        var delta = sub.Poll();

        Assert.Null(delta);
        Assert.Equal(0, sub.LastAppliedRevision);
        Assert.Equal(0, store.GetCombatantDetailRevision(100));
    }

    [Fact]
    public void Subscription_CatchesRelevantChangeAfterLargeIrrelevantBurst()
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        var sub = CreateSubscription(store, mechanics, 100);

        for (int i = 0; i < 80; i++)
            ApplyMetricDamage(store, 300 + i, 400 + i, 1, 3000 + i);

        ApplyMetricDamage(store, 100, 200, 500, 1000);

        var delta = sub.Poll();

        Assert.NotNull(delta);
        Assert.Equal(store.GetCombatantDetailRevision(100), delta!.Revision);
        Assert.Single(delta.OutgoingPairs);
    }

    [Fact]
    public void Subscription_Update_WritesFullSnapshotOnColdStart()
    {
        var (store, mechanics, resources, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = CreateSubscription(store, mechanics, 100, resources);

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(update.IsFullSnapshot);
        Assert.True(update.HasChanges);
        Assert.Equal(2, update.AddedMetricEventCount);
        Assert.Equal(2, writer.MetricEvents.Count);
        Assert.Equal(2, update.Revision);
    }

    [Fact]
    public void Subscription_Update_IgnoresIrrelevantWarmChanges()
    {
        var (store, mechanics, resources, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = CreateSubscription(store, mechanics, 100, resources);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        ApplyMetricDamage(store, 300, 200, 900, 3000);
        adapter = CreateAdapter(store, mechanics, resources);
        snapshot = adapter.CreateSnapshot();
        var beforeCount = writer.MetricEvents.Count;

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(update.HasChanges);
        Assert.Equal(beforeCount, writer.MetricEvents.Count);
    }

    [Fact]
    public void Subscription_Update_AppendsOnlyRelevantWarmEvents()
    {
        var (store, mechanics, resources, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = CreateSubscription(store, mechanics, 100, resources);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        ApplyMetricDamage(store, 100, 200, 300, 3000);
        adapter = CreateAdapter(store, mechanics, resources);
        snapshot = adapter.CreateSnapshot();

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(update.IsFullSnapshot);
        Assert.True(update.HasChanges);
        Assert.Equal(1, update.AddedMetricEventCount);
        Assert.Equal(3, writer.MetricEvents.Count);
        Assert.Equal(3, update.Revision);
    }

    [Fact]
    public void Subscription_Update_RebuildsWhenContextChanges()
    {
        var (store, mechanics, resources, adapter, snapshot) = CreateProjection();
        var writer = new TestDetailWriter();
        var sub = CreateSubscription(store, mechanics, 100, resources);
        sub.Update(adapter, snapshot, forceRefresh: false, writer);

        adapter = CreateAdapter(store, mechanics, resources, Guid.NewGuid());
        snapshot = adapter.CreateSnapshot();

        var update = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(update.IsFullSnapshot);
        Assert.Equal(2, update.AddedMetricEventCount);
        Assert.Equal(2, writer.ClearCount);
        Assert.Equal(2, writer.MetricEvents.Count);
    }

    [Fact]
    public void Subscription_Update_FromSnapshotKeepsHistoryWithoutReplayingHistoricalChanges()
    {
        var (store, mechanics, resources, _, _) = CreateProjection();
        var restored = CombatStore.FromSnapshot(store.CreateSnapshot());
        var restoredMechanics = MechanicStore.FromSnapshot(mechanics.CreateSnapshot());
        var restoredResources = ResourceStore.FromSnapshot(resources.CreateSnapshot());
        var adapter = CreateAdapter(restored, restoredMechanics, restoredResources);
        var snapshot = adapter.CreateSnapshot();
        var writer = new TestDetailWriter();
        var sub = CreateSubscription(restored, restoredMechanics, 100, restoredResources);

        var full = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.True(full.IsFullSnapshot);
        Assert.Equal(2, full.AddedMetricEventCount);
        Assert.Equal(2, writer.MetricEvents.Count);
        Assert.False(sub.Update(adapter, snapshot, forceRefresh: false, writer).HasChanges);

        ApplyMetricDamage(restored, 100, 200, 300, 3000);
        adapter = CreateAdapter(restored, restoredMechanics, restoredResources);
        snapshot = adapter.CreateSnapshot();
        var warm = sub.Update(adapter, snapshot, forceRefresh: false, writer);

        Assert.False(warm.IsFullSnapshot);
        Assert.True(warm.HasChanges);
        Assert.Equal(1, warm.AddedMetricEventCount);
        Assert.Equal(3, writer.MetricEvents.Count);
    }

    private static (CombatStore Store, MechanicStore Mechanics, ResourceStore Resources, SceneCombatSnapshotAdapter Adapter, SceneCombatSnapshot Snapshot) CreateProjection(Guid? encounterId = null)
    {
        var store = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyMetricDamage(store, 100, 200, 500, 1000);
        ApplyMetricDamage(store, 100, 200, 700, 2000);
        var adapter = CreateAdapter(store, mechanics, resources, encounterId);
        return (store, mechanics, resources, adapter, adapter.CreateSnapshot());
    }

    private static SceneCombatSnapshotAdapter CreateAdapter(CombatStore store, MechanicStore mechanics, ResourceStore resources, Guid? encounterId = null) =>
        new(new EntityStore(), new EntityVitalStore(), store, mechanics, resources, new SceneBoundaryStore(), null, encounterId ?? TestEncounterId);

    private static CombatDetailSubscription CreateSubscription(
        CombatStore store,
        MechanicStore mechanics,
        int combatantId,
        ResourceStore? resources = null) =>
        new(store, mechanics, resources ?? new ResourceStore(), combatantId);

    private static void ApplyMetricDamage(CombatStore store, int sourceId, int targetId, long amount, int skillCode)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount
        };
        var materialization = CombatOccurrenceMaterializer.Resolve(
            sourceId,
            targetId,
            in observation,
            CombatOccurrenceResolution.Primary);
        if (materialization.Contribution is not { } contribution)
            throw new InvalidOperationException("Damage observation did not materialize a metric contribution.");

        store.ApplyCombat(sourceId, targetId, in observation, in contribution, store.Revision + 1);
    }

    private sealed class TestDetailWriter : ICombatDetailEventWriter
    {
        public List<CombatMetricDetailEvent> MetricEvents { get; } = [];
        public List<CombatMechanicDetailEvent> MechanicEvents { get; } = [];
        public List<CombatResourceDetailEvent> ResourceEvents { get; } = [];
        public int ClearCount { get; private set; }

        public void Clear()
        {
            ClearCount++;
            MetricEvents.Clear();
            MechanicEvents.Clear();
            ResourceEvents.Clear();
        }

        public void AddMetric(in CombatMetricDetailEvent detailEvent) => MetricEvents.Add(detailEvent);

        public void AddMechanic(in CombatMechanicDetailEvent detailEvent) => MechanicEvents.Add(detailEvent);

        public void AddResource(in CombatResourceDetailEvent detailEvent) => ResourceEvents.Add(detailEvent);
    }
}
