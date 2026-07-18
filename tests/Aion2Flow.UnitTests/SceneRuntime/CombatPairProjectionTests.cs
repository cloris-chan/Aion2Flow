using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPairProjectionTests
{
    [Fact]
    public void Projection_GetPair_ReturnsCorrectSnapshot()
    {
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyDamage(combat, mechanics, resources, 100, 200, 500, 1, 1, 1000);

        var pair = CombatPairProjection.GetPair(combat, mechanics, resources, 100, 200);

        Assert.NotNull(pair);
        Assert.Equal(500, pair.Value.TotalDamage);
        Assert.Equal(1, pair.Value.HitCount);
        Assert.Equal(1, pair.Value.AttemptCount);
        Assert.Equal(1000, pair.Value.LastSkillCode);
    }

    [Fact]
    public void Projection_GetCombatant_HasOutgoingAndIncoming()
    {
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyDamage(combat, mechanics, resources, 100, 200, 500, 1, 1, 1000);
        ApplyDamage(combat, mechanics, resources, 200, 100, 100, 1, 1, 2000);

        var source = CombatPairProjection.GetCombatant(combat, mechanics, resources, 100);

        Assert.NotNull(source);
        Assert.Equal(500, source.Value.OutgoingDamage);
        Assert.Equal(1, source.Value.OutgoingHits);
        Assert.Equal(100, source.Value.IncomingDamage);
        Assert.Equal(1, source.Value.IncomingHits);
    }

    [Fact]
    public void Projection_Revision_MatchesStore()
    {
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyDamage(combat, mechanics, resources, 100, 200, 500, 1, 1, 1000);
        ApplyDamage(combat, mechanics, resources, 100, 200, 300, 1, 1, 1000);

        var pair = CombatPairProjection.GetPair(combat, mechanics, resources, 100, 200);

        Assert.Equal(Math.Max(combat.Revision, mechanics.Revision), pair!.Value.Revision);
    }

    [Fact]
    public void Projection_Rebuild_UpdatesOnNewData()
    {
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyDamage(combat, mechanics, resources, 100, 200, 500, 1, 1, 1000);

        Assert.Single(CombatPairProjection.BuildPairSnapshotMap(combat, mechanics, resources));

        ApplyDamage(combat, mechanics, resources, 100, 300, 300, 1, 1, 2000);
        var pairs = CombatPairProjection.BuildPairSnapshotMap(combat, mechanics, resources);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(2, combat.Revision);
        Assert.Equal(2, mechanics.Revision);
    }

    [Fact]
    public void Projection_ResourceOnlyPair_ProducesZeroMetricPairAndCombatantRoster()
    {
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ApplyResource(resources, 100, 200, 75, 4_001, 100);
        ApplyResource(resources, 100, 200, 25, 4_002, 200);

        var pair = CombatPairProjection.GetPair(combat, mechanics, resources, 100, 200);
        var combatants = CombatPairProjection.BuildCombatantSummaryMap(combat, mechanics, resources);

        Assert.NotNull(pair);
        Assert.Equal(0, pair.Value.TotalDamage);
        Assert.Equal(0, pair.Value.TotalHealing);
        Assert.Equal(0, pair.Value.HitCount);
        Assert.Equal(4_002, pair.Value.LastSkillCode);
        Assert.Equal(100, pair.Value.FirstObserved);
        Assert.Equal(200, pair.Value.LastObserved);
        Assert.Equal([new DirectedPairKey(100, 200)], CombatPairProjection.GetOutgoingPairs(combat, mechanics, resources, 100));
        Assert.Equal([new DirectedPairKey(100, 200)], CombatPairProjection.GetIncomingPairs(combat, mechanics, resources, 200));
        Assert.Equal(2, combatants.Count);
        Assert.Equal(0, combatants[100].OutgoingDamage);
        Assert.Equal(0, combatants[200].IncomingDamage);
    }

    private static void ApplyDamage(
        CombatStore combat,
        MechanicStore mechanics,
        ResourceStore resources,
        int sourceId,
        int targetId,
        long amount,
        int hitCount,
        int attemptCount,
        int skillCode)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount,
            HitCount = hitCount,
            AttemptCount = attemptCount
        };
        var materialization = CombatOccurrenceMaterializer.Resolve(
            sourceId,
            targetId,
            in observation,
            CombatOccurrenceResolution.Primary);
        var observedAtMilliseconds = Math.Max(combat.Revision, mechanics.Revision) + 1;

        if (materialization.Contribution is not { } contribution)
            throw new InvalidOperationException("Damage observation did not materialize a metric contribution.");

        combat.ApplyCombat(sourceId, targetId, in observation, in contribution, observedAtMilliseconds);
        if (materialization.Mechanic is { } mechanic)
            mechanics.Apply(sourceId, targetId, in observation, in mechanic, observedAtMilliseconds, CombatStore.UnknownSourceObservationOrdinal, default);
        if (materialization.Resource is { } resource)
            resources.Apply(sourceId, targetId, in observation, in resource, observedAtMilliseconds, CombatStore.UnknownSourceObservationOrdinal, default);
    }

    private static void ApplyResource(ResourceStore resources, int sourceId, int targetId, long amount, int skillCode, long observedAtMilliseconds)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            ResourceKind = CombatResourceKind.Mana,
            Damage = amount
        };
        var resource = new CombatResourceOccurrence(
            CombatResourceKind.Mana,
            CombatResourceFlowKind.Spend,
            CombatResourceDeliveryKind.Direct,
            amount,
            CombatResolutionTrace.FromPacket(CombatPacketRule.DirectManaResource, default, default));
        resources.Apply(sourceId, targetId, in observation, in resource, observedAtMilliseconds, CombatStore.UnknownSourceObservationOrdinal, default);
    }
}
