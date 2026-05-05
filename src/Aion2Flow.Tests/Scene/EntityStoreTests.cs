using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public class EntityStoreTests
{
    [Fact]
    public void EntityStore_ApplyNpcCode_CreatesEntity()
    {
        var store = new EntityStore();
        store.ApplyNpcCode(1234, 2310108);

        Assert.True(store.TryGet(1234, out var entity));
        Assert.Equal(2310108, entity!.NpcCode);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void EntityStore_ApplyNickname_MarksAsPlayer()
    {
        var store = new EntityStore();
        store.ApplyNickname(2007, "Perigee");

        Assert.True(store.TryGet(2007, out var entity));
        Assert.True(entity!.IsPlayer);
    }

    [Fact]
    public void EntityStore_ApplySummon_SetsOwnerAndKind()
    {
        var store = new EntityStore();
        store.ApplySummon(314, 17755);

        Assert.True(store.TryGet(17755, out var entity));
        Assert.Equal(314, entity!.OwnerEntityId);
        Assert.Equal(NpcKind.Summon, entity.Kind);
    }

    [Fact]
    public void EntityStore_ApplyNpcHp_UpdatesHpFields()
    {
        var store = new EntityStore();
        store.ApplyNpcHp(56688, 22847, 9000000);

        Assert.True(store.TryGet(56688, out var entity));
        Assert.Equal(22847, entity!.CurrentHp);
        Assert.Equal(9000000, entity.MaxHp);
    }

    [Fact]
    public void EntityStore_IsKnownEntity_ReturnsTrueForExisting()
    {
        var store = new EntityStore();
        store.ApplyNpcCode(100, 2000001);

        Assert.True(store.IsKnownEntity(100));
        Assert.False(store.IsKnownEntity(999));
    }

    [Fact]
    public void MetadataStore_ApplyNpcName_LookupWorks()
    {
        var store = new MetadataStore();
        store.ApplyNpcName(2310108, "Nazarak");

        Assert.True(store.TryGetNpcName(2310108, out var name));
        Assert.Equal("Nazarak", name);
    }

    [Fact]
    public void MetadataStore_ApplyDisplayName_LookupWorks()
    {
        var store = new MetadataStore();
        store.ApplyDisplayName(2007, "Perigee");

        Assert.True(store.TryGetDisplayName(2007, out var name));
        Assert.Equal("Perigee", name);
    }
}

public class DomainEventApplierTests
{
    [Fact]
    public void Applier_ApplyJournal_PopulatesEntityStore()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 56688,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 56688, StateCode = 2310108 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 2007,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 2007, StateCode = 0 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(2, entities.Count);
        Assert.True(entities.IsKnownEntity(56688));
        Assert.True(entities.IsKnownEntity(2007));
    }

    [Fact]
    public void Applier_ResourceObservation_UpdatesNpcHp()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 56688,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 56688, StateCode = 2310108 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = 56688,
            TargetEntityId = 0,
            Resource = new ResourceObservation { EntityId = 56688, CurrentValue = 22847, MaximumValue = 9000000 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.TryGet(56688, out var entity));
        Assert.Equal(22847, entity!.CurrentHp);
        Assert.Equal(9000000, entity.MaxHp);
    }

    [Fact]
    public void Applier_SummonObservation_SetsOwnerAndKind()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 314,
            TargetEntityId = 17755,
            State = new StateObservation { EntityId = 17755, StateCode = 0, Value0 = 314 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.TryGet(17755, out var entity));
        Assert.Equal(314, entity!.OwnerEntityId);
        Assert.Equal(NpcKind.Summon, entity.Kind);
    }

    [Fact]
    public void Applier_EmptyJournal_DoesNothing()
    {
        var journal = new ObservedEventJournal();
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(0, entities.Count);
    }

    [Fact]
    public void Applier_VendoredReplay_PopulatesEntities()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.Count > 0, $"Expected entities from journal with {journal.Count} entries");
    }
}

public class CombatStoreTests
{
    [Fact]
    public void CombatStore_ApplyCombat_CreatesPairAndCombatant()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1234);

        Assert.True(store.TryGetPair(100, 200, out var pair));
        Assert.Equal(500, pair!.TotalDamage);
        Assert.Equal(1, pair.HitCount);

        Assert.True(store.TryGetCombatant(100, out var source));
        Assert.Equal(500, source!.OutgoingDamage);

        Assert.True(store.TryGetCombatant(200, out var target));
        Assert.Equal(500, target!.IncomingDamage);
    }

    [Fact]
    public void CombatStore_ApplyCombat_AccumulatesMultipleHits()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 300, 1, 1, 1000);
        store.ApplyCombat(100, 200, 700, 1, 1, 1000);

        Assert.True(store.TryGetPair(100, 200, out var pair));
        Assert.Equal(1000, pair!.TotalDamage);
        Assert.Equal(2, pair.HitCount);

        Assert.True(store.TryGetCombatant(100, out var source));
        Assert.Equal(1000, source!.OutgoingDamage);
        Assert.Equal(2, source.OutgoingHits);
    }

    [Fact]
    public void CombatStore_OutgoingAndIncomingIndexes()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);

        var outgoing = store.GetOutgoingPairs(100);
        Assert.Equal(2, outgoing.Count);

        var incoming200 = store.GetIncomingPairs(200);
        Assert.Single(incoming200);

        var incoming300 = store.GetIncomingPairs(300);
        Assert.Single(incoming300);
    }

    [Fact]
    public void CombatStore_Revision_IncrementsOnEachApply()
    {
        var store = new CombatStore();
        Assert.Equal(0, store.Revision);

        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        Assert.Equal(1, store.Revision);

        store.ApplyCombat(100, 200, 300, 1, 1, 1000);
        Assert.Equal(2, store.Revision);
    }

    [Fact]
    public void DomainEventApplier_CombatObservation_PopulatesCombatStore()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 100,
            TargetEntityId = 200,
            Combat = new CombatObservation { SkillCode = 1000, Damage = 500, HitCount = 1, AttemptCount = 1 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);

        applier.ApplyJournal(journal);

        Assert.True(combat.TryGetPair(100, 200, out var pair));
        Assert.Equal(500, pair!.TotalDamage);
        Assert.Equal(1, combat.Revision);
    }
}
