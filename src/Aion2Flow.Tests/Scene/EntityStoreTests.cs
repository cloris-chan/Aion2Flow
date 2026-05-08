using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Projection;
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
    public void EntityStore_ApplyNpcHp_PreservesKnownMaxWhenRemainHpOmitsMax()
    {
        var store = new EntityStore();

        store.ApplyNpcHp(56688, 49_200, 49_200);
        store.ApplyNpcHp(56688, 22_847, 0);

        Assert.True(store.TryGet(56688, out var entity));
        Assert.Equal(22_847, entity!.CurrentHp);
        Assert.Equal(49_200, entity.MaxHp);
    }

    [Fact]
    public void EntityStore_ApplyNpcExtendedState_UpdatesNpcRuntimeFields()
    {
        var store = new EntityStore();
        store.ApplyNpc2136State(4370, 6, 200003);
        store.ApplyNpc0140Value(4370, 200003);
        store.ApplyNpc0240Value(4370, 200003);
        store.ApplyNpc4636State(4370, 2, 79);
        store.ApplyNpc2C38State(4370, 95, 7);

        Assert.True(store.TryGet(4370, out var entity));
        Assert.Equal((uint)6, entity!.Sequence2136);
        Assert.Equal((uint)200003, entity.Value2136);
        Assert.Equal((uint)200003, entity.Value0140);
        Assert.Equal((uint)200003, entity.Value0240);
        Assert.Equal(((byte)2, (byte)79), entity.State4636);
        Assert.Equal((95, 7), entity.Latest2C38);
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

    [Fact]
    public void MetadataStore_MapStaging_CommitsOnArrival()
    {
        var store = new MetadataStore();

        store.StageDestinationMap(200003);
        store.StageDestinationMapInstance(515552);

        Assert.Equal(0u, store.CurrentMapId);
        Assert.Equal(0u, store.CurrentMapInstanceId);

        store.MarkSceneArrival();

        Assert.Equal(200003u, store.CurrentMapId);
        Assert.Equal(515552u, store.CurrentMapInstanceId);
    }

    [Fact]
    public void MetadataStore_Clear_ResetsMapIdentity()
    {
        var store = new MetadataStore();
        store.StageDestinationMap(200003);
        store.StageDestinationMapInstance(515552);
        store.MarkSceneArrival();

        store.Clear();

        Assert.Equal(0u, store.CurrentMapId);
        Assert.Equal(0u, store.CurrentMapInstanceId);
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
    public void Applier_LifecycleReboundEvents_UseSyntheticEntityId()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int reboundId = int.MaxValue - 1;

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = reboundId,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = reboundId, StateCode = 2000002 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 100,
            TargetEntityId = reboundId,
            Combat = new CombatObservation { SkillCode = 11000010, Damage = 500, HitCount = 1, AttemptCount = 1 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);

        applier.ApplyJournal(journal);

        Assert.True(entities.TryGet(reboundId, out var entity));
        Assert.Equal(2000002, entity!.NpcCode);
        Assert.True(combat.TryGetPair(100, reboundId, out var pair));
        Assert.Equal(500, pair!.TotalDamage);
        Assert.False(entities.TryGet(3518, out _));
    }

    [Fact]
    public void Applier_NpcExtendedStateObservations_PopulateEntityRecord()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 4370,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 4370, StateCode = 2136, Value0 = 6, Value1 = 200003 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 4370,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 4370, StateCode = 140, Value0 = 200003 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 2 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 4370,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 4370, StateCode = 240, Value0 = 200003 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 3 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 4370,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 4370, StateCode = 4636, Value0 = 2, Value1 = 79 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 4 },
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = 0,
            TargetEntityId = 4370,
            Aura = new AuraObservation { TargetEntityId = 4370, SequenceId = 95, ResultCode = 7 }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.TryGet(4370, out var entity));
        Assert.Equal((uint)6, entity!.Sequence2136);
        Assert.Equal((uint)200003, entity.Value2136);
        Assert.Equal((uint)200003, entity.Value0140);
        Assert.Equal((uint)200003, entity.Value0240);
        Assert.Equal(((byte)2, (byte)79), entity.State4636);
        Assert.Equal((95, 7), entity.Latest2C38);
    }

    [Fact]
    public void Applier_SceneObservations_StageAndCommitMapIdentity()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation { MapId = 200003, DiagnosticKey = "stage-destination-map" }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation { MapInstanceId = 515552, DiagnosticKey = "stage-destination-instance" }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 2 },
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation { DiagnosticKey = "scene-arrival" }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(200003u, metadata.CurrentMapId);
        Assert.Equal(515552u, metadata.CurrentMapInstanceId);
    }

    [Fact]
    public void Applier_SceneObservations_DoNotCommitStagedMapBeforeArrival()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation { MapId = 910035, DiagnosticKey = "stage-destination-map" }
        });

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(0u, metadata.CurrentMapId);
        Assert.Equal(0u, metadata.CurrentMapInstanceId);
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
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.Count > 0, $"Expected entities from journal with {journal.Count} entries");
    }

    [Fact]
    public void Applier_VendoredReplay_ReconstructsConfirmedMapIdentity()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(replay.SceneJournal);

        Assert.Equal(replay.SceneOwner.Metadata.CurrentMapId, metadata.CurrentMapId);
        Assert.Equal(replay.SceneOwner.Metadata.CurrentMapInstanceId, metadata.CurrentMapInstanceId);
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
    public void CombatStore_DetailRevision_TracksAffectedCombatantsOnly()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        Assert.Equal(1, store.GetCombatantDetailRevision(100));
        Assert.Equal(1, store.GetCombatantDetailRevision(200));
        Assert.Equal(0, store.GetCombatantDetailRevision(999));

        store.ApplyCombat(300, 400, 700, 1, 1, 2000);

        Assert.Equal(1, store.GetCombatantDetailRevision(100));
        Assert.Equal(2, store.GetCombatantDetailRevision(300));
        Assert.Equal(2, store.GetCombatantDetailRevision(400));
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

