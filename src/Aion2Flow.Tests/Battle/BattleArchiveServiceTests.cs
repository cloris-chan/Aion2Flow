using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Combat.NpcRuntime;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Projection;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class BattleArchiveServiceTests
{
    [Fact]
    public void Archive_Stores_DeepCloned_Snapshot()
    {
        var service = new BattleArchiveService();
        var store = new CombatMetricsStore();
        var snapshot = new DamageMeterSnapshot
        {
            TargetName = "Test Boss",
            BattleTime = 12_000,
            MapId = 200003,
            MapInstanceId = 113515,
            Encounter = new EncounterSummary
            {
                TrackingTargetId = 123,
                IsActive = false,
                ShouldArchive = true,
                Reason = "teardown-hint"
            }
        };

        var combatant = new CombatantMetrics("Tester")
        {
            DamagePerSecond = 1000,
            DamageContribution = 1
        };
        snapshot.Combatants[1] = combatant;
        store.StageDestinationMap(200003);
        store.StageDestinationMapInstance(113515);
        store.MarkSceneArrival();
        store.AppendNickname(1, "Tester", 420);

        var record = service.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Single(service.History);
        Assert.Equal("Test Boss", record!.Snapshot.TargetName);
        Assert.Equal((uint)200003, record.Snapshot.MapId);
        Assert.Equal((uint)113515, record.Snapshot.MapInstanceId);
        Assert.Equal((uint)200003, record.Store.CurrentMapId);
        Assert.Equal((uint)113515, record.Store.CurrentMapInstanceId);
        Assert.True(service.TryGetBattle(record.BattleId, out var archivedRecord));
        Assert.Same(record, archivedRecord);

        snapshot.TargetName = "Changed";
        store.AppendNickname(1, "Changed");

        Assert.Equal("Test Boss", record.Snapshot.TargetName);
        Assert.Equal("Tester", record.Store.Nicknames[1]);
        Assert.Equal(420, record.Store.PlayerOriginServerIds[1]);
    }

    [Fact]
    public void Archive_Extracts_Relevant_Lookups_Without_Mutating_Live_Store()
    {
        var service = new BattleArchiveService();
        var store = new CombatMetricsStore();
        const int playerId = 1;
        const int unrelatedPlayerId = 2;
        const int bossInstanceId = 9001;
        const int unrelatedNpcInstanceId = 9002;
        const int bossCode = 2000002;
        const int unrelatedNpcCode = 2000003;

        store.AppendNickname(playerId, "Tester", 420);
        store.AppendNickname(unrelatedPlayerId, "Other", 160);
        store.AppendNpcCode(bossInstanceId, bossCode);
        store.AppendNpcName(bossCode, "Battle Boss");
        store.AppendNpcCode(unrelatedNpcInstanceId, unrelatedNpcCode);
        store.AppendNpcName(unrelatedNpcCode, "Idle Boss");
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = bossInstanceId,
            SkillCode = 11000010,
            OriginalSkillCode = 11000010,
            Damage = 500,
            Timestamp = 1_000
        });

        var snapshot = new DamageMeterSnapshot
        {
            BattleTime = 500,
            BattleStartTime = 900,
            BattleEndTime = 1_100,
            TargetName = "Battle Boss",
            TargetObservation = new NpcRuntimeObservation
            {
                InstanceId = bossInstanceId
            }
        };
        snapshot.Combatants[playerId] = new CombatantMetrics("Tester");

        var record = service.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(record);

        Assert.True(record!.Store.Nicknames.ContainsKey(playerId));
        Assert.False(record.Store.Nicknames.ContainsKey(unrelatedPlayerId));
        Assert.True(record.Store.PlayerOriginServerIds.ContainsKey(playerId));
        Assert.False(record.Store.PlayerOriginServerIds.ContainsKey(unrelatedPlayerId));
        Assert.True(record.Store.TryGetNpcRuntimeState(bossInstanceId, out var archivedBossState));
        Assert.Equal(bossCode, archivedBossState.NpcCode);
        Assert.False(record.Store.TryGetNpcRuntimeState(unrelatedNpcInstanceId, out _));
        Assert.True(record.Store.NpcNameByCode.ContainsKey(bossCode));
        Assert.False(record.Store.NpcNameByCode.ContainsKey(unrelatedNpcCode));

        Assert.True(store.Nicknames.ContainsKey(playerId));
        Assert.True(store.Nicknames.ContainsKey(unrelatedPlayerId));
        Assert.True(store.TryGetNpcRuntimeState(bossInstanceId, out var liveBossState));
        Assert.Equal(bossCode, liveBossState.NpcCode);
        Assert.True(store.TryGetNpcRuntimeState(unrelatedNpcInstanceId, out var liveUnrelatedNpcState));
        Assert.Equal(unrelatedNpcCode, liveUnrelatedNpcState.NpcCode);
        Assert.True(store.NpcNameByCode.ContainsKey(bossCode));
        Assert.True(store.NpcNameByCode.ContainsKey(unrelatedNpcCode));
    }

    [Fact]
    public void Archive_Slice_Uses_Snapshot_Map_When_Live_Store_Has_Advanced()
    {
        var service = new BattleArchiveService();
        var store = new CombatMetricsStore();
        store.StageDestinationMap(600091);
        store.StageDestinationMapInstance(410001);
        store.MarkSceneArrival();

        var snapshot = new DamageMeterSnapshot
        {
            BattleTime = 12_000,
            MapId = 0,
            MapInstanceId = 0
        };
        snapshot.Combatants[1] = new CombatantMetrics("Tester");

        var record = service.Archive(snapshot, store, "map-transition", isAutomatic: true);

        Assert.NotNull(record);
        Assert.Equal((uint)0, record!.Snapshot.MapId);
        Assert.Equal((uint)0, record.Snapshot.MapInstanceId);
        Assert.Equal((uint)0, record.Store.CurrentMapId);
        Assert.Equal((uint)0, record.Store.CurrentMapInstanceId);
    }

    [Fact]
    public void Archive_Skips_Equivalent_Immediate_Duplicates()
    {
        var service = new BattleArchiveService();
        var store = new CombatMetricsStore();
        var snapshot = new DamageMeterSnapshot
        {
            TargetName = "Test Boss",
            BattleTime = 5_000
        };
        snapshot.Combatants[1] = new CombatantMetrics("Tester");

        var first = service.Archive(snapshot, store, "manual", isAutomatic: false);
        var second = service.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(service.History);
    }

    [Fact]
    public void Archive_Trims_History_And_Removes_Lookup_For_Evicted_Record()
    {
        var service = new BattleArchiveService();
        var store = new CombatMetricsStore();
        Guid firstBattleId = default;

        for (var i = 0; i < 101; i++)
        {
            var snapshot = new DamageMeterSnapshot
            {
                BattleId = Guid.NewGuid(),
                TargetName = $"Boss {i}",
                BattleTime = 10_000 + i
            };
            snapshot.Combatants[i + 1] = new CombatantMetrics($"Tester {i}")
            {
                DamageContribution = 1,
                DamagePerSecond = 1_000 + i
            };

            var record = service.Archive(snapshot, store, "manual", isAutomatic: false);
            Assert.NotNull(record);

            if (i == 0)
            {
                firstBattleId = record!.BattleId;
            }
        }

        Assert.Equal(100, service.History.Count);
        Assert.False(service.TryGetBattle(firstBattleId, out _));
    }

    [Fact]
    public void SceneArchivePayload_Captures_Detail_Delta_Without_Live_Store()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = SceneArchivePayload.Create(owner, snapshot);
        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal(snapshot.BattleId, payload.Snapshot.BattleId);
        Assert.Equal(2, payload.Events.Count);
        Assert.Equal(playerId, delta.CombatantId);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(playerId, delta.Events[0].SourceId);
        Assert.Equal(bossId, delta.Events[0].TargetId);
        Assert.Equal(750, delta.Events[0].Packet.Damage);
        Assert.Equal("Tester", delta.DisplayNames[playerId]);
        Assert.Equal("Archive Boss", delta.DisplayNames[bossId]);
        Assert.Equal(751, delta.Combatant!.OutgoingDamage);
        Assert.Single(delta.OutgoingPairs);
    }

    [Fact]
    public void SceneArchivePayload_Is_Independent_Of_Live_Scene_Mutations()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = SceneArchivePayload.Create(owner, snapshot);

        snapshot.TargetName = "Changed";
        owner.Entities.ApplyNickname(playerId, "Changed");
        owner.Combat.ApplyCombat(playerId, bossId, new CombatObservation
        {
            SkillCode = 11000011,
            Damage = 250,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 2_000);

        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal("Archive Boss", payload.Snapshot.TargetName);
        Assert.Equal("Tester", payload.DisplayNames[playerId]);
        Assert.Equal(2, payload.Events.Count);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(750, delta.Events[0].Packet.Damage);
    }

    [Fact]
    public void SceneArchivePayload_Captures_Identity_And_Boss_Focus_Facts()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = SceneArchivePayload.Create(owner, snapshot);

        var bossIdentity = Assert.Single(payload.Entities, e => e.EntityId == bossId);
        Assert.Equal(2_999_997, bossIdentity.NpcCode);
        Assert.Equal(NpcKind.Boss, bossIdentity.Kind);
        Assert.True(payload.NpcNamesByCode.ContainsKey(2_999_997));
        var focus = Assert.Single(payload.Bosses, b => b.InstanceId == bossId);
        Assert.True(focus.HasHp);
        Assert.Equal(50_000, focus.Hp);
    }

    [Fact]
    public void Archive_ScenePayload_Does_Not_Create_Legacy_Store_Slice()
    {
        const int playerId = 100;
        const int bossId = 200;
        var service = new BattleArchiveService();
        var owner = CreateSceneOwner(playerId, bossId);
        var payload = SceneArchivePayload.Create(owner, owner.CreateSnapshot());

        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.NotSame(payload, record!.ScenePayload);
        Assert.Equal(payload.Events.Count, record.ScenePayload!.Events.Count);
        Assert.Empty(record.Store.CombatPacketsBySource);
        Assert.Empty(record.Store.Nicknames);
        Assert.Equal(payload.Snapshot.BattleId, record.BattleId);
    }

    private static SceneReadModelOwner CreateSceneOwner(int playerId, int bossId)
    {
        const int bossCode = 2_999_997;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, playerId, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 1_000);
        AppendState(journal, sceneId, bossId, 0, bossCode, 0, 0, null, 2, 1_001);
        AppendState(journal, sceneId, bossCode, 0, StateCodes.NpcName, 0, 0, "Archive Boss", 3, 1_002);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcKind, (int)NpcKind.Boss, 0, null, 4, 1_003);
        AppendResource(journal, sceneId, bossId, 50_000, 100_000, 5, 1_004);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcBattle, 1, 0, null, 6, 1_005);
        AppendCombat(journal, sceneId, playerId, bossId, 750, 7, 1_500);
        AppendCombat(journal, sceneId, playerId, bossId, 1, 8, 1_501);
        journal.CompleteBatch(1);

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid());
        owner.Refresh();
        return owner;
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            State = new StateObservation(sourceId, stateCode, value0, value1, 0, text)
        });
    }

    private static void AppendResource(ObservedEventJournal journal, Guid sceneId, int entityId, long current, long maximum, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = entityId,
            TargetEntityId = 0,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            Resource = new ResourceObservation(entityId, current, maximum, null, 0)
        });
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int damage, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0x0438, 0, ordinal, observedAt),
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                Damage = damage,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
    }
}
