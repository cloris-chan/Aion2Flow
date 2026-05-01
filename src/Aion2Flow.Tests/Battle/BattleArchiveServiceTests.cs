using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Combat.NpcRuntime;

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
        store.UpdateCurrentMap(200003);
        store.UpdateCurrentMapInstance(113515);
        store.AppendNickname(1, "Tester");

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

        store.AppendNickname(playerId, "Tester");
        store.AppendNickname(unrelatedPlayerId, "Other");
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
        store.UpdateCurrentMap(600091);
        store.UpdateCurrentMapInstance(410001);

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
}
