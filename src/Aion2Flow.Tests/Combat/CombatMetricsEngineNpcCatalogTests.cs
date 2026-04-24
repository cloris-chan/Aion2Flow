using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineNpcCatalogTests
{
    [Fact]
    public void LoadSkillMap_Also_Loads_NpcCatalog()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");

        Assert.True(CombatMetricsEngine.TryResolveNpcCatalogEntry(2000002, out var entry));
        Assert.Equal("德拉克紐特弓手", entry.Name);
        Assert.Equal(NpcCatalogKind.Monster, entry.Kind);
    }

    [Theory]
    [InlineData(NpcCatalogKind.Monster, NpcKind.Monster)]
    [InlineData(NpcCatalogKind.Boss, NpcKind.Boss)]
    [InlineData(NpcCatalogKind.Summon, NpcKind.Summon)]
    [InlineData(NpcCatalogKind.Friendly, NpcKind.Friendly)]
    [InlineData(NpcCatalogKind.Unknown, NpcKind.Unknown)]
    [InlineData(NpcCatalogKind.Object, NpcKind.Unknown)]
    public void ResolveNpcKind_Maps_Catalog_Kind_Enum(NpcCatalogKind kind, NpcKind expected)
    {
        Assert.Equal(expected, CombatMetricsEngine.ResolveNpcKind(kind));
    }

    [Fact]
    public void ResolveCombatantDisplayName_Returns_NpcCatalog_Name_When_NpcCode_Set()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources([], catalog);

        var store = new CombatMetricsStore();
        store.AppendNpcCode(npcInstanceId, npcCode);

        var snapshot = new DamageMeterSnapshot();

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, npcInstanceId);

        Assert.True(catalog.TryGetValue(npcCode, out var expectedEntry));
        Assert.Equal(expectedEntry.Name, displayName);
    }

    [Fact]
    public void ResolveCombatantDisplayName_Falls_Back_To_NpcNameByCode_When_Catalog_Missing()
    {
        const int npcInstanceId = 5555;
        const int unknownNpcCode = 2999999;
        CombatMetricsEngine.SetGameResources([], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.AppendNpcCode(npcInstanceId, unknownNpcCode);
        store.AppendNpcName(unknownNpcCode, "CustomNpcName");

        var snapshot = new DamageMeterSnapshot();

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, npcInstanceId);

        Assert.Equal("CustomNpcName", displayName);
    }

    [Fact]
    public void ResolveCombatantDisplayName_Prefers_NpcCatalog_Over_Nickname()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources([], catalog);

        var store = new CombatMetricsStore();
        store.AppendNpcCode(npcInstanceId, npcCode);
        store.AppendNickname(npcInstanceId, "PlayerNick");

        var snapshot = new DamageMeterSnapshot();

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, npcInstanceId);

        Assert.Equal(catalog[npcCode].Name, displayName);
    }

    [Fact]
    public void ResolveCombatantDisplayName_Returns_Numeric_Id_Without_NpcCode()
    {
        CombatMetricsEngine.SetGameResources([], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var snapshot = new DamageMeterSnapshot();

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, 12345);

        Assert.Equal("12345", displayName);
    }

    [Fact]
    public void ArchiveSlice_Preserves_Npc_State_For_Target_Entity()
    {
        const int npcEntityId = 16710;
        const int npcCode = 2980179;
        const int playerId = 9206;

        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources([], catalog);

        var store = new CombatMetricsStore();
        store.AppendNpcCode(npcEntityId, npcCode);
        store.AppendNpcName(npcCode, catalog[npcCode].Name);
        store.AppendNpcKind(npcEntityId, NpcKind.Monster);
        store.AppendNickname(playerId, "TestPlayer");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 36358,
            Timestamp = now,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
        });

        var snapshot = new DamageMeterSnapshot
        {
            BattleStartTime = now - 1000,
            BattleEndTime = now + 1000,
        };
        snapshot.Combatants[playerId] = new CombatantMetrics("TestPlayer");

        var archiveSlice = store.CreateArchiveSlice(snapshot);

        Assert.True(archiveSlice.TryGetNpcRuntimeState(npcEntityId, out var state),
            $"Archive slice missing NPC state for entity {npcEntityId}");
        Assert.Equal(npcCode, state.NpcCode);

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(archiveSlice, snapshot, npcEntityId);
        Assert.NotEqual(npcEntityId.ToString(), displayName);
        Assert.Equal(catalog[npcCode].Name, displayName);
    }

    [Fact]
    public void MultiBattle_ArchiveSlice_Preserves_Npc_State_After_Reset()
    {
        const int npcEntityId = 17952;
        const int npcCode = 2980159;
        const int playerId = 11616;
        const int battle1Target = 33541;

        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), catalog);

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);

        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        store.AppendNickname(playerId, "TestPlayer");

        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = battle1Target,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 50000,
            Timestamp = t0,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
        });

        store.AppendNpcCode(npcEntityId, npcCode);
        if (catalog.TryGetValue(npcCode, out var entry))
        {
            store.AppendNpcName(npcCode, entry.Name);
            store.AppendNpcKind(npcEntityId, CombatMetricsEngine.ResolveNpcKind(entry.Kind));
        }

        Assert.True(store.TryGetNpcRuntimeState(npcEntityId, out var preResetState),
            "NPC state should exist on live store before reset");
        Assert.Equal(npcCode, preResetState.NpcCode);

        var snapshot1 = engine.CreateBattleSnapshot();
        var archiveSlice1 = store.CreateArchiveSlice(snapshot1);
        engine.Reset();

        Assert.True(store.TryGetNpcRuntimeState(npcEntityId, out var postResetState),
            "NPC state should survive reset on live store");
        Assert.Equal(npcCode, postResetState.NpcCode);

        var t1 = t0 + 10_000;
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 14547,
            Timestamp = t1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
        });
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17730000,
            OriginalSkillCode = 17730001,
            Damage = 4092,
            Timestamp = t1 + 100,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
        });

        var snapshot2 = engine.CreateBattleSnapshot();
        var liveName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot2, npcEntityId);
        Assert.NotEqual(npcEntityId.ToString(), liveName);
        Assert.Equal(catalog[npcCode].Name, liveName);

        var archiveSlice2 = store.CreateArchiveSlice(snapshot2);
        Assert.True(archiveSlice2.TryGetNpcRuntimeState(npcEntityId, out var archiveState),
            "Archive slice for Battle 2 should have NPC state for entity 17952");
        Assert.Equal(npcCode, archiveState.NpcCode);

        var archiveName = CombatMetricsEngine.ResolveCombatantDisplayName(archiveSlice2, snapshot2, npcEntityId);
        Assert.NotEqual(npcEntityId.ToString(), archiveName);
        Assert.Equal(catalog[npcCode].Name, archiveName);
    }

    [Fact]
    public void FullSession_Replay_Resolves_Entity_17952_Npc_Name()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), catalog);

        var logPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "replay-scan", "aion2flow.frame.20260415173658.log");
        if (!File.Exists(logPath))
        {
            return;
        }

        var result = PacketLogReplayService.Replay(logPath);
        const int entityId = 17952;
        const int npcCode = 2980159;

        Assert.True(result.Store.TryGetNpcRuntimeState(entityId, out var state),
            $"Replay store must have NPC state for entity {entityId}");
        Assert.Equal(npcCode, state.NpcCode);

        var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(result.Store, result.Snapshot, entityId);
        Assert.NotEqual(entityId.ToString(), displayName);
        Assert.Equal(catalog[npcCode].Name, displayName);

        var archiveSlice = result.Store.CreateArchiveSlice(result.Snapshot);
        var archiveName = CombatMetricsEngine.ResolveCombatantDisplayName(archiveSlice, result.Snapshot, entityId);
        Assert.NotEqual(entityId.ToString(), archiveName);
        Assert.Equal(catalog[npcCode].Name, archiveName);
    }

    [Fact]
    public void Replay_State_Catalog_Probe_Does_Not_Overwrite_NpcSpawn_Code_When_Value_Misses_Catalog()
    {
        const int entityId = 4370;
        const int npcCode = 2980049;
        const int sceneStateValue = 200003;

        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        Assert.True(catalog.ContainsKey(npcCode));
        Assert.False(catalog.ContainsKey(sceneStateValue));
        CombatMetricsEngine.SetGameResources([], catalog);

        var npcSpawnLine = $"2026-04-24T23:09:45.3164516+08:00|npc-spawn|16777343:56119->16777343:49300|kind=create-198|entity={entityId}|npcCode={npcCode}|data=00";
        var observedLine = $"2026-04-24T23:10:13.4000000+08:00|state-4536|16777343:56119->16777343:49300|source={entityId}|value0=0|tailLen=0|data=094536922200";
        var stateLine = $"2026-04-24T23:10:13.4172863+08:00|state-2136|16777343:56119->16777343:49300|target={entityId}|seq=6|value0={sceneStateValue}|value1=7602133|value2=0|value3=0x41c568f4|value4=0x4537c974|value5=0x42800000|value6=0xc2b40000|value7=2|tailMarker=0x004f|tailLen=7|data=33213606000000430D0300D5FF730000000000F468C54174C93745000080420000B4C202000000000000000000004F00";

        var path = Path.Combine(Path.GetTempPath(), $"replay-npc-state-{Guid.NewGuid()}.log");
        File.WriteAllLines(path, [npcSpawnLine, observedLine, stateLine]);
        try
        {
            var result = PacketLogReplayService.Replay(path);

            Assert.True(result.Store.TryGetNpcRuntimeState(entityId, out var state),
                $"Replay store missing NPC state for entity {entityId}");
            Assert.Equal(npcCode, state.NpcCode);
            Assert.Equal((uint)sceneStateValue, state.Value2136);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(
        16710, 2980179,
        "2026-04-15T17:00:10.2378590+08:00|npc-spawn|16777343:60362->16777343:55221|kind=create-198|entity=16710|npcCode=2980179|data=E1014036C6820104220053792D000002000040C00000C040000090420000B442004001E0C65BE0C65B64000000640000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000C682010110000040C00000C04000009042110284969800FFFFFFFFFFFFFFFF8075D52ABB030000C6820101000040C00000C040000090421103AEF22101FFFFFFFFFFFFFFFF8075D52ABB030000C6820101000040C00000C0400000904201003200000003019600000096000000472C0C8400",
        "2026-04-15T17:00:16.6449720+08:00|damage|16777343:60362->16777343:55221|target=16710|source=9206|skillRaw=17070240|damage=36358|skill=17070240|baseSkill=17070000|charge=0|specs=2+4|skillName=Chain of Torment|skillKind=PeriodicDamage|skillSemantics=Damage, PeriodicDamage, Support|valueKind=Damage|data=260438C682011600F647A07804010D0318008B1EBF6501000000DF8801869C0201")]
    [InlineData(
        17858, 2980049,
        "2026-04-15T17:28:42.6249268+08:00|npc-spawn|16777343:60362->16777343:59238|kind=create-198|entity=17858|npcCode=2980049|data=E1014036C28B01042200D1782D000002000040C00000C040000090420000B44200400180EA3080EA3064000000640000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000C28B010110000040C00000C04000009042110284969800FFFFFFFFFFFFFFFF8075D52ABB030000C28B0101000040C00000C0400000904211039AF22101FFFFFFFFFFFFFFFF8075D52ABB030000C28B0101000040C00000C0400000904201002D00000003019600000096000000472C0C8400",
        "2026-04-15T17:28:48.9762913+08:00|damage|16777343:60362->16777343:59238|target=17858|source=9849|skillRaw=17070240|damage=27944|skill=17070240|baseSkill=17070000|charge=0|specs=2+4|skillName=Chain of Torment|skillKind=PeriodicDamage|skillSemantics=Damage, PeriodicDamage, Support|valueKind=Damage|data=290438C28B013600F94CA07804012E0308008B1EBF6501000000F78001A8DA010101EA15")]
    public void Replay_NpcSpawn_And_Damage_Resolves_Npc_Display_Name(int entityId, int npcCode, string npcSpawnLine, string damageLine)
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), catalog);

        var path = Path.Combine(Path.GetTempPath(), $"replay-npc-{Guid.NewGuid()}.log");
        File.WriteAllLines(path, [npcSpawnLine, damageLine]);
        try
        {
            var result = PacketLogReplayService.Replay(path);

            Assert.True(result.ReplayedEventCounts.ContainsKey("npc-spawn"), "npc-spawn not replayed");
            Assert.True(result.ReplayedEventCounts.ContainsKey("damage"), "damage not replayed");

            Assert.True(result.Store.TryGetNpcRuntimeState(entityId, out var state),
                $"Replay store missing NPC state for entity {entityId}");
            Assert.Equal(npcCode, state.NpcCode);

            var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(
                result.Store, result.Snapshot, entityId);
            Assert.NotEqual(entityId.ToString(), displayName);
            Assert.Equal(catalog[npcCode].Name, displayName);

            var target = result.Combatants.FirstOrDefault(c => c.CombatantId == entityId);
            Assert.NotNull(target);
            Assert.NotEqual(entityId.ToString(), target.DisplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
