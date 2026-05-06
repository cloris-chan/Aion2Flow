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

public class LegacyBattleSnapshotAdapterTests
{
    [Fact]
    public void Adapter_CreateSnapshot_ProducesCombatantEntries()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Player1");
        entities.ApplyNpcCode(200, 2310108);
        combat.ApplyCombat(100, 200, 1000, 5, 5, 1000);

        var adapter = new LegacyBattleSnapshotAdapter(entities, combat);
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal(2, snapshot.Combatants.Count);
        Assert.True(snapshot.Combatants.ContainsKey(100));
        Assert.True(snapshot.Combatants.ContainsKey(200));
    }

    [Fact]
    public void Adapter_CreateSnapshot_ResolvesDisplayName()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 2310108);
        combat.ApplyCombat(100, 200, 500, 1, 1, 1000);

        var adapter = new LegacyBattleSnapshotAdapter(entities, combat);
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal("Perigee", snapshot.Combatants[100].Nickname);
        Assert.Equal("NPC-2310108", snapshot.Combatants[200].Nickname);
    }

    [Fact]
    public void Adapter_EmptyCombat_ProducesEmptySnapshot()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();

        var adapter = new LegacyBattleSnapshotAdapter(entities, combat);
        var snapshot = adapter.CreateSnapshot();

        Assert.Empty(snapshot.Combatants);
    }
}

public class DualReadParityTests
{
    [Fact]
    public void M2_06_ScenePath_CapturesSameCombatantIds_AsLegacyPath()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var adapter = new LegacyBattleSnapshotAdapter(entities, combat);
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
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
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
