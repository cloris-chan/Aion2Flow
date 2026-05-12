using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class NpcCatalogSceneTests
{
    [Fact]
    public void LoadSkillMap_Also_Loads_NpcCatalog()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");

        Assert.True(CombatResourceRegistry.TryResolveNpcCatalogEntry(2000002, out var entry));
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
        Assert.Equal(expected, CombatResourceRegistry.ResolveNpcKind(kind));
    }

    [Fact]
    public void SceneSnapshot_Uses_NpcCatalog_Name_When_NpcCode_Set()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        const int playerId = 2007;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, npcCode);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1_000,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1,
            Timestamp = 1_050
        });

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload(snapshot);

        Assert.True(catalog.TryGetValue(npcCode, out var expectedEntry));
        Assert.Equal(expectedEntry.Name, snapshot.TargetName);
        var detail = archive.CreateDetailDelta(playerId);
        Assert.True(detail.DisplayNames.TryGetValue(npcInstanceId, out var displayName));
        Assert.Equal(expectedEntry.Name, displayName);
    }

    [Fact]
    public void SceneSnapshot_Falls_Back_To_NpcCode_When_Catalog_Missing()
    {
        const int npcInstanceId = 5555;
        const int unknownNpcCode = 2999999;
        const int playerId = 2007;
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, unknownNpcCode);
        scene.AppendNpcName(unknownNpcCode, "CustomNpcName");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1_000,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1,
            Timestamp = 1_050
        });

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload(snapshot);

        Assert.Equal(string.Empty, snapshot.TargetName);
        var detail = archive.CreateDetailDelta(playerId);
        Assert.True(detail.DisplayNames.TryGetValue(npcInstanceId, out var displayName));
        Assert.Equal($"NPC-{unknownNpcCode}", displayName);
    }

    [Fact]
    public void SceneSnapshot_Preserves_Explicit_Nickname_Over_NpcCatalog_Name()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        const int playerId = 2007;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, npcCode);
        scene.AppendNickname(npcInstanceId, "PlayerNick");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1_000,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcInstanceId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1,
            Timestamp = 1_050
        });

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload(snapshot);

        Assert.Equal(catalog[npcCode].Name, snapshot.TargetName);
        var detail = archive.CreateDetailDelta(playerId);
        Assert.True(detail.DisplayNames.TryGetValue(npcInstanceId, out var displayName));
        Assert.Equal("PlayerNick", displayName);
    }

    [Fact]
    public void SceneSnapshot_Clears_Previously_Inferred_Class_When_Combatant_Is_Identified_As_Npc()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());
        using var scene = new SceneTestHarness();
        const int npcInstanceId = 19945;
        const int targetId = 14037;

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = npcInstanceId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 1_841,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = npcInstanceId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 1,
            Timestamp = 1_050
        });

        var beforeNpcIdentity = scene.CreateSnapshot();
        Assert.True(beforeNpcIdentity.Combatants.TryGetValue(npcInstanceId, out var initiallyInferred));
        Assert.Equal(CharacterClass.Elementalist, initiallyInferred.CharacterClass);

        scene.AppendNpcCode(npcInstanceId, 2100350);
        scene.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        var afterNpcIdentity = scene.CreateSnapshot();
        Assert.True(afterNpcIdentity.Combatants.TryGetValue(npcInstanceId, out var npcCombatant));
        Assert.Null(npcCombatant.CharacterClass);
    }

    [Fact]
    public void SceneSnapshot_Returns_Numeric_Id_Without_NpcCode()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        using var scene = new SceneTestHarness();
        const int sourceId = 12345;
        const int targetId = 54321;

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1_000,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            OriginalSkillCode = 17070000,
            SkillCode = 17070000,
            Damage = 1,
            Timestamp = 1_050
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        Assert.Equal("12345", combatant.Nickname);
    }

    [Fact]
    public void ArchivePayload_Preserves_Npc_State_For_Target_Entity()
    {
        const int npcEntityId = 16710;
        const int npcCode = 2980179;
        const int playerId = 9206;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcEntityId, npcCode);
        scene.AppendNpcName(npcCode, catalog[npcCode].Name);
        scene.AppendNpcKind(npcEntityId, NpcKind.Monster);
        scene.AppendNickname(playerId, "TestPlayer");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 36_358,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 1,
            Timestamp = 1_050,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload(snapshot);

        var identity = Assert.Single(archive.Entities, static e => e.EntityId == npcEntityId);
        Assert.Equal(npcCode, identity.NpcCode);
        Assert.Equal(NpcKind.Monster, identity.Kind);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcEntityId, out var scopedNpcCode));
        Assert.Equal(npcCode, scopedNpcCode);
        var detail = archive.CreateDetailDelta(playerId);
        Assert.True(detail.DisplayNames.TryGetValue(npcEntityId, out var displayName));
        Assert.Equal(catalog[npcCode].Name, displayName);
    }

    [Fact]
    public void LiveReadModel_Reset_Preserves_Npc_Identity_For_Next_Battle_And_Archive()
    {
        const int npcEntityId = 17952;
        const int npcCode = 2980159;
        const int playerId = 11616;
        const int battle1Target = 33541;
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNickname(playerId, "TestPlayer");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = battle1Target,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 50_000,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendNpcCode(npcEntityId, npcCode);
        scene.AppendNpcName(npcCode, catalog[npcCode].Name);
        scene.AppendNpcKind(npcEntityId, CombatResourceRegistry.ResolveNpcKind(catalog[npcCode].Kind));

        Assert.True(scene.TryGetNpcRuntimeState(npcEntityId, out var preResetState));
        Assert.Equal(npcCode, preResetState.NpcCode);

        scene.Owner.ResetCombat(Guid.NewGuid(), scene.Owner.AppliedObservationOrdinal + 1);

        Assert.True(scene.TryGetNpcRuntimeState(npcEntityId, out var postResetState));
        Assert.Equal(npcCode, postResetState.NpcCode);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17070000,
            OriginalSkillCode = 17070240,
            Damage = 14_547,
            Timestamp = 10_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcEntityId,
            SkillCode = 17730000,
            OriginalSkillCode = 17730001,
            Damage = 4_092,
            Timestamp = 10_100,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload(snapshot);

        Assert.Equal(catalog[npcCode].Name, snapshot.TargetName);
        var identity = Assert.Single(archive.Entities, static e => e.EntityId == npcEntityId);
        Assert.Equal(npcCode, identity.NpcCode);
        var detail = archive.CreateDetailDelta(playerId);
        Assert.True(detail.DisplayNames.TryGetValue(npcEntityId, out var displayName));
        Assert.Equal(catalog[npcCode].Name, displayName);
    }

    [Fact]
    public void FullSession_Replay_Resolves_Entity_17952_Npc_Name()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), catalog);

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

        Assert.True(SceneReplayTestView.TryGetNpcRuntimeState(result, entityId, out var state), $"Replay scene must have NPC state for entity {entityId}");
        Assert.Equal(npcCode, state.NpcCode);

        var displayName = SceneReplayTestView.ResolveDisplayName(result, entityId);
        Assert.NotEqual(entityId.ToString(), displayName);
        Assert.Equal(catalog[npcCode].Name, displayName);
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
        CombatResourceRegistry.SetGameResources([], catalog);

        var npcSpawnLine = $"2026-04-24T23:09:45.3164516+08:00|npc-spawn|16777343:56119->16777343:49300|kind=create-198|entity={entityId}|npcCode={npcCode}|data=00";
        var observedLine = $"2026-04-24T23:10:13.4000000+08:00|state-4536|16777343:56119->16777343:49300|source={entityId}|value0=0|tailLen=0|data=094536922200";
        var stateLine = $"2026-04-24T23:10:13.4172863+08:00|state-2136|16777343:56119->16777343:49300|target={entityId}|seq=6|value0={sceneStateValue}|value1=7602133|value2=0|value3=0x41c568f4|value4=0x4537c974|value5=0x42800000|value6=0xc2b40000|value7=2|tailMarker=0x004f|tailLen=7|data=33213606000000430D0300D5FF730000000000F468C54174C93745000080420000B4C202000000000000000000004F00";
        var path = Path.Combine(Path.GetTempPath(), $"replay-npc-state-{Guid.NewGuid()}.log");
        File.WriteAllLines(path, [npcSpawnLine, observedLine, stateLine]);
        try
        {
            var result = PacketLogReplayService.Replay(path);

            Assert.True(SceneReplayTestView.TryGetNpcRuntimeState(result, entityId, out var state), $"Replay scene missing NPC state for entity {entityId}");
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
        "2026-04-15T17:00:16.6449720+08:00|damage|16777343:60362->16777343:55221|target=16710|source=9206|skillRaw=17070240|damage=36358|skill=17070240|baseSkill=17070000|charge=0|specs=2+4|skillName=Chain of Torment|valueKind=Damage|data=260438C682011600F647A07804010D0318008B1EBF6501000000DF8801869C0201")]
    [InlineData(
        17858, 2980049,
        "2026-04-15T17:28:42.6249268+08:00|npc-spawn|16777343:60362->16777343:59238|kind=create-198|entity=17858|npcCode=2980049|data=E1014036C28B01042200D1782D000002000040C00000C040000090420000B44200400180EA3080EA3064000000640000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000C28B010110000040C00000C04000009042110284969800FFFFFFFFFFFFFFFF8075D52ABB030000C28B0101000040C00000C0400000904211039AF22101FFFFFFFFFFFFFFFF8075D52ABB030000C28B0101000040C00000C0400000904201002D00000003019600000096000000472C0C8400",
        "2026-04-15T17:28:48.9762913+08:00|damage|16777343:60362->16777343:59238|target=17858|source=9849|skillRaw=17070240|damage=27944|skill=17070240|baseSkill=17070000|charge=0|specs=2+4|skillName=Chain of Torment|valueKind=Damage|data=290438C28B013600F94CA07804012E0308008B1EBF6501000000F78001A8DA010101EA15")]
    public void Replay_NpcSpawn_And_Damage_Resolves_Npc_Display_Name(int entityId, int npcCode, string npcSpawnLine, string damageLine)
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), catalog);
        var path = Path.Combine(Path.GetTempPath(), $"replay-npc-{Guid.NewGuid()}.log");
        File.WriteAllLines(path, [npcSpawnLine, damageLine]);
        try
        {
            var result = PacketLogReplayService.Replay(path);

            Assert.True(result.ReplayedEventCounts.ContainsKey("npc-spawn"));
            Assert.True(result.ReplayedEventCounts.ContainsKey("damage"));

            Assert.True(SceneReplayTestView.TryGetNpcRuntimeState(result, entityId, out var state), $"Replay scene missing NPC state for entity {entityId}");
            Assert.Equal(npcCode, state.NpcCode);

            var displayName = SceneReplayTestView.ResolveDisplayName(result, entityId);
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

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(17070000, "Chain of Torment", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "Additional Strike", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ];
    }
}