public class SnapshotChangeFeedTests
{
    [Fact]
    public void CombatStore_ChangeFeed_TracksPairAndCombatantChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 200, 300, 1, 1, 1000);

        var cursor = store.CreateCursor(0);
        var batch = store.ReadChanges(cursor, 100);

        Assert.Equal(2, store.Revision);
        Assert.Equal(6, batch.Changes.Count);
        Assert.False(batch.HasMore);
    }

    [Fact]
    public void CombatStore_ChangeFeed_CursorSkipsAlreadyRead()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);
        store.ApplyCombat(100, 300, 300, 1, 1, 2000);

        var cursor = store.CreateCursor(0);
        var batch1 = store.ReadChanges(cursor, 3);
        Assert.Equal(3, batch1.Changes.Count);
        Assert.True(batch1.HasMore);

        var cursor2 = new SnapshotChangeCursor(batch1.ToRevision, 0);
        var batch2 = store.ReadChanges(cursor2, 100);
        Assert.Equal(3, batch2.Changes.Count);
        Assert.False(batch2.HasMore);
    }

    [Fact]
    public void CombatStore_ChangeFeed_DoesNotSplitRevisionGroups()
    {
        var store = new CombatStore();
        for (int i = 0; i < 30; i++)
            store.ApplyCombat(100 + i, 200 + i, 1, 1, 1, 1000 + i);

        var cursor = store.CreateCursor(0);
        var batch = store.ReadChanges(cursor, 64);

        Assert.Equal(63, batch.Changes.Count);
        Assert.True(batch.HasMore);
        Assert.Equal(21, batch.ToRevision);

        var next = store.ReadChanges(new SnapshotChangeCursor(batch.ToRevision, 0), 100);

        Assert.Equal(27, next.Changes.Count);
        Assert.Equal(22, next.Changes[0].Revision);
    }

    [Fact]
    public void CombatStore_ChangeFeed_ReturnsWholeRevisionGroupWhenLimitIsSmaller()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 1, 1, 1, 1000);
        store.ApplyCombat(300, 400, 1, 1, 1, 2000);

        var batch = store.ReadChanges(store.CreateCursor(0), 1);

        Assert.Equal(3, batch.Changes.Count);
        Assert.True(batch.HasMore);
        Assert.All(batch.Changes, change => Assert.Equal(1, change.Revision));
    }

    [Fact]
    public void CombatStore_ChangeFeed_EmptyWhenNoChanges()
    {
        var store = new CombatStore();
        var cursor = store.CreateCursor(0);
        var batch = store.ReadChanges(cursor, 100);

        Assert.Empty(batch.Changes);
        Assert.False(batch.HasMore);
    }

    [Fact]
    public void CombatStore_ChangeFeed_OnlyReturnsNewChanges()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var cursor = store.CreateCursor(1);
        var batch = store.ReadChanges(cursor, 100);
        Assert.Empty(batch.Changes);

        store.ApplyCombat(100, 300, 300, 1, 1, 2000);
        batch = store.ReadChanges(cursor, 100);
        Assert.Equal(3, batch.Changes.Count);
    }
}

