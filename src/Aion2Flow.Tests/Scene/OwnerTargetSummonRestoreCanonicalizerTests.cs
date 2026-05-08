using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.Scene;

public sealed class OwnerTargetSummonRestoreCanonicalizerTests
{
    [Fact]
    public void ScenePath_TreatsOwnerTargetWindSpiritRestoreAsHealing()
    {
        const int ownerId = 4086;
        const int summonId = 38013;
        var entities = new EntityStore();
        entities.ApplySummon(ownerId, summonId);
        var canonicalizer = new OwnerTargetSummonRestoreCanonicalizer(entities);
        var observation = new CombatObservation
        {
            SkillCode = 16990003,
            OriginalSkillCode = 16990003,
            Damage = 114,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var result = canonicalizer.Normalize(summonId, ownerId, in observation);

        Assert.Equal(CombatEventKind.Healing, result.Observation.EventKind);
        Assert.Equal(CombatValueKind.Healing, result.Observation.ValueKind);
        Assert.Equal(114, result.Observation.Damage);
    }

    [Fact]
    public void ScenePath_KeepsWindSpiritRestoreAsDamageWithoutSummonOwnerContext()
    {
        const int ownerId = 4086;
        const int summonId = 38013;
        var canonicalizer = new OwnerTargetSummonRestoreCanonicalizer(new EntityStore());
        var observation = new CombatObservation
        {
            SkillCode = 16990003,
            OriginalSkillCode = 16990003,
            Damage = 114,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var result = canonicalizer.Normalize(summonId, ownerId, in observation);

        Assert.Equal(CombatEventKind.Damage, result.Observation.EventKind);
        Assert.Equal(CombatValueKind.Damage, result.Observation.ValueKind);
    }

    [Fact]
    public void ScenePath_KeepsOtherOwnerTargetSummonSkillAsDamage()
    {
        const int ownerId = 4086;
        const int summonId = 38013;
        var entities = new EntityStore();
        entities.ApplySummon(ownerId, summonId);
        var canonicalizer = new OwnerTargetSummonRestoreCanonicalizer(entities);
        var observation = new CombatObservation
        {
            SkillCode = 16990004,
            OriginalSkillCode = 16990004,
            Damage = 114,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var result = canonicalizer.Normalize(summonId, ownerId, in observation);

        Assert.Equal(CombatEventKind.Damage, result.Observation.EventKind);
        Assert.Equal(CombatValueKind.Damage, result.Observation.ValueKind);
    }

    [Fact]
    public void ScenePath_DomainApplyStoresOwnerTargetWindSpiritRestoreAsHealing()
    {
        const int ownerId = 4086;
        const int summonId = 38013;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            TargetEntityId = summonId,
            State = new StateObservation { EntityId = summonId, StateCode = 0 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = summonId,
            TargetEntityId = ownerId,
            Combat = new CombatObservation
            {
                SkillCode = 16990003,
                OriginalSkillCode = 16990003,
                Damage = 114,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(summonId, out var summon));
        Assert.Equal(114, summon!.OutgoingHealing);
        Assert.Equal(0, summon.OutgoingDamage);
        Assert.True(combat.TryGetCombatant(ownerId, out var owner));
        Assert.Equal(114, owner!.IncomingHealing);
        Assert.Equal(0, owner.IncomingDamage);
    }

    [Fact]
    public void ScenePath_OwnerTargetWindSpiritRestoreProjectionStoresHealingOnly()
    {
        const int ownerId = 4086;
        const int summonId = 38013;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            TargetEntityId = summonId,
            State = new StateObservation { EntityId = summonId, StateCode = 0 }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = summonId,
            TargetEntityId = ownerId,
            Combat = new CombatObservation
            {
                SkillCode = 16990003,
                OriginalSkillCode = 16990003,
                Damage = 114,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        var scene = Apply(journal);

        Assert.True(scene.TryGetCombatant(summonId, out var sceneSummon));
        Assert.Equal(114, sceneSummon!.OutgoingHealing);
        Assert.Equal(0, sceneSummon.OutgoingDamage);
        Assert.True(scene.TryGetCombatant(ownerId, out var sceneOwner));
        Assert.Equal(114, sceneOwner!.IncomingHealing);
        Assert.Equal(0, sceneOwner.IncomingDamage);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }
}
