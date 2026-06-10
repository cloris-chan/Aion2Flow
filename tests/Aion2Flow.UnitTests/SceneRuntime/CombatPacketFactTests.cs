using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPacketFactTests
{
    [Fact]
    public void ScenePath_PreservesParserAuthoritativeMultiHitFact()
    {
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 10, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 8171,
            TargetEntityId = 42995,
            Combat = new CombatObservation
            {
                SkillCode = 17010230,
                Damage = 2400,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 5,
                MultiHitCount = 2,
                Modifiers = DamageModifiers.MultiHit,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8171, 42995, out var pair));
        Assert.Equal(1, pair!.MultiHitCount);
        Assert.True(combat.TryGetCombatant(8171, out var source));
        Assert.Equal(1, source!.OutgoingMultiHits);
    }

    [Fact]
    public void PeriodicNormalizer_PreservesParserAuthoritativeMultiHitCount()
    {
        var canonicalizer = new PeriodicPoolCanonicalizer();
        var observation = new CombatObservation
        {
            SkillCode = 17010230,
            Damage = 2400,
            HitCount = 1,
            AttemptCount = 1,
            Marker = 5,
            MultiHitCount = 2,
            Modifiers = DamageModifiers.MultiHit,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var results = canonicalizer.Normalize(8171, 42995, in observation);
        Assert.Equal(1, results.Count);
        var result = results[0];

        Assert.Equal(2, result.Observation.MultiHitCount);
        Assert.Equal(DamageModifiers.MultiHit, result.Observation.Modifiers & DamageModifiers.MultiHit);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }
}
