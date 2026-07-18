using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class MechanicStoreTests
{
    [Fact]
    public void Apply_AggregatesPacketMechanicsAndRawMultiHitSubCount()
    {
        var store = new MechanicStore();

        Apply(
            store,
            skillCode: 1_001,
            modifiers: DamageModifiers.Critical | DamageModifiers.MultiHit,
            hitCount: 1,
            attemptCount: 1,
            evadeCount: 0,
            invincibleCount: 0,
            multiHitCount: 1,
            multiHitSubCount: 4,
            observedAtMilliseconds: 100,
            sourceObservationOrdinal: 7);
        Apply(
            store,
            skillCode: 1_002,
            modifiers: DamageModifiers.MultiHit,
            hitCount: 2,
            attemptCount: 2,
            evadeCount: 0,
            invincibleCount: 0,
            multiHitCount: 1,
            multiHitSubCount: 7,
            observedAtMilliseconds: 200,
            sourceObservationOrdinal: 8);
        Apply(
            store,
            skillCode: 1_003,
            modifiers: DamageModifiers.Evade,
            hitCount: 0,
            attemptCount: 1,
            evadeCount: 1,
            invincibleCount: 0,
            multiHitCount: 0,
            multiHitSubCount: 0,
            observedAtMilliseconds: 300,
            sourceObservationOrdinal: 9);
        Apply(
            store,
            skillCode: 1_004,
            modifiers: DamageModifiers.Invincible,
            hitCount: 0,
            attemptCount: 2,
            evadeCount: 0,
            invincibleCount: 2,
            multiHitCount: 0,
            multiHitSubCount: 0,
            observedAtMilliseconds: 400,
            sourceObservationOrdinal: 10);

        Assert.Equal(4, store.Events.Count);
        Assert.Equal(4, store.Revision);
        Assert.True(store.TryGetPair(100, 200, out var pair));
        Assert.Equal(DamageModifiers.Critical | DamageModifiers.MultiHit | DamageModifiers.Evade | DamageModifiers.Invincible, pair!.Modifiers);
        Assert.Equal(3, pair.HitCount);
        Assert.Equal(6, pair.AttemptCount);
        Assert.Equal(1, pair.EvadeCount);
        Assert.Equal(2, pair.InvincibleCount);
        Assert.Equal(2, pair.MultiHitCount);
        Assert.Equal(11, pair.MultiHitSubCount);
        Assert.Equal(1_004, pair.LastSkillCode);
        Assert.Equal(100, pair.FirstObserved);
        Assert.Equal(400, pair.LastObserved);

        Assert.True(store.TryGetCombatant(100, out var source));
        Assert.Equal(3, source!.OutgoingHits);
        Assert.Equal(6, source.OutgoingAttempts);
        Assert.Equal(1, source.OutgoingEvades);
        Assert.Equal(2, source.OutgoingInvincibles);
        Assert.Equal(2, source.OutgoingMultiHits);

        Assert.True(store.TryGetCombatant(200, out var target));
        Assert.Equal(3, target!.IncomingHits);
        Assert.Equal(6, target.IncomingAttempts);
        Assert.Equal(1, target.IncomingEvades);
        Assert.Equal(2, target.IncomingInvincibles);
        Assert.Equal(2, target.IncomingMultiHits);
    }

    private static void Apply(
        MechanicStore store,
        int skillCode,
        DamageModifiers modifiers,
        int hitCount,
        int attemptCount,
        int evadeCount,
        int invincibleCount,
        int multiHitCount,
        int multiHitSubCount,
        long observedAtMilliseconds,
        long sourceObservationOrdinal)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            HitCount = hitCount,
            AttemptCount = attemptCount,
            MultiHitCount = multiHitSubCount,
            Modifiers = modifiers
        };
        var mechanic = new CombatMechanicOccurrence(
            modifiers,
            hitCount,
            attemptCount,
            evadeCount,
            invincibleCount,
            multiHitCount,
            multiHitSubCount,
            CombatResolutionTrace.FromPacket(CombatPacketRule.DirectValue, default, default));
        var raw = new RawPacketReference(0x0438, 64, sourceObservationOrdinal);

        store.Apply(
            sourceId: 100,
            targetId: 200,
            in observation,
            in mechanic,
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw);
    }
}
