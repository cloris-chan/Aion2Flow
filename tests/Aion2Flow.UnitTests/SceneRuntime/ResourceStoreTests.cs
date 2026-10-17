using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class ResourceStoreTests
{
    [Fact]
    public void Apply_AggregatesResourceFlowsAndPreservesEventEvidence()
    {
        var store = new ResourceStore();
        Apply(store, CombatResourceKind.Health, CombatResourceFlowKind.Restore, 100, skillCode: 1_001, observedAtMilliseconds: 100, sourceObservationOrdinal: 7);
        Apply(store, CombatResourceKind.Health, CombatResourceFlowKind.Unknown, 20, skillCode: 1_002, observedAtMilliseconds: 200, sourceObservationOrdinal: 8);
        Apply(store, CombatResourceKind.Mana, CombatResourceFlowKind.Restore, 30, skillCode: 1_003, observedAtMilliseconds: 300, sourceObservationOrdinal: 9);
        Apply(store, CombatResourceKind.Mana, CombatResourceFlowKind.Spend, 40, skillCode: 1_004, observedAtMilliseconds: 400, sourceObservationOrdinal: 10);
        Apply(store, CombatResourceKind.Mana, CombatResourceFlowKind.Unknown, 50, skillCode: 1_005, observedAtMilliseconds: 500, sourceObservationOrdinal: 11);

        Assert.Equal(5, store.Revision);
        Assert.Equal(5, store.Events.Count);
        for (var i = 0; i < store.Events.Count; i++)
        {
            var record = store.Events[i];
            Assert.Equal(i + 1, record.Revision);
            Assert.Equal(i + 7, record.SourceObservationOrdinal);
            Assert.Equal(new RawPacketReference(0x0438, 64, i + 7), record.Raw);
        }

        Assert.True(store.TryGetPair(100, 200, out var pair));
        Assert.Equal(100, pair!.HealthRestored);
        Assert.Equal(20, pair.HealthUnknown);
        Assert.Equal(30, pair.ManaRestored);
        Assert.Equal(40, pair.ManaSpent);
        Assert.Equal(50, pair.ManaUnknown);
        Assert.Equal(1_005, pair.LastSkillCode);
        Assert.Equal(100, pair.FirstObserved);
        Assert.Equal(500, pair.LastObserved);
        Assert.Equal(5, pair.Revision);
    }

    private static void Apply(
        ResourceStore store,
        CombatResourceKind resourceKind,
        CombatResourceFlowKind flow,
        long amount,
        int skillCode,
        long observedAtMilliseconds,
        long sourceObservationOrdinal)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount,
            ResourceKind = resourceKind
        };
        var packetRule = resourceKind == CombatResourceKind.Health
            ? CombatPacketRule.DirectHealthResource
            : CombatPacketRule.DirectManaResource;
        var resource = new CombatResourceOccurrence(
            resourceKind,
            flow,
            CombatResourceDeliveryKind.Direct,
            amount,
            CombatResolutionTrace.FromPacket(packetRule, default, default));
        var raw = new RawPacketReference(0x0438, 64, sourceObservationOrdinal);

        store.Apply(
            sourceId: 100,
            targetId: 200,
            in observation,
            in resource,
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw);
    }
}