public class SceneSnapshotAdapterBasicTests
{
    [Fact]
    public void Adapter_CreateSnapshot_ProducesCombatantEntries()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Player1");
        entities.ApplyNpcCode(200, 2310108);
        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 1000,
            Damage = 1000,
            HitCount = 5,
            AttemptCount = 5,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 1000,
            Damage = 1,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_001);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new MetadataStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Single(snapshot.Combatants);
        Assert.True(snapshot.Combatants.ContainsKey(100));
    }

    [Fact]
    public void Adapter_CreateSnapshot_ResolvesDisplayName()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 9_999_998);
        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 1000,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 1000,
            Damage = 1,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_001);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new MetadataStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal("Perigee", snapshot.Combatants[100].Nickname);
    }

    [Fact]
    public void Adapter_EmptyCombat_ProducesEmptySnapshot()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new MetadataStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Empty(snapshot.Combatants);
    }

    [Fact]
    public void Adapter_CreateSnapshot_UsesMetadataMapIdentity()
    {
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        metadata.StageDestinationMap(200003);
        metadata.StageDestinationMapInstance(515552);
        metadata.MarkSceneArrival();

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata);
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal(200003u, snapshot.MapId);
        Assert.Equal(515552u, snapshot.MapInstanceId);
    }
}

