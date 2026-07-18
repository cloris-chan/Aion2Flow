using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class EntityVitalStoreTests
{
    [Fact]
    public void Apply_StoresCurrentAndMaximumHp()
    {
        var store = new EntityVitalStore();
        var observation = new EntityVitalObservation(56_688, 22_847, 9_000_000);

        var state = store.Apply(in observation, observedAtMilliseconds: 1_000, observationOrdinal: 7);

        Assert.Equal(56_688, state.EntityId);
        Assert.Equal(22_847, state.CurrentHp);
        Assert.Equal(9_000_000, state.MaxHp);
        Assert.Equal(1_000, state.ObservedAtMilliseconds);
        Assert.Equal(7, state.ObservationOrdinal);
        Assert.Equal(1, state.Revision);
        Assert.Equal(1, store.Revision);
    }

    [Fact]
    public void Apply_DoesNotInferMaximumFromCurrentHp()
    {
        var store = new EntityVitalStore();
        var observation = new EntityVitalObservation(56_688, 22_847, null);

        var state = store.Apply(in observation, observedAtMilliseconds: 1_000, observationOrdinal: 0);

        Assert.Equal(22_847, state.CurrentHp);
        Assert.Null(state.MaxHp);
    }

    [Fact]
    public void Apply_PreservesKnownMaximumWhenLaterObservationOmitsIt()
    {
        var store = new EntityVitalStore();
        var initial = new EntityVitalObservation(56_688, 49_200, 49_200);
        var update = new EntityVitalObservation(56_688, 22_847, null);

        store.Apply(in initial, observedAtMilliseconds: 900, observationOrdinal: 0);
        var state = store.Apply(in update, observedAtMilliseconds: 1_000, observationOrdinal: 1);

        Assert.Equal(22_847, state.CurrentHp);
        Assert.Equal(49_200, state.MaxHp);
        Assert.Equal(2, state.Revision);
    }

    [Fact]
    public void CreateStateSnapshot_ReturnsStatesInEntityOrder()
    {
        var store = new EntityVitalStore();
        var second = new EntityVitalObservation(200, 20, 100);
        var first = new EntityVitalObservation(100, 10, 100);
        store.Apply(in second, observedAtMilliseconds: 1, observationOrdinal: 0);
        store.Apply(in first, observedAtMilliseconds: 2, observationOrdinal: 1);

        var snapshot = store.CreateStateSnapshot();

        Assert.Collection(
            snapshot,
            state => Assert.Equal(100, state.EntityId),
            state => Assert.Equal(200, state.EntityId));
    }
}
