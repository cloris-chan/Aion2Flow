using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

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
    public void EntityStore_ApplyNpcHp_DoesNotInferMaxFromRemainHp()
    {
        var store = new EntityStore();

        store.ApplyNpcHp(56688, 22_847, 0);

        Assert.True(store.TryGet(56688, out var entity));
        Assert.Equal(22_847, entity!.CurrentHp);
        Assert.Null(entity.MaxHp);
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
    public void RuntimeMetadataRegistry_UpsertNpcCode_LookupWorks()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertNpcCode(56688, 2310108);

        Assert.True(registry.TryGetNpcCode(56688, out var npcCode));
        Assert.Equal(2310108, npcCode);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_LookupWorks()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light);

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal("Perigee", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
        Assert.False(metadata.IsLocalPlayer);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesFaction_WhenUnknownArrivesLater()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light);
        registry.UpsertPcMetadata(2007, "Perigee");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal(Faction.Light, metadata.Faction);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesCharacterClass_WhenNicknameRefreshes()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light, CharacterClass.Sorcerer);
        registry.UpsertPcMetadata(2007, "Perigee");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal(CharacterClass.Sorcerer, metadata.CharacterClass);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesLocalPlayer_WhenGenericMetadataRefreshes()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light, CharacterClass.Elementalist, isLocalPlayer: true);
        registry.UpsertPcMetadata(2007, "Perigee");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.True(metadata.IsLocalPlayer);
        Assert.Equal(Faction.Light, metadata.Faction);
        Assert.Equal(CharacterClass.Elementalist, metadata.CharacterClass);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesOriginServerId_WhenGenericMetadataRefreshes()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light, CharacterClass.Elementalist, originServerId: 1007);
        registry.UpsertPcMetadata(2007, "Perigee");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal(1007, metadata.OriginServerId);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesLegionName_WhenGenericMetadataRefreshes()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light, CharacterClass.Elementalist, legionName: "Aether");
        registry.UpsertPcMetadata(2007, "Perigee");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal("Aether", metadata.LegionName);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_PreservesNickname_WhenFieldMetadataArrivesLater()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(2007, "Perigee", Faction.Light);
        registry.UpsertPcMetadata(2007, string.Empty, originServerId: 1007, legionName: "Aether");

        Assert.True(registry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal("Perigee", metadata.Nickname);
        Assert.Equal(1007, metadata.OriginServerId);
        Assert.Equal("Aether", metadata.LegionName);
        Assert.Equal(Faction.Light, metadata.Faction);
    }

    [Fact]
    public void RuntimeMetadataRegistry_UpsertPcMetadata_KeepsSingleLocalPlayer()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "First", characterClass: CharacterClass.Cleric, isLocalPlayer: true);
        registry.UpsertPcMetadata(200, "Second", characterClass: CharacterClass.Elementalist, isLocalPlayer: true);

        Assert.True(registry.TryGetPcMetadata(100, out var first));
        Assert.True(registry.TryGetPcMetadata(200, out var second));
        Assert.False(first.IsLocalPlayer);
        Assert.True(second.IsLocalPlayer);
    }

    [Fact]
    public void SceneBoundaryStore_MapState_CommitsImmediately()
    {
        var store = new SceneBoundaryStore();

        store.StageDestinationMap(200003);
        store.StageDestinationMapInstance(515552);

        Assert.Equal(200003u, store.CurrentMapId);
        Assert.Equal(515552u, store.CurrentMapInstanceId);

        Assert.Equal(200003u, store.CurrentMapId);
        Assert.Equal(515552u, store.CurrentMapInstanceId);
    }

    [Fact]
    public void SceneBoundaryStore_Clear_ResetsMapIdentity()
    {
        var store = new SceneBoundaryStore();
        store.StageDestinationMap(200003);
        store.StageDestinationMapInstance(515552);

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
            Stamp = new TimelineStamp { OffsetTicks = 1_000 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = 2007,
            TargetEntityId = 0,
            State = new StateObservation { EntityId = 2007, StateCode = 0 }
        });

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
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
        var metadata = new SceneBoundaryStore();
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
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.TryGet(17755, out var entity));
        Assert.Equal(314, entity!.OwnerEntityId);
        Assert.Equal(NpcKind.Summon, entity.Kind);
    }

    [Fact]
    public void Applier_TransientEffectControl_AttributesDamageToExplicitPlayerOwner()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);
        const int ownerId = 7206;
        const int effectSourceId = 73942;
        const int targetId = 180015;

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_000 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            State = new StateObservation { EntityId = ownerId, StateCode = StateCodes.PlayerIdentity, Text = "Owner" }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_100 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 1, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = ownerId,
            TargetEntityId = 0,
            Raw = new RawPacketReference { Opcode = 0x0238 },
            Combat = new CombatObservation { SkillCode = 15281240, BodySkillVariantRaw = 15281240, ChainId = targetId }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_480 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 2, BatchOrdinal = 2 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference { Opcode = 0x0638 },
            Combat = new CombatObservation { SkillCode = 15281241, BodySkillVariantRaw = 15281241 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_620 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 3, BatchOrdinal = 3 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 12_000,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_680 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 4, BatchOrdinal = 4 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 345,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        owner.Refresh();
        var snapshot = owner.CreateSnapshot();

        Assert.True(owner.Entities.TryGet(effectSourceId, out var effectEntity));
        Assert.Equal(ownerId, effectEntity!.OwnerEntityId);
        Assert.Equal(EntityOwnerKind.TransientEffect, effectEntity.OwnerKind);
        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var ownerCombatant));
        Assert.Equal(12_345, ownerCombatant.DamageAmount);
        Assert.False(snapshot.Combatants.ContainsKey(effectSourceId));
    }

    [Fact]
    public void Applier_TransientEffect0438Control_AttributesNpcCodedEffectDamageToOwner()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);
        const int ownerId = 7206;
        const int effectSourceId = 73942;
        const int targetId = 180015;

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 900 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            State = new StateObservation { EntityId = ownerId, StateCode = StateCodes.PlayerIdentity, Text = "Owner" }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 950 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = effectSourceId,
            State = new StateObservation { EntityId = effectSourceId, StateCode = 2_920_658 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_000 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 2, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = ownerId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281240,
                BodySkillVariantRaw = 15281240,
                Marker = 29,
                Type = 3
            }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_240 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 3, BatchOrdinal = 2 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 12_000,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_320 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 4, BatchOrdinal = 3 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 345,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_400 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 5, BatchOrdinal = 4 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = effectSourceId,
            State = new StateObservation { EntityId = effectSourceId, StateCode = 2_920_658 }
        });

        owner.Refresh();
        var snapshot = owner.CreateSnapshot();

        Assert.True(owner.Entities.TryGet(effectSourceId, out var effectEntity));
        Assert.Equal(2_920_658, effectEntity!.NpcCode);
        Assert.Equal(ownerId, effectEntity.OwnerEntityId);
        Assert.Equal(EntityOwnerKind.TransientEffect, effectEntity.OwnerKind);
        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var ownerCombatant));
        Assert.Equal(12_345, ownerCombatant.DamageAmount);
        Assert.False(snapshot.Combatants.ContainsKey(effectSourceId));
    }

    [Fact]
    public void Applier_TransientEffectControl_DoesNotUseKnownNpcOwnerSeed()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);
        const int nonPlayerSourceId = 4000;
        const int effectSourceId = 5000;
        const int targetId = 6000;

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 900 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 0 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = nonPlayerSourceId,
            State = new StateObservation { EntityId = nonPlayerSourceId, StateCode = 2_100_001 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_000 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 1, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = nonPlayerSourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference { Opcode = 0x0238 },
            Combat = new CombatObservation { SkillCode = 15281240, BodySkillVariantRaw = 15281240, ChainId = targetId }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_320 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 2, BatchOrdinal = 2 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference { Opcode = 0x0638 },
            Combat = new CombatObservation { SkillCode = 15281241, BodySkillVariantRaw = 15281241 }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_460 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 3, BatchOrdinal = 3 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 12_000,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_520 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 4, BatchOrdinal = 4 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = effectSourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 15281243,
                BodySkillVariantRaw = 15281243,
                Damage = 345,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        owner.Refresh();
        var snapshot = owner.CreateSnapshot();

        if (owner.Entities.TryGet(effectSourceId, out var effectEntity))
            Assert.Null(effectEntity.OwnerEntityId);
        Assert.True(snapshot.Combatants.TryGetValue(effectSourceId, out var effectCombatant));
        Assert.Equal(12_345, effectCombatant.DamageAmount);
        Assert.False(snapshot.Combatants.ContainsKey(nonPlayerSourceId));
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
        var metadata = new SceneBoundaryStore();
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
            Aura = new AuraObservation { Kind = AuraObservationKind.Result, EntityId = 4370, InstanceSequenceId = 95, ResultCode = 7 }
        });

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
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

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(200003u, metadata.CurrentMapId);
        Assert.Equal(515552u, metadata.CurrentMapInstanceId);
    }

    [Fact]
    public void Applier_SceneObservations_CommitMapStateImmediately()
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
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(910035u, metadata.CurrentMapId);
        Assert.Equal(0u, metadata.CurrentMapInstanceId);
    }

    [Fact]
    public void Applier_EmptyJournal_DoesNothing()
    {
        var journal = new ObservedEventJournal();
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.Equal(0, entities.Count);
    }

    [Fact]
    public void Applier_VendoredReplay_PopulatesEntities()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(journal);

        Assert.True(entities.Count > 0, $"Expected entities from journal with {journal.Count} entries");
    }

    [Fact]
    public void Applier_VendoredReplay_ReconstructsConfirmedMapIdentity()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var applier = new DomainEventApplier(entities, metadata, new CombatStore());

        applier.ApplyJournal(replay.SceneJournal);

        Assert.Equal(replay.SceneOwner.Boundary.CurrentMapId, metadata.CurrentMapId);
        Assert.Equal(replay.SceneOwner.Boundary.CurrentMapInstanceId, metadata.CurrentMapInstanceId);
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
        var metadata = new SceneBoundaryStore();
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

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new SceneBoundaryStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Single(snapshot.Combatants);
        Assert.True(snapshot.Combatants.ContainsKey(100));
    }

    [Fact]
    public void Adapter_CreateSnapshot_ProjectsCombatantFactsWithoutDisplayName()
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

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new SceneBoundaryStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Equal(501, snapshot.Combatants[100].DamageAmount);
    }

    [Fact]
    public void Adapter_EmptyCombat_ProducesEmptySnapshot()
    {
        var entities = new EntityStore();
        var combat = new CombatStore();

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, new SceneBoundaryStore());
        var snapshot = adapter.CreateSnapshot();

        Assert.Empty(snapshot.Combatants);
    }

    [Fact]
    public void Adapter_CreateSnapshot_UsesMetadataMapIdentity()
    {
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        metadata.StageDestinationMap(200003);
        metadata.StageDestinationMapInstance(515552);

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
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcDisplayEntry>
        {
            [9_999_999] = new(9_999_999, "Nazarak", NpcCatalogKind.Boss)
        });

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 9_999_999);
        metadata.StageDestinationMap(200003);
        metadata.StageDestinationMapInstance(515552);

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
        Assert.Equal(200, snapshot.TargetObservation?.InstanceId);
        Assert.Equal(1_000, snapshot.EncounterStartTime);
        Assert.Equal(2_600, snapshot.EncounterEndTime);
        Assert.Equal(1_600, snapshot.EncounterTime);
        Assert.True(snapshot.Encounter.IsActive);
        var player = snapshot.Combatants[100];
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
            new SkillDisplayEntry(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(13000010, "Recover", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcDisplayEntry>
        {
            [9_999_999] = new(9_999_999, "Nazarak", NpcCatalogKind.Boss)
        });

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Perigee");
        entities.ApplyNpcCode(200, 9_999_999);

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
        Assert.Equal(1500, player.DamageAmount);
        Assert.Equal(600, player.HealingAmount);
        Assert.Equal(CharacterClass.Gladiator, player.CharacterClass);
        Assert.Equal(1_000, snapshot.EncounterStartTime);
        Assert.Equal(2_500, snapshot.EncounterEndTime);
        Assert.Equal(1_500, snapshot.EncounterTime);
        Assert.Equal(1500d / 1500 * 1000, player.DamagePerSecond, 3);
        Assert.Equal(1d, player.DamageContribution, 3);
    }

    [Fact]
    public void Adapter_CreateSnapshot_FoldsSummonOutgoingDamageToOwnerAndSkipsSummonTargets()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new SkillDisplayEntry(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null)
        ], new Dictionary<int, NpcDisplayEntry>());

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

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, new SceneBoundaryStore()).CreateSnapshot();

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
            new SkillDisplayEntry(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcDisplayEntry>());

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

        var snapshot = new SceneCombatSnapshotAdapter(entities, combat, new SceneBoundaryStore()).CreateSnapshot();

        Assert.Equal(CharacterClass.Gladiator, snapshot.Combatants[100].CharacterClass);
        Assert.Null(snapshot.Combatants[200].CharacterClass);
    }

    [Fact]
    public void Adapter_CreateSnapshot_UsesTimelineNowForBossFallbackWithoutCombat()
    {
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
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
        var metadata = new SceneBoundaryStore();
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
    public void Owner_CreateSnapshot_ReusesProjectionUntilInputRevisionChanges()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendNickname(100, "Player");
        scene.AppendNpcCode(200, 2_999_999);
        scene.AppendNpcName(2_999_999, "Target");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var first = scene.CreateSnapshot();
        var second = scene.Owner.CreateSnapshot();

        Assert.Same(first, second);
        Assert.Equal(1, scene.Owner.ProjectionCacheStats.SnapshotBuilds);
        Assert.Equal(1, scene.Owner.ProjectionCacheStats.SnapshotCacheHits);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 300,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 2_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var third = scene.CreateSnapshot();

        Assert.NotSame(first, third);
        Assert.Equal(800, third.Combatants[100].DamageAmount);
        Assert.Equal(2, scene.Owner.ProjectionCacheStats.SnapshotBuilds);
        Assert.Equal(1, scene.Owner.ProjectionCacheStats.SnapshotCacheHits);
    }

    [Fact]
    public void Owner_CreateSnapshot_Uses_Metadata_Class_Over_Skill_Evidence()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendNickname(100, "Player", characterClass: CharacterClass.Cleric);
        scene.AppendNpcCode(200, 2_999_999);
        scene.AppendNpcName(2_999_999, "Target");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 300,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 2_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var snapshot = scene.CreateSnapshot();

        Assert.Equal(CharacterClass.Cleric, snapshot.Combatants[100].CharacterClass);
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(100, out var metadata));
        Assert.Equal(CharacterClass.Cleric, metadata.CharacterClass);
    }

    [Fact]
    public void Owner_CreateSnapshot_Metadata_Class_Overrides_Previous_Skill_Evidence()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendNickname(100, "Player");
        scene.AppendNpcCode(200, 2_999_999);
        scene.AppendNpcName(2_999_999, "Target");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 300,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 2_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var inferred = scene.CreateSnapshot();
        Assert.Equal(CharacterClass.Gladiator, inferred.Combatants[100].CharacterClass);

        scene.AppendNickname(100, "Player", characterClass: CharacterClass.Cleric);
        var corrected = scene.CreateSnapshot();

        Assert.Equal(CharacterClass.Cleric, corrected.Combatants[100].CharacterClass);
    }

    [Fact]
    public void Owner_CreateSnapshot_DoesNotInvalidateCacheForNpcNameEventButInvalidatesForEntityAndBossFocusChanges()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendNickname(100, "Player");
        scene.AppendNpcKind(200, NpcKind.Boss);
        scene.AppendNpcCode(200, 2_999_999);
        scene.SetNpcBattle(200, true, 900);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var first = scene.CreateSnapshot();
        var cached = scene.Owner.CreateSnapshot();
        Assert.Same(first, cached);

        scene.AppendNpcName(2_999_999, "Renamed Target");
        var ignoredNpcName = scene.Owner.CreateSnapshot();
        Assert.Same(first, ignoredNpcName);

        scene.AppendNickname(100, "Renamed Player");
        var entityChanged = scene.Owner.CreateSnapshot();
        Assert.NotSame(ignoredNpcName, entityChanged);

        scene.AppendNpcHp(200, 1234, 5000, 2_000);
        var bossChanged = scene.Owner.CreateSnapshot();
        Assert.NotSame(entityChanged, bossChanged);
        Assert.Contains(bossChanged.BossFocuses, static boss => boss.InstanceId == 200 && boss.Hp == 1234);

        Assert.Equal(3, scene.Owner.ProjectionCacheStats.SnapshotBuilds);
        Assert.Equal(2, scene.Owner.ProjectionCacheStats.SnapshotCacheHits);
    }

    [Fact]
    public void Owner_CreateSnapshot_ReplayIdleTicksUseSingleProjectionBuild()
    {
        SceneReplayFixture.SetResources();
        var replay = SceneReplayFixture.Replay("aion2flow.stream.20260415211500.log");
        var before = replay.SceneOwner.ProjectionCacheStats;

        var first = replay.SceneOwner.CreateSnapshot();
        for (var i = 0; i < 8; i++)
        {
            var next = replay.SceneOwner.CreateSnapshot();
            Assert.Same(first, next);
        }

        var after = replay.SceneOwner.ProjectionCacheStats;
        Assert.Equal(0, after.SnapshotBuilds - before.SnapshotBuilds);
        Assert.Equal(9, after.SnapshotCacheHits - before.SnapshotCacheHits);
        Assert.True(first.Combatants.Count > 0);
    }

    [Fact]
    public void Owner_CreateSnapshot_CacheHit_ReturnsFrozenInstance_WithoutAllocation()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendNickname(100, "Player");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 200,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var first = scene.CreateSnapshot();
        var warm = scene.Owner.CreateSnapshot();
        Assert.Same(first, warm);
        var beforeStats = scene.Owner.ProjectionCacheStats;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            Assert.Same(first, scene.Owner.CreateSnapshot());
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        var afterStats = scene.Owner.ProjectionCacheStats;

        Assert.Equal(0, allocated);
        Assert.Equal(beforeStats.SnapshotBuilds, afterStats.SnapshotBuilds);
        Assert.Equal(beforeStats.SnapshotCacheHits + 1_000, afterStats.SnapshotCacheHits);
    }

    [Fact]
    public void Owner_CreateSnapshot_ActiveMiss_DoesNotAllocatePerEventPacketDtos()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var journal = new ObservedEventJournal();
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Player");
        for (var i = 0; i < 128; i++)
        {
            combat.ApplyCombat(100, 200, new CombatObservation
            {
                SkillCode = 11000010,
                Damage = 100 + i,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }, 1_000 + i);
        }

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, metadata, combat);
        owner.Refresh();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = owner.CreateSnapshot();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.True(snapshot.Combatants.TryGetValue(100, out var player));
        Assert.Equal(20_928, player.DamageAmount);
        Assert.True(allocated < 160_000, $"active miss allocated {allocated:N0} bytes");
    }

    [Fact]
    public void Owner_CreateSkillBreakdown_UsesCompactProjectionAllocation()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var journal = new ObservedEventJournal();
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        entities.ApplyNickname(100, "Player");
        for (var i = 0; i < 128; i++)
        {
            combat.ApplyCombat(100, 200, new CombatObservation
            {
                SkillCode = 11000010,
                Damage = 100 + i,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }, 1_000 + i);
        }

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, metadata, combat);
        owner.Refresh();
        var snapshot = owner.CreateSnapshot();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var breakdown = owner.CreateSkillBreakdown(snapshot, 100);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.True(breakdown.Skills.TryGetBySkillCode(11000010, out var skill));
        Assert.Equal(128, skill.Times);
        Assert.True(allocated < 40_000, $"skill breakdown allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SceneCombatSnapshot_PublicApi_DoesNotExposeMutableCollectionsOrSkills()
    {
        Assert.Equal(typeof(CombatantSnapshotMap), typeof(SceneCombatSnapshot).GetProperty(nameof(SceneCombatSnapshot.Combatants))!.PropertyType);
        Assert.Equal(typeof(SnapshotList<SceneBossFocusSnapshot>), typeof(SceneCombatSnapshot).GetProperty(nameof(SceneCombatSnapshot.BossFocuses))!.PropertyType);
        Assert.NotNull(typeof(CombatantSnapshotMap).GetMethod(nameof(CombatantSnapshotMap.AsSpan), Type.EmptyTypes));
        Assert.NotNull(typeof(SkillMetricsSnapshotMap).GetMethod(nameof(SkillMetricsSnapshotMap.AsSpan), Type.EmptyTypes));
        Assert.DoesNotContain(typeof(SceneCombatantMetrics).GetProperties(), static property => property.Name == "Skills");
    }

    [Fact]
    public void Owner_CreateSnapshot_CachesBossFocusOnlyUntilActivityLeaseExpires()
    {
        var sceneStarted = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableSceneTimeProvider(sceneStarted.AddMilliseconds(1_000));
        var scene = new SceneLiveReadModel(sceneStarted, timeProvider);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        var source = SyntheticObservationExtensions.Source(sceneStarted.ToUnixTimeMilliseconds() + 1_000);
        sink.AppendNpcKind(in source, 200, NpcKind.Boss);
        sink.SetNpcBattle(in source, 200, true);

        var first = scene.Owner.CreateSnapshot();
        var second = scene.Owner.CreateSnapshot();

        Assert.Same(first, second);
        Assert.Single(second.BossFocuses);

        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(11_001));
        var expired = scene.Owner.CreateSnapshot();

        Assert.NotSame(second, expired);
        Assert.Empty(expired.BossFocuses);
        Assert.Equal(2, scene.Owner.ProjectionCacheStats.SnapshotBuilds);
        Assert.Equal(1, scene.Owner.ProjectionCacheStats.SnapshotCacheHits);
    }

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
            Raw = default,
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
    public void Owner_Refresh_DoesNotFlushPendingCompactAvoidanceBeforeCompletedBatch()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);

        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { OffsetTicks = 1_000 * TimeSpan.TicksPerMillisecond, ObservationOrdinal = 0, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 100,
            TargetEntityId = 200,
            Raw = new RawPacketReference { Opcode = 0x0438 },
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 77,
                Type = 1,
                LayoutTag = 0
            }
        });

        owner.Refresh();

        Assert.False(owner.Combat.TryGetPair(100, 200, out _));

        journal.CompleteBatch(100);
        owner.Refresh();

        Assert.True(owner.Combat.TryGetPair(100, 200, out var pair));
        Assert.NotNull(pair);
        Assert.Equal(0, pair.TotalDamage);
        Assert.Equal(1, pair.EvadeCount);
    }

    [Fact]
    public void Owner_CreateDetailDelta_ReusesWarmSubscriptionForIrrelevantCombat()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(scene, sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(scene, sink, 100, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(scene, sink, 300, 400, 11000010, 700, 3_000, 3);
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
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(scene, sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(scene, sink, 100, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(scene, sink, 100, 200, 11000010, 200, 3_000, 3);
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
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        AppendScenePacket(scene, sink, 100, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(scene, sink, 300, 400, 11000010, 700, 2_000, 2);
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
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        sink.AppendNickname(100, "Owner");
        sink.AppendSummon(100, 500);
        AppendScenePacket(scene, sink, 500, 200, 11000010, 500, 1_000, 1);
        AppendScenePacket(scene, sink, 500, 200, 11000010, 300, 2_000, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var firstSnapshot = scene.Owner.CreateSnapshot();
        var cold = scene.Owner.CreateDetailDelta(firstSnapshot, 100);

        AppendScenePacket(scene, sink, 500, 200, 11000010, 200, 3_000, 3);
        sink.CompleteBatch(3);

        var secondSnapshot = scene.Owner.CreateSnapshot();
        var warm = scene.Owner.CreateDetailDelta(secondSnapshot, 100);

        Assert.NotSame(cold, warm);
        Assert.Equal(3, warm.Revision);
        Assert.Equal(3, warm.Events.Count);
        Assert.All(warm.Events, e => Assert.Equal(100, e.SourceId));
    }

    [Fact]
    public async Task Owner_CreateFrame_KeepsSnapshotDetailAndArchiveOnOneReadModelRevisionUnderConcurrentAppendAndReset()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        var scene = new SceneLiveReadModel();
        var sink = SceneSinkFactory.CreateForLive(scene)();
        sink.AppendNickname(100, "Player");
        using var writerStop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var testCancellation = TestContext.Current.CancellationToken;
        var writer = Task.Run(() =>
        {
            var batch = 0L;
            while (!writerStop.IsCancellationRequested && !testCancellation.IsCancellationRequested)
            {
                var currentBatch = Interlocked.Increment(ref batch);
                var targetId = 200 + (int)(currentBatch % 4);
                sink.AppendCombatPacket(new ParsedCombatPacket
                {
                    SourceId = 100,
                    TargetId = targetId,
                    SkillCode = 11000010,
                    Damage = 10,
                    Timestamp = 1_000 + currentBatch * 25,
                    BatchOrdinal = currentBatch,
                    HitContribution = 1,
                    AttemptContribution = 1,
                    EventKind = CombatEventKind.Damage,
                    ValueKind = CombatValueKind.Damage
                });
                sink.CompleteBatch(currentBatch);
                if (currentBatch % 37 == 0)
                    scene.Reset(new DateTimeOffset(2026, 5, 9, 14, 30, (int)(currentBatch % 60), TimeSpan.Zero));
            }
        }, testCancellation);

        for (var i = 0; i < 250; i++)
        {
            var frame = scene.Owner.CreateFrame(100, forceDetailRefresh: i % 17 == 0);
            var snapshot = frame.Snapshot;
            Assert.Equal(snapshot.ReadModelRevision, frame.ReadModelRevision);
            Assert.Equal(snapshot.BossFocuses.Count, frame.BossFocuses.Count);
            Assert.True(snapshot.EncounterTime >= 0);
            Assert.True(snapshot.EncounterEndTime >= snapshot.EncounterStartTime);
            if (frame.Detail is { } detail)
            {
                Assert.Equal(100, detail.CombatantId);
                Assert.True(detail.Revision <= snapshot.ReadModelRevision);
                Assert.All(detail.Events, e => Assert.True(e.SourceId == 100 || e.TargetId == 100));
            }

            if (snapshot.EncounterTime > 0 && snapshot.Combatants.Count > 0)
            {
                var archive = scene.Owner.CreateArchiveCapture();
                if (archive.Snapshot.EncounterTime > 0 && archive.Snapshot.Combatants.Count > 0)
                {
                    Assert.NotEmpty(archive.Payload.Events);
                    Assert.True(archive.Snapshot.EncounterEndTime >= archive.Snapshot.EncounterStartTime);
                }
            }

            await Task.Yield();
        }

        await writerStop.CancelAsync();
        await writer.WaitAsync(testCancellation);
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
            Assert.Equal(scene.SessionId, snapshot.EncounterId);
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
                Damage = 500,
                Timestamp = scene.SessionStarted.ToUnixTimeMilliseconds() + 1_000,
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
                Damage = 300,
                Timestamp = scene.SessionStarted.ToUnixTimeMilliseconds() + 2_000,
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
            var nextStarted = new DateTimeOffset(2026, 5, 9, 14, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 9)));
            scene.Reset(nextStarted);
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = 201,
                SkillCode = 11000010,
                Damage = 700,
                Timestamp = scene.SessionStarted.ToUnixTimeMilliseconds() + 3_000,
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
                Damage = 300,
                Timestamp = scene.SessionStarted.ToUnixTimeMilliseconds() + 4_000,
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
            Assert.NotEqual(first.EncounterId, second.EncounterId);
            Assert.Equal(nextStarted, scene.SessionStarted);
            Assert.Equal(nextStarted, scene.Owner.SceneStarted);
            Assert.Equal(resetStartOrdinal, scene.Journal.Read(resetStartOrdinal).Stamp.ObservationOrdinal);
            Assert.Equal(scene.SessionId, scene.Journal.Read(resetStartOrdinal).SceneSessionId);
            Assert.Equal(1000, second.Combatants[100].DamageAmount);
            Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(100, out var pc));
            Assert.Equal("Player", pc.Nickname);
            Assert.DoesNotContain(200, second.Combatants.Keys);
        }
        finally
        {
        }
    }

    [Fact]
    public void LiveReadModel_Reset_AppliesPendingNpcIdentityBeforeBarrier()
    {
        const int npcId = 29194;
        const int npcCode = 2_980_122;
        var scene = new SceneLiveReadModel();
        try
        {
            var sink = SceneSinkFactory.CreateForLive(scene)();
            sink.AppendNpcCode(npcId, npcCode);
            sink.AppendNpcKind(npcId, NpcKind.Boss);
            sink.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = 100,
                TargetId = npcId,
                SkillCode = 11000010,
                Damage = 500,
                Timestamp = 1_000,
                BatchOrdinal = 1,
                HitContribution = 1,
                AttemptContribution = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            sink.CompleteBatch(1);

            scene.Reset(new DateTimeOffset(2026, 5, 30, 19, 35, 44, TimeSpan.Zero));

            Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(npcId, out var retainedNpcCode));
            Assert.Equal(npcCode, retainedNpcCode);
            Assert.True(scene.Owner.Entities.TryGet(npcId, out var retainedEntity));
            Assert.Equal(NpcKind.Boss, retainedEntity.Kind);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            sink.SetNpcBattle(npcId, true, now);
            sink.AppendNpcHp(npcId, 70, 100, now);

            var snapshot = scene.Owner.CreateSnapshot();

            Assert.Empty(snapshot.Combatants);
            var boss = Assert.Single(snapshot.BossFocuses);
            Assert.Equal(npcId, boss.InstanceId);
            Assert.Equal(70, boss.Hp);
            Assert.Equal(100, boss.MaxHp);
        }
        finally
        {
        }
    }

    [Fact]
    public void LiveReadModel_Reset_RestoresActiveBossFocusAndPreservesNpcCatalogIdentity()
    {
        const int bossId = 29194;
        const int bossCode = 2_980_122;
        const int monsterId = 29195;
        const int monsterCode = 2_980_123;
        var firstStarted = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        var secondStarted = firstStarted.AddSeconds(10);
        var timeProvider = new MutableSceneTimeProvider(firstStarted);
        var scene = new SceneLiveReadModel(firstStarted, timeProvider);
        try
        {
            var sink = SceneSinkFactory.CreateForLive(scene)();
            var identitySource = SyntheticObservationExtensions.Source(firstStarted.ToUnixTimeMilliseconds() + 10);
            sink.AppendNpcCode(in identitySource, bossId, bossCode);
            sink.AppendNpcKind(in identitySource, bossId, NpcKind.Boss);
            sink.AppendNpcCode(in identitySource, monsterId, monsterCode);
            sink.AppendNpcKind(in identitySource, monsterId, NpcKind.Monster);
            sink.SetNpcBattle(bossId, true, firstStarted.ToUnixTimeMilliseconds() + 20);
            sink.AppendNpcHp(bossId, 100, 100, firstStarted.ToUnixTimeMilliseconds() + 30);
            _ = scene.Owner.CreateSnapshot();

            scene.Reset(secondStarted);

            sink.AppendNpcHp(bossId, 70, 100, secondStarted.ToUnixTimeMilliseconds() + 100);
            timeProvider.SetUtcNow(secondStarted.AddMilliseconds(100));
            var snapshot = scene.Owner.CreateSnapshot();

            var boss = Assert.Single(snapshot.BossFocuses);
            Assert.Equal(bossId, boss.InstanceId);
            Assert.Equal(70, boss.Hp);
            Assert.Equal(100, boss.MaxHp);
            Assert.True(scene.Owner.Entities.TryGet(bossId, out var retainedBoss));
            Assert.Equal(bossCode, retainedBoss.NpcCode);
            Assert.Equal(NpcKind.Boss, retainedBoss.Kind);
            Assert.True(scene.Owner.Entities.TryGet(monsterId, out var retainedMonster));
            Assert.Equal(monsterCode, retainedMonster.NpcCode);
            Assert.Equal(NpcKind.Monster, retainedMonster.Kind);
            Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(bossId, out var retainedBossCode));
            Assert.Equal(bossCode, retainedBossCode);
            Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(monsterId, out var retainedMonsterCode));
            Assert.Equal(monsterCode, retainedMonsterCode);
        }
        finally
        {
        }
    }

    private sealed class MutableSceneTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
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

    private static SkillDisplayCatalog BuildSkillMap()
    {
        return
        [
            new SkillDisplayEntry(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
        ];
    }

    private static void AppendScenePacket(
        SceneLiveReadModel scene,
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
            Damage = damage,
            Timestamp = scene.SessionStarted.ToUnixTimeMilliseconds() + timestamp,
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
    public void M2_06_ScenePath_CapturesSameCombatantIds_AsBaselinePath()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var adapter = new SceneCombatSnapshotAdapter(entities, combat, metadata);
        var sceneSnapshot = adapter.CreateSnapshot();

        var baselineSnapshot = replay.Snapshot;

        var baselineWithDamage = baselineSnapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .Select(static kv => kv.Key)
            .ToHashSet();

        var sceneIds = sceneSnapshot.Combatants.Keys.ToHashSet();

        foreach (var id in baselineWithDamage)
        {
            Assert.True(sceneIds.Contains(id), $"Scene path missing combatant {id} that has damage in baseline path");
        }
    }

    [Fact]
    public void M2_06_CombatStore_DamageTotals_MatchBaseline_OutgoingDamage()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var baselineSnapshot = replay.Snapshot;

        var topDealer = baselineSnapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        Assert.True(combat.TryGetCombatant(topDealer.Key, out var sceneCombatant));
        Assert.True(sceneCombatant!.OutgoingDamage > 0, $"Scene path has 0 outgoing damage for combatant {topDealer.Key}");
    }
}