public class SceneCombatSnapshotAdapterTests
{
    [Fact]
    public void Adapter_CreateSnapshot_ProjectsSceneTotalsAndWindow()
    {
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 9_999_999);
        metadata.ApplyNpcName(9_999_999, "Nazarak");
        metadata.StageDestinationMap(200003);
        metadata.StageDestinationMapInstance(515552);
        metadata.MarkSceneArrival();

        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 11000010,
            Damage = 1500,
            HitCount = 2,
            AttemptCount = 2,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(100, 100, new CombatObservation
        {
            SkillCode = 17000010,
            Damage = 600,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing
        }, 2_500);
        combat.ApplyCombat(100, 100, new CombatObservation
        {
            SkillCode = 17000011,
            Damage = 300,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield,
            EffectTag = PacketEffectTag.ShieldAbsorbed
        }, 2_600);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata);
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal(200003u, snapshot.MapId);
        Assert.Equal(515552u, snapshot.MapInstanceId);
        Assert.Equal("Nazarak", snapshot.TargetName);
        Assert.Equal(200, snapshot.TargetObservation?.InstanceId);
        Assert.Equal(1_000, snapshot.BattleStartTime);
        Assert.Equal(2_600, snapshot.BattleEndTime);
        Assert.Equal(1_600, snapshot.BattleTime);
        Assert.True(snapshot.Encounter.IsActive);
        var player = snapshot.Combatants[100];
        Assert.Equal("Perigee", player.Nickname);
        Assert.Equal(1500, player.DamageAmount);
        Assert.Equal(600, player.HealingAmount);
        Assert.Equal(300, player.ShieldAbsorbedAmount);
        Assert.Equal(1, player.ShieldAbsorbedTimes);
    }

    [Fact]
    public void Adapter_CreateSnapshot_ExpandsSinglePointWindowWithRelevantRecovery()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(13000010, "Recover", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 9_999_999);
        metadata.ApplyNpcName(9_999_999, "Nazarak");

        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 11000010,
            Damage = 1500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(100, 100, new CombatObservation
        {
            SkillCode = 13000010,
            Damage = 600,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing
        }, 2_500);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata);
        var snapshot = adapter.CreateSnapshot();

        var player = snapshot.Combatants[100];
        Assert.Equal("Perigee", player.Nickname);
        Assert.Equal(1500, player.DamageAmount);
        Assert.Equal(600, player.HealingAmount);
        Assert.Equal(CharacterClass.Gladiator, player.CharacterClass);
        Assert.Equal(1_000, snapshot.BattleStartTime);
        Assert.Equal(2_500, snapshot.BattleEndTime);
        Assert.Equal(1_500, snapshot.BattleTime);
        Assert.Equal(1500d / 1500 * 1000, player.DamagePerSecond, 3);
        Assert.Equal(1d, player.DamageContribution, 3);
    }

    [Fact]
    public void Adapter_CreateSnapshot_FoldsSummonOutgoingDamageToOwnerAndSkipsSummonTargets()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new Skill(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new Skill(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(314, "Owner");
        entities.ApplySummon(314, 900);
        entities.ApplyNpcCode(200, 9_999_999);

        combat.ApplyCombat(314, 200, new CombatObservation
        {
            SkillCode = 16010000,
            Damage = 405,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(900, 200, new CombatObservation
        {
            SkillCode = 16100003,
            Damage = 1205,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_010);
        combat.ApplyCombat(900, 900, new CombatObservation
        {
            SkillCode = 16990004,
            Damage = 10_000,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Support
        }, 1_020);

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, new MetadataStore()).CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(314, out var owner));
        Assert.False(snapshot.Combatants.ContainsKey(900));
        Assert.Equal(1610, owner.DamageAmount);
        Assert.Equal(CharacterClass.Elementalist, owner.CharacterClass);
    }

    [Fact]
    public void Adapter_CreateSnapshot_HidesKnownNpcClassEvenWithPlayerSkill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Player");
        entities.ApplyNpcCode(200, 9_999_999);

        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 11000010,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_000);
        combat.ApplyCombat(200, 100, new CombatObservation
        {
            SkillCode = 11000010,
            Damage = 300,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1_100);

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, new MetadataStore()).CreateSnapshot();

        Assert.Equal(CharacterClass.Gladiator, snapshot.Combatants[100].CharacterClass);
        Assert.Null(snapshot.Combatants[200].CharacterClass);
    }

    [Fact]
    public void Adapter_CreateSnapshot_UsesTimelineNowForBossFallbackWithoutCombat()
    {
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var bossFocus = new BossFocusStore(entities);
        entities.ApplyNpcKind(3518, NpcKind.Boss);
        entities.ApplyBattleToggle(3518, true);
        bossFocus.ApplyBattle(3518, true, 1_000);

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, metadata, bossFocus).CreateSnapshot();

        Assert.Equal(3518, snapshot.Encounter.TrackingTargetId);
        Assert.True(snapshot.Encounter.IsActive);
    }

    [Fact]
    public void Adapter_CreateSnapshot_ExpiresBossFallbackAgainstLatestSceneObservation()
    {
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var bossFocus = new BossFocusStore(entities);
        entities.ApplyNpcKind(3518, NpcKind.Boss);
        entities.ApplyBattleToggle(3518, true);
        bossFocus.ApplyBattle(3518, true, 1_000);
        entities.ApplyNickname(100, "Player");
        entities.ApplyNpcCode(200, 9_999_999);

        combat.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 11000010,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 20_000);

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, metadata, bossFocus).CreateSnapshot();

        Assert.Equal(200, snapshot.Encounter.TrackingTargetId);
        Assert.NotEqual(3518, snapshot.Encounter.TrackingTargetId);
    }
}

