using System.Globalization;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class PacketLogReplayServiceTests
{
    [Fact]
    public void Replay_Reconstructs_Battle_Snapshot_And_Combatant_Summaries_From_Frame_Log()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var firstLine = "2026-04-10T16:15:36.2148073+08:00|damage|16777343:62420->16777343:52250|target=200287|source=16039|skillRaw=17010230|damage=3593|skill=17010230|baseSkill=17010000|charge=0|specs=2+3|skillName=Earth's Retribution|valueKind=Damage|data=230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01";
        var secondLine = "2026-04-10T16:15:36.3112138+08:00|damage|16777343:62420->16777343:52250|target=200287|source=16039|skillRaw=17730001|damage=2875|skill=17730000|baseSkill=17730000|charge=1|skillName=Empyrean Lord's Grace|valueKind=Damage|data=220438DF9C0C0400A77DD1890E015002AFD5AD6901000000D88501BB1601";
        var metaLine = "2026-04-10T16:15:36.1000000+08:00|frame-batch|16777343:62420->16777343:52250|offset=0|frameLength=35|data=230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01";

        var firstPacket = ParseDamagePacket("230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01");
        var secondPacket = ParseDamagePacket("220438DF9C0C0400A77DD1890E015002AFD5AD6901000000D88501BB1601");
        var expectedBattleTime =
            DateTimeOffset.Parse("2026-04-10T16:15:36.3112138+08:00").ToUnixTimeMilliseconds() -
            DateTimeOffset.Parse("2026-04-10T16:15:36.2148073+08:00").ToUnixTimeMilliseconds();

        var path = WriteTempReplayLog("frame", metaLine, firstLine, secondLine);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.Equal(3, replay.TotalLines);
            Assert.Equal(2, replay.ReplayedLines);
            Assert.Equal(1, replay.SkippedLines);
            Assert.Equal(2, replay.ReplayedEventCounts["damage"]);
            Assert.Equal(1, replay.SkippedEventCounts["frame-batch"]);
            Assert.Equal(expectedBattleTime, replay.Snapshot.BattleTime);
            Assert.Equal(200287, replay.Snapshot.TargetObservation?.InstanceId);

            var source = Assert.Single(replay.Combatants, static summary => summary.CombatantId == 16039);
            Assert.Equal(firstPacket.Damage + secondPacket.Damage, source.OutgoingDamage);
            Assert.Equal((firstPacket.IsCritical ? 1 : 0) + (secondPacket.IsCritical ? 1 : 0), source.OutgoingCriticals);
            Assert.Equal(2, source.OutgoingHits);
            Assert.Equal(2, source.OutgoingAttempts);

            var target = Assert.Single(replay.Combatants, static summary => summary.CombatantId == 200287);
            Assert.Equal(firstPacket.Damage + secondPacket.Damage, target.IncomingDamage);
            Assert.Equal(source.OutgoingCriticals, target.IncomingCriticals);
            Assert.Equal(2, target.IncomingHits);
            Assert.Equal(2, target.IncomingAttempts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_Tracks_Cross_Server_And_Artifact_Dungeon_Scene_Ids()
    {
        var lowerReshantaLine = "2026-05-02T15:18:07.1029891+08:00|state-0140|16777343:60154->16777343:65518|target=8709|value0=20|value1=1|sceneMap=True|data=150140140000000000000000000000000100";
        var crossServerLine = "2026-05-02T15:18:11.3272281+08:00|state-0140|16777343:60154->16777343:65518|target=8709|value0=22|value1=1|sceneMap=True|data=150140160000000000000000000000000100";
        var artifactDungeonLine = "2026-05-02T15:18:45.0172805+08:00|state-0140|16777343:60154->16777343:65518|target=21120|value0=503006|value1=1|sceneMap=True|data=150140DEAC07000000000000000000000100";
        var pantheonLine = "2026-05-02T15:52:39.1861829+08:00|state-0140|16777343:51528->16777343:53941|target=15498|value0=50|value1=1|sceneMap=True|data=150140320000000000000000000000000100";
        var ownNicknameLine = "2026-05-02T15:52:40.0000000+08:00|nickname|16777343:51528->16777343:53941|playerId=2007|kind=own|len=7|originServer=495|data=D1143336D70F5FB17904070750657269676565EF0306000000012D000000";

        var path = WriteTempReplayLog("frame", lowerReshantaLine, crossServerLine, artifactDungeonLine, pantheonLine, ownNicknameLine);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.Equal(5, replay.ReplayedLines);
            Assert.Equal(4, replay.ReplayedEventCounts["state-0140"]);
            Assert.Equal(1, replay.ReplayedEventCounts["nickname"]);
            Assert.Equal(50u, replay.SceneOwner.Metadata.CurrentMapId);
            Assert.Equal(50u, replay.Snapshot.MapId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Frame_Replay_Stages_Scene_Map_Until_Nickname_Arrival()
    {
        var pantheonLine = "2026-05-02T15:52:39.1861829+08:00|state-0140|16777343:51528->16777343:53941|target=15498|value0=50|value1=1|sceneMap=True|data=150140320000000000000000000000000100";
        var ownNicknameLine = "2026-05-02T15:52:40.0000000+08:00|nickname|16777343:51528->16777343:53941|playerId=2007|kind=own|len=7|originServer=495|data=D1143336D70F5FB17904070750657269676565EF0306000000012D000000";

        var stagedPath = WriteTempReplayLog("frame", pantheonLine);
        var arrivedPath = WriteTempReplayLog("frame", pantheonLine, ownNicknameLine);
        try
        {
            var stagedReplay = PacketLogReplayService.Replay(stagedPath);

            Assert.Equal(1, stagedReplay.ReplayedLines);
            Assert.Equal(0u, stagedReplay.SceneOwner.Metadata.CurrentMapId);
            Assert.Equal(0u, stagedReplay.Snapshot.MapId);

            var arrivedReplay = PacketLogReplayService.Replay(arrivedPath);

            Assert.Equal(2, arrivedReplay.ReplayedLines);
            Assert.Equal(50u, arrivedReplay.SceneOwner.Metadata.CurrentMapId);
            Assert.Equal(50u, arrivedReplay.Snapshot.MapId);
        }
        finally
        {
            File.Delete(stagedPath);
            File.Delete(arrivedPath);
        }
    }

    [Fact]
    public void Stream_Replay_Runtime_Sink_Path_Matches_Direct_Scene_Processor()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", entries.Select(BuildStreamReplayLine).ToArray());
        try
        {
            var sinkReplay = PacketLogReplayService.Replay(path);
            var directReplay = ReplayStreamEntriesWithSceneProcessor(entries);

            AssertStreamReplayParity(directReplay, sinkReplay);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scene_Replay_Is_Deterministic()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", entries.Select(BuildStreamReplayLine).ToArray());
        try
        {
            var first = PacketLogReplayService.Replay(path);
            var second = PacketLogReplayService.Replay(path);

            AssertSnapshotParity(first.Snapshot, second.Snapshot);
            Assert.Equal(first.ReplayedLines, second.ReplayedLines);
            Assert.Equal(first.SkippedLines, second.SkippedLines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_Records_Baseline_Counters_For_Ingest_Snapshot_And_Summary()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", entries.Select(BuildStreamReplayLine).ToArray());
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.NotSame(PacketLogReplayBaselineCounters.Empty, replay.BaselineCounters);
            AssertBaselineCounter(replay.BaselineCounters.ReplayIngest);
            AssertBaselineCounter(replay.BaselineCounters.SnapshotCreation);
            AssertBaselineCounter(replay.BaselineCounters.CombatantSummaryCreation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_Reconstructs_Boss_Activity_From_Battle_And_Health_Frame()
    {
        CombatResourceRegistry.SetGameResources(
            BuildReplaySkillMap(),
            new Dictionary<int, NpcCatalogEntry>
            {
                [2310108] = new NpcCatalogEntry(2310108, "Boss", NpcCatalogKind.Boss)
            });

        var spawnLine = "2026-05-01T21:36:18.6000332+08:00|npc-spawn|16777343:60154->16777343:65518|kind=create-177|entity=56688|npcCode=2310108|data=BB014036F0BA030C2200DC3F230000021E6D28C7A8157F4600000B4300003441000801B08003B0800364000000640000000000000000000000000000000000000001000000000000000000000000000000000000000602110181969800FFFFFFFFFFFFFFFF8075D52ABB030000F0BA0301141E6D28C7A8157F4600000B431102BC060000FFFFFFFFFFFFFFFF8075D52ABB030000F0BA03011E6D28C7A8157F4600000B430100160000000301960000009600000098308BB500";
        var pulseLine = "2026-05-01T21:37:31.3028167+08:00|sidecar-3538|16777343:60154->16777343:65518|target=56688|state=0|source=0|data=0B3538F0BA030000";
        var activeSidecarLine = "2026-05-01T21:37:40.6643590+08:00|sidecar-3538|16777343:60154->16777343:65518|target=56688|state=0|source=11946|data=0C3538F0BA0300AA5D";
        var activeLine = "2026-05-01T21:37:40.6646403+08:00|battle-toggle|16777343:60154->16777343:65518|npcId=56688|active=True|tailLen=2|data=0B218DF0BA030001";
        var hpLine = "2026-05-01T21:37:41.4785243+08:00|remain-hp|16777343:60154->16777343:65518|npcId=56688|value0=2|value1=1|value2=0|value=22847|data=14008DF0BA030201003F590000";
        var exitLine = "2026-05-01T21:38:08.1582599+08:00|battle-toggle|16777343:60154->16777343:65518|npcId=56688|active=False|tailLen=2|data=0B218DF0BA030000";
        var spawnOnlyPath = WriteTempReplayLog("frame", spawnLine);
        var sidecarOnlyPath = WriteTempReplayLog("frame", spawnLine, pulseLine);
        var hpPath = WriteTempReplayLog("frame", spawnLine, pulseLine, activeSidecarLine, activeLine, hpLine);
        var exitPath = WriteTempReplayLog("frame", spawnLine, pulseLine, activeSidecarLine, activeLine, hpLine, exitLine);
        try
        {
            var spawnOnlyReplay = PacketLogReplayService.Replay(spawnOnlyPath);
            var afterSpawn = DateTimeOffset.Parse("2026-05-01T21:36:18.7000332+08:00").ToUnixTimeMilliseconds();
            Assert.False(spawnOnlyReplay.SceneOwner.BossFocus.TryGetObservedBoss(afterSpawn, 2_000, out _));

            var sidecarOnlyReplay = PacketLogReplayService.Replay(sidecarOnlyPath);
            var afterPulse = DateTimeOffset.Parse("2026-05-01T21:37:31.4028167+08:00").ToUnixTimeMilliseconds();
            Assert.False(sidecarOnlyReplay.SceneOwner.BossFocus.TryGetObservedBoss(afterPulse, 2_000, out _));

            var replay = PacketLogReplayService.Replay(hpPath);
            var afterHp = DateTimeOffset.Parse("2026-05-01T21:37:41.5785243+08:00").ToUnixTimeMilliseconds();

            Assert.Equal(1, replay.ReplayedEventCounts["npc-spawn"]);
            Assert.Equal(2, replay.ReplayedEventCounts["sidecar-3538"]);
            Assert.Equal(1, replay.ReplayedEventCounts["battle-toggle"]);
            Assert.Equal(1, replay.ReplayedEventCounts["remain-hp"]);
            Assert.True(replay.SceneOwner.BossFocus.TryGetObservedBoss(afterHp, 2_000, out var boss));
            Assert.True(boss.HasHp);
            Assert.Equal(56_688, boss.InstanceId);
            Assert.Equal(22_847, boss.Hp);
            Assert.Equal(49_200, boss.MaxHp);
            var sceneReplay = PacketLogReplayService.Replay(hpPath);
            var applier = ApplySceneJournal(sceneReplay.SceneJournal);

            Assert.True(applier.BossFocus.TryGetObservedBoss(afterHp, 2_000, out var sceneBoss));
            Assert.True(sceneBoss.HasHp);
            Assert.Equal(56_688, sceneBoss.InstanceId);
            Assert.Equal(22_847, sceneBoss.Hp);
            Assert.Equal(49_200, sceneBoss.MaxHp);

            var exitReplay = PacketLogReplayService.Replay(exitPath);
            var afterExit = DateTimeOffset.Parse("2026-05-01T21:38:08.2582599+08:00").ToUnixTimeMilliseconds();
            Assert.False(exitReplay.SceneOwner.BossFocus.TryGetObservedBoss(afterExit, 2_000, out _));
            var sceneExitReplay = PacketLogReplayService.Replay(exitPath);
            var exitApplier = ApplySceneJournal(sceneExitReplay.SceneJournal);

            Assert.False(exitApplier.BossFocus.TryGetObservedBoss(afterExit, 2_000, out _));
        }
        finally
        {
            File.Delete(spawnOnlyPath);
            File.Delete(sidecarOnlyPath);
            File.Delete(hpPath);
            File.Delete(exitPath);
        }
    }

    [Theory]
    [InlineData("aion2flow.stream.20260411174533.log", 3, 0)]
    [InlineData("aion2flow.stream.20260411174739.log", 0, 3)]
    [InlineData("aion2flow.stream.20260411184521.log", 2, 2)]
    [InlineData("aion2flow.stream.20260411192501.log", 6, 1)]
    [InlineData("aion2flow.stream.20260411205158.log", 3, 2)]
    [InlineData("aion2flow.stream.20260411210634.log", 5, 0)]
    [InlineData("aion2flow.stream.20260411212441.log", 1, 0)]
    [InlineData("aion2flow.stream.20260411215842.log", 7, 0)]
    [InlineData("aion2flow.stream.20260411232425.log", 10, 3)]
    [InlineData("aion2flow.stream.20260411235759.log", 1, 1)]
    [InlineData("aion2flow.stream.20260412103519.log", 18, 7)]
    [InlineData("aion2flow.stream.20260412110721.log", 10, 7)]
    public void Replay_Reconstructs_April11_Incoming_Avoidance_Ground_Truth_From_Stream_Log(string fileName, int expectedEvades, int expectedInvincibles)
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == expectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == expectedInvincibles, summaryDump);
    }

    [Theory]
    [InlineData("aion2flow.stream.20260412103519.log", 18, 7)]
    [InlineData("aion2flow.stream.20260412110721.log", 10, 7)]
    public void Replay_Reconstructs_Reported_MultiSource_Invincibles_With_Full_Skill_Map(string fileName, int expectedEvades, int expectedInvincibles)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == expectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == expectedInvincibles, summaryDump);
    }

    [Fact]
    public void Replay_20260415_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 20211224, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingInvincibles == 8, $"OutgoingInvincibles={player.OutgoingInvincibles}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1305, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1313, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021557_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021557.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 7920567, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1170, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1170, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021557 = 583068;
        Assert.True(player.IncomingHealing == gameReportedHealing021557, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021557}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021406_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021406.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles} dmg={player.OutgoingDamage}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 3961239, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 525, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 525, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021406 = 141564;
        Assert.True(player.IncomingHealing == gameReportedHealing021406, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021406}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260417003456_Ground_AoE_Entities_Attributed_To_Owning_Player()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417003456.log"));

        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount} name={c.Value.Nickname}"));

        Assert.False(snapshot.Combatants.ContainsKey(99306), $"Ground AoE entity 99306 should not appear separately.\n{combatantDump}");
        Assert.False(snapshot.Combatants.ContainsKey(39022), $"Ground AoE entity 39022 should not appear separately.\n{combatantDump}");

        Assert.True(snapshot.Combatants.TryGetValue(664, out var cleric), $"Cleric 664 not found.\n{combatantDump}");
        Assert.True(cleric.DamageAmount == 3421060, $"Cleric damage={cleric.DamageAmount} expected=3421060\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260417023559_Cleric_Healing_No_False_Drain()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417023559.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;

        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount} name={c.Value.Nickname}"));

        var divineAuraSkillCode = 17150340;
        if (metrics.Skills.TryGetValue(divineAuraSkillCode, out var divineAura))
        {
            Assert.True(divineAura.DrainHealingAmount == 0,
                $"Divine Aura drain={divineAura.DrainHealingAmount} should be 0\n{combatantDump}");
        }

        const int gameReportedHealing023559 = 70963;
        Assert.Equal(gameReportedHealing023559, metrics.HealingAmount);
    }

    [Fact]
    public void Replay_20260417141813_Light_Of_Regeneration_Periodic_Healing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417141813.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;

        var lightOfRegenBaseSkill = 17090000;
        var lightOfRegenSkills = metrics.Skills
            .Where(kvp => kvp.Key / 10000 * 10000 == lightOfRegenBaseSkill || kvp.Key == lightOfRegenBaseSkill)
            .ToList();

        var skillDump = string.Join("\n", lightOfRegenSkills
            .Select(kvp => $"skill={kvp.Key} heal={kvp.Value.HealingAmount} periodic={kvp.Value.PeriodicHealingAmount} drain={kvp.Value.DrainHealingAmount}"));

        const int expectedLightOfRegenHealing = 4599;
        var totalLightOfRegenHealing = lightOfRegenSkills.Sum(kvp => kvp.Value.HealingAmount);
        Assert.True(totalLightOfRegenHealing == expectedLightOfRegenHealing,
            $"LightOfRegen total={totalLightOfRegenHealing} expected={expectedLightOfRegenHealing}\n{skillDump}");
    }

    [Fact]
    public void Replay_20260419204630_Instance_Clear_Restore_And_Incoming_Damage()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} dmg={player.OutgoingDamage} heal={player.IncomingHealing} inDmg={player.IncomingDamage} inHits={player.IncomingHits}\n{summaryDump}";

        Assert.True(player.IncomingDamage == 946, $"IncomingDamage={player.IncomingDamage} expected=946\n{diagDump}");
        Assert.True(player.IncomingHits == 2, $"IncomingHits={player.IncomingHits} expected=2\n{diagDump}");

        Assert.True(player.IncomingHealing == 48630, $"IncomingHealing={player.IncomingHealing} expected=48630 (HP instance-clear restore + Radiant Benediction, excludes MP restore)\n{diagDump}");
    }

    [Fact]
    public void Replay_20260423001617_Visible_Combatant_Damage_Contribution_Does_Not_Exceed_One_Hundred_Percent()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260423001617.log"));
        Assert.True(replay.ReplayedLines > 0);

        var visibleCombatants = replay.Snapshot.Combatants
            .Where(static pair => pair.Value.CharacterClass is not null)
            .OrderByDescending(static pair => pair.Value.DamageAmount)
            .ToArray();

        var visibleContributionTotal = visibleCombatants.Sum(static pair => pair.Value.DamageContribution);
        var combatantDump = string.Join(
            Environment.NewLine,
            visibleCombatants.Select(static pair =>
                $"id={pair.Key} class={pair.Value.CharacterClass} dmg={pair.Value.DamageAmount} dps={pair.Value.DamagePerSecond:F2} share={pair.Value.DamageContribution:P4} name={pair.Value.Nickname}"));

        Assert.True(visibleCombatants.Length >= 2, combatantDump);
        Assert.True(visibleContributionTotal <= 1.0000000001d,
            $"visibleContributionTotal={visibleContributionTotal:P8}\n{combatantDump}");
    }

    [Fact]
    public void Replay_Does_Not_Synthesize_Regeneration_Healing_For_Known_Summons()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var summonLine = "2026-04-25T00:58:41.8295743+08:00|summon|16777343:58107->16777343:50695|kind=create-177|owner=314|summon=17755|npcCode=2920107|data=BC014036DB8A015F1000AB8E2C004002003F18C70079064700FC1A461A2C7E42302D01C249C249740B0000740B000000000000D078020064000000F04902000100000000000000A08601000000000090D00300010101110143AA9809FFFFFFFFFFFFFFFF8075D52ABB030000BA0207028FBB18C736A9054700001A460702063A010000FA0200000000EF030641657468657201000200000000000000000000000000000002CD004C040000D000B202000017000000D71D030000";
        var damageLine = "2026-04-25T00:58:46.2662741+08:00|damage|16777343:58107->16777343:50695|target=17755|source=24468|skillRaw=1232480|damage=16|skill=10000|baseSkill=1230000|charge=0|specs=2+4|skillName=Account Security|valueKind=Damage|data=230438DB8A01060094BF0160CE1200020240038B9D580701000000904E1001";
        var path = WriteTempReplayLog("frame", summonLine, damageLine);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.Equal(1, replay.ReplayedEventCounts["summon"]);
            Assert.Equal(1, replay.ReplayedEventCounts["damage"]);
            Assert.False(
                SceneReplayTestView.BySource(replay).TryGetValue(17755, out var summonPackets) &&
                summonPackets.Any(static packet => packet.SourceId == 17755 && packet.TargetId == 17755 && packet.Damage == 3));
            Assert.DoesNotContain(
                replay.Snapshot.Combatants,
                static pair => pair.Key == 314 && pair.Value.HealingAmount > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_20260426110459_Templar_DirectSelfHpRecovery_Packets_Are_Healing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426110459.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 6774;
        const int hpAbsorptionEffectSkillCode = 10000013;
        const int wardingStrikeSkillCode = 12351450;
        const int punishingStrikeSkillCode = 12060240;
        var selfHealingPackets = SceneReplayTestView.BySource(replay)[playerId]
            .Where(packet =>
                packet.SourceId == playerId &&
                packet.TargetId == playerId &&
                packet.EventKind == CombatEventKind.Healing)
            .ToArray();
        var packetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay)[playerId]
                .Where(static packet => packet.SourceId == packet.TargetId)
                .Select(static packet =>
                    $"skill={packet.SkillCode} raw={packet.OriginalSkillCode} damage={packet.Damage} event={packet.EventKind} value={packet.ValueKind} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} marker={packet.Marker} detail={packet.DetailRaw}"));

        var hpAbsorptionRecovery = selfHealingPackets
            .Where(static packet => packet.SkillCode == hpAbsorptionEffectSkillCode)
            .Sum(static packet => packet.Damage);
        var wardingStrikeRecovery = selfHealingPackets
            .Where(static packet => packet.SkillCode == wardingStrikeSkillCode)
            .Sum(static packet => packet.Damage);
        var punishingStrikeRecovery = selfHealingPackets
            .Where(static packet =>
                packet.OriginalSkillCode == punishingStrikeSkillCode &&
                packet.ValueKind == CombatValueKind.DrainHealing)
            .Sum(static packet => packet.Damage);
        Assert.True(hpAbsorptionRecovery == 5372, packetDump);
        Assert.True(wardingStrikeRecovery == 2492, packetDump);
        Assert.True(punishingStrikeRecovery == 1563, packetDump);

        var recognizedSelfRecovery = hpAbsorptionRecovery + wardingStrikeRecovery + punishingStrikeRecovery;
        Assert.Equal(9427, recognizedSelfRecovery);

        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount} name={pair.Value.Nickname}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);
        Assert.True(playerMetrics.HealingAmount == recognizedSelfRecovery,
            $"HealingAmount={playerMetrics.HealingAmount} expected={recognizedSelfRecovery}\n{packetDump}\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260426121726_Templar_Healing_Matches_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426121726.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 15980;
        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount} name={pair.Value.Nickname}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);

        var skillDump = string.Join(
            Environment.NewLine,
            playerMetrics.Skills
                .Where(static pair => pair.Value.HealingAmount > 0 || pair.Value.DrainHealingAmount > 0)
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair =>
                    $"skill={pair.Key} heal={pair.Value.HealingAmount} periodic={pair.Value.PeriodicHealingAmount} drain={pair.Value.DrainHealingAmount} times={pair.Value.HealingTimes}"));
        var packetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay)[playerId]
                .Where(static packet => packet.SourceId == packet.TargetId || packet.DrainHealAmount > 0)
                .Select(static packet =>
                    $"skill={packet.SkillCode} raw={packet.OriginalSkillCode} damage={packet.Damage} drain={packet.DrainHealAmount} event={packet.EventKind} value={packet.ValueKind} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} marker={packet.Marker} type={packet.Type} detail={packet.DetailRaw}"));

        long SkillDrainHealing(int skillCode) =>
            playerMetrics.Skills.TryGetValue(skillCode, out var metrics)
                ? metrics.DrainHealingAmount
                : 0;

        Assert.True(SkillDrainHealing(12010250) == 858, skillDump);
        Assert.True(SkillDrainHealing(12020250) == 911, skillDump);
        Assert.True(SkillDrainHealing(12030250) == 897, skillDump);
        Assert.True(SkillDrainHealing(12440250) == 2395, skillDump);
        Assert.True(SkillDrainHealing(12060240) == 784, skillDump);
        Assert.True(playerMetrics.HealingAmount == 31531,
            $"HealingAmount={playerMetrics.HealingAmount} expected=31531\n{skillDump}\n{packetDump}\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260426140354_SummonRestores_And_TargetSupport_Are_Classified_From_PacketShape()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426140354.log"));

        const int playerId = 4156;
        var playerOwnedIds = SceneReplayTestView.SummonOwnerByInstance(replay)
            .Where(static pair => pair.Value == playerId)
            .Select(static pair => pair.Key)
            .Append(playerId)
            .ToHashSet();
        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.DamageAmount + pair.Value.HealingAmount)
                .Select(static pair =>
                    $"id={pair.Key} name={pair.Value.Nickname} class={pair.Value.CharacterClass} damage={pair.Value.DamageAmount} heal={pair.Value.HealingAmount} shield={pair.Value.ShieldAmount}"));
        var summaryDump = string.Join(
            Environment.NewLine,
            replay.Combatants
                .OrderByDescending(static summary => summary.OutgoingDamage + summary.OutgoingHealing + summary.IncomingDamage)
                .Select(static summary =>
                    $"id={summary.CombatantId} name={summary.DisplayName} outDmg={summary.OutgoingDamage} inDmg={summary.IncomingDamage} outHeal={summary.OutgoingHealing} inHeal={summary.IncomingHealing} outShield={summary.OutgoingShield} inShield={summary.IncomingShield} attempts={summary.OutgoingAttempts}/{summary.IncomingAttempts} hits={summary.OutgoingHits}/{summary.IncomingHits}"));
        var summonDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.SummonOwnerByInstance(replay)
                .OrderBy(static pair => pair.Key)
                .Select(static pair => $"summon={pair.Key} owner={pair.Value}"));
        var targetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.ByTarget(replay)
                .Select(static pair => new
                {
                    Target = pair.Key,
                    Damage = pair.Value.Where(static packet => packet.EventKind == CombatEventKind.Damage).Sum(static packet => packet.Damage),
                    Count = pair.Value.Count
                })
                .OrderByDescending(static entry => entry.Damage)
                .Select(entry => $"target={entry.Target} damage={entry.Damage} packets={entry.Count}"));
        var playerIncomingDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.ByTarget(replay)[playerId]
                .Where(static packet => packet.EventKind == CombatEventKind.Damage)
                .GroupBy(static packet => new { packet.SourceId, packet.SkillCode, packet.OriginalSkillCode })
                .Select(static group => new
                {
                    group.Key.SourceId,
                    group.Key.SkillCode,
                    group.Key.OriginalSkillCode,
                    Damage = group.Sum(static packet => packet.Damage),
                    Attempts = group.Sum(static packet => packet.AttemptContribution),
                    Hits = group.Sum(static packet => packet.HitContribution),
                    Count = group.Count()
                })
                .OrderByDescending(static entry => entry.Damage)
                .Select(entry => $"source={entry.SourceId} skill={entry.SkillCode} raw={entry.OriginalSkillCode} damage={entry.Damage} attempts={entry.Attempts} hits={entry.Hits} packets={entry.Count}"));
        var playerHealingGroupDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.Packets(replay)
                .Where(packet => packet.SourceId == playerId &&
                                 packet.Timestamp >= replay.Snapshot.BattleStartTime &&
                                 packet.Timestamp <= replay.Snapshot.BattleEndTime &&
                                 packet.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing)
                .GroupBy(packet => new
                {
                    packet.SkillCode,
                    packet.OriginalSkillCode,
                    packet.ValueKind,
                    InWindow = true,
                    RawSource = packet.SourceId,
                    IsSelfTarget = packet.TargetId == playerId,
                    IsSummonTarget = playerOwnedIds.Contains(packet.TargetId) && packet.TargetId != playerId
                })
                .Select(group => new
                {
                    group.Key.SkillCode,
                    group.Key.OriginalSkillCode,
                    group.Key.ValueKind,
                    group.Key.InWindow,
                    group.Key.RawSource,
                    group.Key.IsSelfTarget,
                    group.Key.IsSummonTarget,
                    Damage = group.Sum(static packet => packet.Damage),
                    Count = group.Count()
                })
                .OrderByDescending(entry => entry.Damage)
                .Select(entry =>
                    $"skill={entry.SkillCode} raw={entry.OriginalSkillCode} value={entry.ValueKind} inWindow={entry.InWindow} rawSource={entry.RawSource} self={entry.IsSelfTarget} summonTarget={entry.IsSummonTarget} damage={entry.Damage} count={entry.Count}"));
        var spiritDescentPacketDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay).Values
                .SelectMany(static queue => queue)
                .Where(static packet => packet.SkillCode == 16990004 || packet.OriginalSkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        var diagnostics =
            $"target={replay.Snapshot.TargetObservation?.InstanceId} targetName={replay.Snapshot.TargetName} battle={replay.Snapshot.BattleStartTime}-{replay.Snapshot.BattleEndTime}\ncombatants:\n{combatantDump}\nsummaries:\n{summaryDump}\nsummons:\n{summonDump}\ntargets:\n{targetDump}\nplayer-healing-groups:\n{playerHealingGroupDump}\nspirit-descent-packets:\n{spiritDescentPacketDump}\nplayer-incoming:\n{playerIncomingDump}";

        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), diagnostics);
        Assert.False(
            playerMetrics.Skills.TryGetValue(16990004, out var spiritDescentRestore) && spiritDescentRestore.HealingAmount > 0,
            diagnostics);

        var playerSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == playerId);
        Assert.True(playerSummary.IncomingDamage == 13_347, diagnostics);

        foreach (var summonId in playerOwnedIds.Where(static id => id != playerId))
        {
            var summonSummary = Assert.Single(replay.Combatants, summary => summary.CombatantId == summonId);
            Assert.True(summonSummary.IncomingDamage == 0, diagnostics);
        }
    }

    [Fact]
    public void Replay_20260426031332_EnhanceSpiritBenediction_Self_And_Summon_Healing_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426031332.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 10277;
        const int summonId = 37299;
        const int enhanceSpiritBenedictionSkillCode = 16190000;
        var healingPackets = SceneReplayTestView.BySource(replay)[playerId]
            .Where(packet =>
                packet.SkillCode == enhanceSpiritBenedictionSkillCode &&
                packet.ValueKind == CombatValueKind.PeriodicHealing)
            .ToArray();
        var packetDump = string.Join(
            Environment.NewLine,
            healingPackets.Select(static packet =>
                $"target={packet.TargetId} damage={packet.Damage} mode={packet.PeriodicRelation}:{packet.PeriodicMode} value={packet.ValueKind}"));

        Assert.Equal(20, healingPackets.Length);
        Assert.True(healingPackets.Sum(static packet => packet.Damage) == 3438, packetDump);
        Assert.True(healingPackets.Where(static packet => packet.TargetId == playerId).Sum(static packet => packet.Damage) == 1737, packetDump);
        Assert.True(healingPackets.Where(static packet => packet.TargetId == summonId).Sum(static packet => packet.Damage) == 1701, packetDump);
        Assert.All(healingPackets, static packet => Assert.Equal(CombatEventKind.Healing, packet.EventKind));

        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount} name={pair.Value.Nickname}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);
        var skillDump = string.Join(
            Environment.NewLine,
            playerMetrics.Skills
                .Where(static pair => pair.Value.HealingAmount > 0)
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair =>
                    $"skill={pair.Key} heal={pair.Value.HealingAmount} periodic={pair.Value.PeriodicHealingAmount} drain={pair.Value.DrainHealingAmount} times={pair.Value.HealingTimes}"));
        var spiritDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay).Values
                .SelectMany(static queue => queue)
                .Where(static packet => packet.SkillCode == 16990004 || packet.OriginalSkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        Assert.True(playerMetrics.HealingAmount == 3438, $"HealingAmount={playerMetrics.HealingAmount} expected=3438 battle={replay.Snapshot.BattleStartTime}-{replay.Snapshot.BattleEndTime}\n{skillDump}\n{spiritDump}\n{combatantDump}");
        Assert.True(playerMetrics.Skills.TryGetValue(enhanceSpiritBenedictionSkillCode, out var skill), combatantDump);
        Assert.Equal(3438, skill.HealingAmount);
        Assert.Equal(3438, skill.PeriodicHealingAmount);

        var summaryDump = string.Join(
            Environment.NewLine,
            replay.Combatants.Select(static summary =>
                $"id={summary.CombatantId} outgoingHealing={summary.OutgoingHealing} incomingHealing={summary.IncomingHealing} outgoingDamage={summary.OutgoingDamage} incomingDamage={summary.IncomingDamage}"));
        var playerSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == playerId);
        var summonSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == summonId);
        Assert.True(playerSummary.OutgoingHealing == 3438, summaryDump);
        Assert.True(playerSummary.IncomingHealing == 1737, summaryDump);
        Assert.True(summonSummary.IncomingHealing == 1701, summaryDump);
    }

    [Fact]
    public void SceneReplay_VendoredLog_IsDeterministic_20260415()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log");
        var first = PacketLogReplayService.Replay(path);
        var second = PacketLogReplayService.Replay(path);

        AssertSnapshotParity(first.Snapshot, second.Snapshot);
        Assert.Equal(first.ReplayedLines, second.ReplayedLines);
        Assert.True(second.SceneJournal.Count > 0);
    }

    [Fact]
    public void SceneReplay_VendoredLog_IsDeterministic_20260419()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log");
        var first = PacketLogReplayService.Replay(path);
        var second = PacketLogReplayService.Replay(path);

        AssertSnapshotParity(first.Snapshot, second.Snapshot);
        Assert.Equal(first.ReplayedLines, second.ReplayedLines);
        Assert.True(second.SceneJournal.Count > 0);
    }

    [Fact]
    public void SceneReplay_JournalOrdinals_AreMonotonicallyIncreasing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log");
        var replay = PacketLogReplayService.Replay(path);

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        long prevOrdinal = -1;
        for (int i = 0; i < journal.Count; i++)
        {
            var entry = journal.Read(i);
            Assert.True(entry.Stamp.ObservationOrdinal > prevOrdinal, $"Ordinal {entry.Stamp.ObservationOrdinal} at index {i} not greater than {prevOrdinal}");
            prevOrdinal = entry.Stamp.ObservationOrdinal;
        }
    }

    [Fact]
    public void SceneReplay_BaselineCounters_AreRecorded()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", entries.Select(BuildStreamReplayLine).ToArray());
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.NotSame(PacketLogReplayBaselineCounters.Empty, replay.BaselineCounters);
            AssertBaselineCounter(replay.BaselineCounters.ReplayIngest);
            AssertBaselineCounter(replay.BaselineCounters.SnapshotCreation);
            AssertBaselineCounter(replay.BaselineCounters.CombatantSummaryCreation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SceneReplay_AlwaysExposesSceneJournal()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
        };

        var path = WriteTempReplayLog("stream", entries.Select(BuildStreamReplayLine).ToArray());
        try
        {
            var replay = PacketLogReplayService.Replay(path);
            Assert.True(replay.SceneJournal.Count > 0);
            Assert.NotNull(replay.SceneOwner);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempReplayLog(string logKind, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{logKind}.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static DomainEventApplier ApplySceneJournal(ObservedEventJournal journal)
    {
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), new CombatStore());
        applier.ApplyJournal(journal);
        return applier;
    }

    private static StreamReplayEntry CreateStreamReplayEntry(string timestamp, string fixture)
    {
        return new StreamReplayEntry(
            DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            HexHelper.FromFixture(fixture));
    }

    private static string BuildStreamReplayLine(StreamReplayEntry entry)
        => $"{entry.Timestamp:O}|dir=inbound|16777343:57080->16777343:49820|len={entry.Payload.Length}|data={Convert.ToHexString(entry.Payload)}";

    private static StreamProcessorReplayResult ReplayStreamEntriesWithSceneProcessor(IReadOnlyList<StreamReplayEntry> entries)
    {
        using var holder = SceneSinkFactory.CreateForReplay();
        var replayedLines = 0;
        var skippedLines = 0;
        using var processor = new PacketStreamProcessor(holder.Sink);
        var connection = new TcpConnection(16777343, 16777343, 57080, 49820);

        foreach (var entry in entries)
        {
            if (processor.AppendAndProcess(entry.Payload, connection, entry.Timestamp.ToUnixTimeMilliseconds()))
            {
                replayedLines++;
            }
            else
            {
                skippedLines++;
            }
        }

        holder.Sink.CompleteBatch(long.MaxValue);
        return new StreamProcessorReplayResult(
            entries.Count,
            replayedLines,
            skippedLines,
            holder.Owner.CreateSnapshot(),
            SceneReplayTestView.Packets(new PacketLogReplayResult(
                string.Empty,
                entries.Count,
                replayedLines,
                skippedLines,
                holder.Owner.CreateSnapshot(),
                holder.Journal,
                holder.Owner,
                [],
                new Dictionary<string, int>(),
                new Dictionary<string, int>())));
    }

    private static void AssertStreamReplayParity(StreamProcessorReplayResult expected, PacketLogReplayResult actual)
    {
        Assert.Equal(expected.TotalLines, actual.TotalLines);
        Assert.Equal(expected.ReplayedLines, actual.ReplayedLines);
        Assert.Equal(expected.SkippedLines, actual.SkippedLines);
        AssertSnapshotParity(expected.Snapshot, actual.Snapshot);
        Assert.Equal(
            BuildBattlePacketFacts(expected.Packets, expected.Snapshot),
            BuildBattlePacketFacts(SceneReplayTestView.Packets(actual), actual.Snapshot));
    }

    private static void AssertBaselineCounter(PacketLogReplayBaselineCounter counter)
    {
        Assert.True(counter.Elapsed >= TimeSpan.Zero);
        Assert.True(counter.AllocatedBytes >= 0);
    }

    private static void AssertSnapshotParity(DamageMeterSnapshot expected, DamageMeterSnapshot actual)
    {
        Assert.Equal(expected.BattleTime, actual.BattleTime);
        Assert.Equal(expected.BattleStartTime, actual.BattleStartTime);
        Assert.Equal(expected.BattleEndTime, actual.BattleEndTime);
        Assert.Equal(expected.TargetName, actual.TargetName);
        Assert.Equal(expected.MapId, actual.MapId);
        Assert.Equal(expected.MapInstanceId, actual.MapInstanceId);
        Assert.Equal(expected.TargetObservation?.InstanceId, actual.TargetObservation?.InstanceId);
        Assert.Equal(
            expected.Combatants.Keys.Order().ToArray(),
            actual.Combatants.Keys.Order().ToArray());

        foreach (var id in expected.Combatants.Keys)
        {
            AssertCombatantParity(expected.Combatants[id], actual.Combatants[id]);
        }
    }

    private static void AssertCombatantParity(CombatantMetrics expected, CombatantMetrics actual)
    {
        Assert.Equal(expected.Nickname, actual.Nickname);
        Assert.Equal(expected.CharacterClass, actual.CharacterClass);
        Assert.Equal(expected.DamageAmount, actual.DamageAmount);
        Assert.Equal(expected.HealingAmount, actual.HealingAmount);
        Assert.Equal(expected.PeriodicHealingAmount, actual.PeriodicHealingAmount);
        Assert.Equal(expected.DrainDamageAmount, actual.DrainDamageAmount);
        Assert.Equal(expected.DrainHealingAmount, actual.DrainHealingAmount);
        Assert.Equal(expected.RegenerationHealingAmount, actual.RegenerationHealingAmount);
        Assert.Equal(expected.ShieldAmount, actual.ShieldAmount);
        Assert.Equal(expected.ShieldTimes, actual.ShieldTimes);
        Assert.Equal(expected.ShieldAbsorbedAmount, actual.ShieldAbsorbedAmount);
        Assert.Equal(expected.ShieldAbsorbedTimes, actual.ShieldAbsorbedTimes);
        Assert.Equal(
            expected.Skills.Keys.Order().ToArray(),
            actual.Skills.Keys.Order().ToArray());

        foreach (var skillCode in expected.Skills.Keys)
        {
            AssertSkillParity(expected.Skills[skillCode], actual.Skills[skillCode]);
        }
    }

    private static void AssertSkillParity(SkillMetrics expected, SkillMetrics actual)
    {
        Assert.Equal(expected.SkillCode, actual.SkillCode);
        Assert.Equal(expected.EventKind, actual.EventKind);
        Assert.Equal(expected.PrimaryValueKind, actual.PrimaryValueKind);
        Assert.Equal(expected.DamageAmount, actual.DamageAmount);
        Assert.Equal(expected.PeriodicDamageAmount, actual.PeriodicDamageAmount);
        Assert.Equal(expected.PeriodicDamageTimes, actual.PeriodicDamageTimes);
        Assert.Equal(expected.HealingAmount, actual.HealingAmount);
        Assert.Equal(expected.HealingTimes, actual.HealingTimes);
        Assert.Equal(expected.SupportTimes, actual.SupportTimes);
        Assert.Equal(expected.PeriodicHealingAmount, actual.PeriodicHealingAmount);
        Assert.Equal(expected.PeriodicHealingTimes, actual.PeriodicHealingTimes);
        Assert.Equal(expected.DrainDamageAmount, actual.DrainDamageAmount);
        Assert.Equal(expected.DrainDamageTimes, actual.DrainDamageTimes);
        Assert.Equal(expected.DrainHealingAmount, actual.DrainHealingAmount);
        Assert.Equal(expected.DrainHealingTimes, actual.DrainHealingTimes);
        Assert.Equal(expected.RegenerationHealingAmount, actual.RegenerationHealingAmount);
        Assert.Equal(expected.RegenerationHealingTimes, actual.RegenerationHealingTimes);
        Assert.Equal(expected.ShieldAmount, actual.ShieldAmount);
        Assert.Equal(expected.ShieldTimes, actual.ShieldTimes);
        Assert.Equal(expected.ShieldAbsorbedAmount, actual.ShieldAbsorbedAmount);
        Assert.Equal(expected.ShieldAbsorbedTimes, actual.ShieldAbsorbedTimes);
        Assert.Equal(expected.CriticalTimes, actual.CriticalTimes);
        Assert.Equal(expected.Times, actual.Times);
        Assert.Equal(expected.AttemptTimes, actual.AttemptTimes);
        Assert.Equal(expected.EvadeTimes, actual.EvadeTimes);
        Assert.Equal(expected.InvincibleTimes, actual.InvincibleTimes);
        Assert.Equal(expected.MultiHitTimes, actual.MultiHitTimes);
        Assert.Equal(expected.BackTimes, actual.BackTimes);
        Assert.Equal(expected.PerfectTimes, actual.PerfectTimes);
        Assert.Equal(expected.SmiteTimes, actual.SmiteTimes);
        Assert.Equal(expected.ParryTimes, actual.ParryTimes);
        Assert.Equal(expected.BlockTimes, actual.BlockTimes);
        Assert.Equal(expected.EnduranceTimes, actual.EnduranceTimes);
        Assert.Equal(expected.RegenerationTimes, actual.RegenerationTimes);
        Assert.Equal(expected.DefensivePerfectTimes, actual.DefensivePerfectTimes);
    }

    private static string[] BuildBattlePacketFacts(IReadOnlyList<SceneReplayPacket> packets, DamageMeterSnapshot snapshot)
    {
        return packets
            .Where(packet => packet.Timestamp >= snapshot.BattleStartTime && packet.Timestamp <= snapshot.BattleEndTime)
            .Select(static packet =>
            {
                return string.Join(
                    "|",
                    packet.SourceId,
                    packet.TargetId,
                    packet.SourceId,
                    packet.TargetId,
                    packet.OriginalSkillCode,
                    packet.SkillCode,
                    packet.Damage,
                    packet.Modifiers,
                    packet.EventKind,
                    packet.ValueKind,
                    packet.EffectTag,
                    packet.Timestamp);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSummaryDump(IEnumerable<PacketLogCombatantSummary> summaries)
    {
        return string.Join(
            Environment.NewLine,
            summaries
                .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
                .ThenByDescending(static summary => summary.IncomingDamage)
                .Select(static summary =>
                    $"id={summary.CombatantId} incoming(evade={summary.IncomingEvades}, invincible={summary.IncomingInvincibles}, damage={summary.IncomingDamage}, hits={summary.IncomingHits}, attempts={summary.IncomingAttempts}) outgoing(damage={summary.OutgoingDamage}, hits={summary.OutgoingHits}, attempts={summary.OutgoingAttempts})"));
    }

    private static ParsedCombatPacket ParseDamagePacket(string hex)
    {
        var packet = HexHelper.Parse(hex);
        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);
        if (!ok)
        {
            var reader = new PacketSpanReader(packet);
            Assert.True(reader.TryReadVarInt(out _));
            ok = Packet0438DamageParser.TryParsePayload(packet.AsSpan()[reader.Offset..], out parsed, out _);
        }

        Assert.True(ok);

        return new ParsedCombatPacket
        {
            SourceId = parsed.SourceId,
            TargetId = parsed.TargetId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.SkillCodeRaw,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Damage = parsed.Damage,
            Modifiers = parsed.Modifiers
        };
    }

    private static SkillCollection BuildReplaySkillMap()
    {
        return
        [
            new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
            new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ];
    }

    private sealed record StreamProcessorReplayResult(
        int TotalLines,
        int ReplayedLines,
        int SkippedLines,
        DamageMeterSnapshot Snapshot,
        IReadOnlyList<SceneReplayPacket> Packets);

    private sealed record StreamReplayEntry(DateTimeOffset Timestamp, byte[] Payload);
}