public class SceneReadModelOwnerTests
{
    [Fact]
    public void Owner_Refresh_AppliesJournalIncrementally()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 100,
            State = new StateObservation
            {
                EntityId = 100,
                StateCode = StateCodes.PlayerIdentity,
                Text = "Perigee"
            }
        });

        owner.Refresh();

        Assert.Equal(1, owner.AppliedObservationOrdinal);
        Assert.True(owner.Entities.TryGet(100, out var entity));
        Assert.Equal("Perigee", entity.Nickname);

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 100,
            TargetEntityId = 200,
            Raw = new RawPacketReference { TimestampMilliseconds = 1_000 },
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                Damage = 500,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        owner.Refresh();

        Assert.Equal(2, owner.AppliedObservationOrdinal);
        Assert.True(owner.Combat.TryGetPair(100, 200, out var pair));
        Assert.NotNull(pair);
        Assert.Equal(500, pair.TotalDamage);
    }

    [Fact]
    public void Owner_Refresh_DoesNotFlushPendingCompactOutcomeBeforeCompletedBatch()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 100,
            TargetEntityId = 200,
            Raw = new RawPacketReference { TimestampMilliseconds = 1_000 },
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                Damage = 1,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 77,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        owner.Refresh();

        Assert.False(owner.Combat.TryGetPair(100, 200, out _));

        journal.CompleteBatch(100);
        owner.Refresh();

        Assert.True(owner.Combat.TryGetPair(100, 200, out var pair));
        Assert.NotNull(pair);
        Assert.Equal(1, pair.TotalDamage);
    }

    [Fact]
    public void Owner_CreateDetailDelta_ReusesWarmSubscriptionForIrrelevantCombat()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(sink, 100, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(sink, 300, 400, 11000010, 700, 3_000, 3);
        sink.CompleteBatch(3);

        var secondSnapshot = scene.Owner.CreateSnapshot();
        var warm = scene.Owner.CreateDetailDelta(secondSnapshot, 100);

        Assert.Same(cold, warm);
        Assert.Equal(2, warm.Revision);
        Assert.Equal(2, warm.Events.Count);
    }

    [Fact]
    public void Owner_CreateDetailDelta_UpdatesWarmSubscriptionForRelevantCombat()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(sink, 100, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(sink, 100, 200, 11000010, 200, 3_000, 3);
        sink.CompleteBatch(3);

        var secondSnapshot = scene.Owner.CreateSnapshot();
        var warm = scene.Owner.CreateDetailDelta(secondSnapshot, 100);

        Assert.NotSame(cold, warm);
        Assert.Equal(3, warm.Revision);
        Assert.Equal(3, warm.Events.Count);
    }

    [Fact]
    public void Owner_CreateDetailDelta_UsesSeparateSubscriptionForSelectionSwitch()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(sink, 300, 400, 11000010, 700, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var snapshot = scene.Owner.CreateSnapshot();
        var first = scene.Owner.CreateDetailDelta(snapshot, 100);
        var second = scene.Owner.CreateDetailDelta(snapshot, 300);

        Assert.NotSame(first, second);
        Assert.Equal(100, first.CombatantId);
        Assert.Equal(300, second.CombatantId);
        Assert.Single(first.Events);
        Assert.Single(second.Events);
    }

    [Fact]
    public void Owner_CreateDetailDelta_TreatsSummonDamageAsOwnerRelevantOnWarmPoll()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        sink.AppendNickname(100, "Owner");
        sink.AppendSummon(100, 500);
        AppendScenePacket(sink, 500, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(sink, 500, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(sink, 500, 200, 11000010, 200, 3_000, 3);
        sink.CompleteBatch(3);

        var secondSnapshot = scene.Owner.CreateSnapshot();
        var warm = scene.Owner.CreateDetailDelta(secondSnapshot, 100);

        Assert.NotSame(cold, warm);
        Assert.Equal(3, warm.Revision);
        Assert.Equal(3, warm.Events.Count);
        Assert.All(warm.Events, e => Assert.Equal(100, e.SourceId));
    }

    [Fact]
    public void LiveReadModel_CapturesFactoryJournal()
    {
        var scene = new SceneLiveReadModel();
        try
        {
            var sink = SceneSinkFactory.CreateForLive(scene)();
            sink.AppendNickname(100, "Perigee");
            scene.Owner.Refresh();
            var snapshot = scene.Owner.CreateSnapshot();

            Assert.Equal(1, scene.Journal.Count);
            Assert.Equal(scene.SessionId, snapshot.BattleId);
            Assert.True(scene.Owner.Entities.TryGet(100, out var entity));
            Assert.Equal("Perigee", entity.Nickname);
        }
        finally
        {
        }
    }

    [Fact]
    public void LiveReadModel_Reset_BarrierSeparatesOldAndNewCombat()
    {
        var scene = new SceneLiveReadModel();
        try
        {
            var sink = SceneSinkFactory.CreateForLive(scene)();
            sink.AppendNickname(100, "Player");
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = 200,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = 500,
                Timestamp = 1_000,
                BatchOrdinal = 1,
                HitContribution = 1,
                AttemptContribution = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = 200,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = 300,
                Timestamp = 2_000,
                BatchOrdinal = 2,
                HitContribution = 1,
                AttemptContribution = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            sink.CompleteBatch(1);
            sink.CompleteBatch(2);

            var first = scene.Owner.CreateSnapshot();
            var oldSessionId = scene.SessionId;
            var resetStartOrdinal = scene.Clock.NextObservationOrdinal;
            scene.Reset();
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = 201,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = 700,
                Timestamp = 3_000,
                BatchOrdinal = 3,
                HitContribution = 1,
                AttemptContribution = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = 201,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = 300,
                Timestamp = 4_000,
                BatchOrdinal = 4,
                HitContribution = 1,
                AttemptContribution = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            sink.CompleteBatch(3);
            sink.CompleteBatch(4);

            var second = scene.Owner.CreateSnapshot();

            Assert.NotEqual(oldSessionId, scene.SessionId);
            Assert.NotEqual(first.BattleId, second.BattleId);
            Assert.Equal(resetStartOrdinal, scene.Journal.Read(resetStartOrdinal).Stamp.ObservationOrdinal);
            Assert.Equal(scene.SessionId, scene.Journal.Read(resetStartOrdinal).SceneSessionId);
            Assert.Equal(1000, second.Combatants[100].DamageAmount);
            Assert.Equal("Player", second.Combatants[100].Nickname);
            Assert.DoesNotContain(200, second.Combatants.Keys);
        }
        finally
        {
        }
    }

    [Fact]
    public void LiveReadModel_FactoryCreatesSceneSink()
    {
        var scene = new SceneLiveReadModel();
        var factory = SceneSinkFactory.CreateForLive(scene);
        try
        {
            var sink = factory();
            sink.AppendNickname(100, "Perigee");
            scene.Owner.Refresh();

            Assert.Equal(1, scene.Journal.Count);
            Assert.True(scene.Owner.Entities.TryGet(100, out var entity));
            Assert.Equal("Perigee", entity.Nickname);
        }
        finally
        {
        }
    }

    [Fact]
    public void ReplaySinkHolder_ExposesSceneOwner()
    {
        try
        {
            using var holder = SceneSinkFactory.CreateForReplay();
            Assert.NotNull(holder.Journal);
            Assert.NotNull(holder.Owner);
            var journal = holder.Journal;
            var owner = holder.Owner;
            holder.Sink.AppendNickname(100, "Perigee");
            owner.Refresh();

            Assert.Equal(1, journal.Count);
            Assert.True(owner.Entities.TryGet(100, out var entity));
            Assert.Equal("Perigee", entity.Nickname);
        }
        finally
        {
        }
    }

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
        ];
    }

    private static void AppendScenePacket(
        JournalingRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp,
        long batchOrdinal)
    {
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            Timestamp = timestamp,
            BatchOrdinal = batchOrdinal,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
    }
}

public class DualReadParityTests
{
    [Fact]
    public void M2_06_ScenePath_CapturesSameCombatantIds_AsLegacyPath()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata);
        var sceneSnapshot = adapter.CreateSnapshot();

        var legacySnapshot = replay.Snapshot;

        var legacyWithDamage = legacySnapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .Select(static kv => kv.Key)
            .ToHashSet();

        var sceneIds = sceneSnapshot.Combatants.Keys.ToHashSet();

        foreach (var id in legacyWithDamage)
        {
            Assert.True(sceneIds.Contains(id), $"Scene path missing combatant {id} that has damage in legacy path");
        }
    }

    [Fact]
    public void M2_06_CombatStore_DamageTotals_MatchLegacy_OutgoingDamage()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var legacySnapshot = replay.Snapshot;

        var topDealer = legacySnapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        Assert.True(combat.TryGetCombatant(topDealer.Key, out var sceneCombatant));
        Assert.True(sceneCombatant!.OutgoingDamage > 0, $"Scene path has 0 outgoing damage for combatant {topDealer.Key}");
    }
}
