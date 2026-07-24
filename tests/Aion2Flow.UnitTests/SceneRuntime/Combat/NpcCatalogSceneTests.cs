using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

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
    [InlineData(NpcCatalogKind.TrainingDummy, NpcKind.TrainingDummy)]
    [InlineData(NpcCatalogKind.Unknown, NpcKind.Unknown)]
    [InlineData(NpcCatalogKind.Object, NpcKind.Unknown)]
    public void ResolveNpcKind_Maps_Catalog_Kind_Enum(NpcCatalogKind kind, NpcKind expected)
    {
        Assert.Equal(expected, CombatResourceRegistry.ResolveNpcKind(kind));
    }

    [Fact]
    public void ResolveNpcKind_Maps_TrainingScarecrow_To_TrainingDummy_Not_Boss()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2500075, out var scarecrow));
        Assert.Equal(NpcCatalogKind.TrainingDummy, scarecrow.Kind);
        Assert.Equal(NpcKind.TrainingDummy, CombatResourceRegistry.ResolveNpcKind(scarecrow.Kind));
        Assert.NotEqual(NpcKind.Boss, CombatResourceRegistry.ResolveNpcKind(scarecrow.Kind));
    }

    [Fact]
    public void ApplyNpcCatalog_Applies_Summon_Kind_From_Catalog()
    {
        const int summonId = 81994;
        const int summonNpcCode = 2920190;
        CombatResourceTestFixture.SetResources([], new Dictionary<int, NpcDisplayEntry>
        {
            [summonNpcCode] = new(summonNpcCode, "古代精靈", NpcCatalogKind.Summon, NpcHpDisplayScale.Normal)
        });
        using var scene = new SceneTestHarness();
        var writer = new SceneObservationWriter(scene.Sink);

        writer.ApplyNpcCatalog(Source(1_000), summonId, summonNpcCode, requireCatalogEntry: true);
        scene.Owner.Refresh();

        Assert.True(scene.Owner.Entities.TryGet(summonId, out var entity));
        Assert.Equal(summonNpcCode, entity.NpcCode);
        Assert.Equal(NpcKind.Summon, entity.Kind);
        Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(summonId, out var npcCode));
        Assert.Equal(summonNpcCode, npcCode);
    }

    [Fact]
    public void SceneArchiveScope_Captures_NpcCode_When_NpcCode_Set()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        const int playerId = 2007;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, npcCode);
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1_000, 1_000);
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        Assert.True(catalog.TryGetValue(npcCode, out var expectedEntry));
        Assert.Equal(npcInstanceId, snapshot.TargetObservation?.InstanceId);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcInstanceId, out var scopedNpcCode));
        Assert.Equal(npcCode, scopedNpcCode);
        Assert.Equal(expectedEntry.Name, catalog[scopedNpcCode].Name);
    }

    [Fact]
    public void SceneArchiveScope_Captures_Known_NpcCode_Outside_Combat_Details_For_Playback()
    {
        const int playbackOnlyEntityId = 17329;
        const int playbackOnlyNpcCode = 2920804;
        const int targetEntityId = 29994;
        const int targetNpcCode = 2400032;
        const int playerId = 2007;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(playbackOnlyEntityId, playbackOnlyNpcCode);
        scene.AppendNpcKind(playbackOnlyEntityId, CombatResourceRegistry.ResolveNpcKind(catalog[playbackOnlyNpcCode].Kind));
        scene.AppendNpcCode(targetEntityId, targetNpcCode);
        AppendDamage(scene, playerId, targetEntityId, 17070000, 1_000, 1_000);
        AppendDamage(scene, playerId, targetEntityId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        Assert.True(archive.IdentityScope.TryGetNpcCode(playbackOnlyEntityId, out var scopedNpcCode));
        Assert.Equal(playbackOnlyNpcCode, scopedNpcCode);
        Assert.Equal(catalog[playbackOnlyNpcCode].Name, catalog[scopedNpcCode].Name);
        var identity = Assert.Single(archive.Entities, static e => e.EntityId == playbackOnlyEntityId);
        Assert.Equal(playbackOnlyNpcCode, identity.NpcCode);
    }

    [Fact]
    public void SceneArchiveScope_Captures_NpcCode_When_Catalog_Missing()
    {
        const int npcInstanceId = 5555;
        const int unknownNpcCode = 2999999;
        const int playerId = 2007;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, unknownNpcCode);
        scene.AppendNpcName(unknownNpcCode, "CustomNpcName");
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1_000, 1_000);
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        Assert.Equal(npcInstanceId, snapshot.TargetObservation?.InstanceId);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcInstanceId, out var scopedNpcCode));
        Assert.Equal(unknownNpcCode, scopedNpcCode);
    }

    [Fact]
    public void SceneArchiveScope_Captures_ExplicitPcMetadata_AlongsideNpcCode()
    {
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;
        const int playerId = 2007;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcInstanceId, npcCode);
        scene.AppendNickname(npcInstanceId, "PlayerNick");
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1_000, 1_000);
        AppendDamage(scene, playerId, npcInstanceId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        Assert.Equal(npcInstanceId, snapshot.TargetObservation?.InstanceId);
        Assert.True(archive.IdentityScope.TryGetPcMetadata(npcInstanceId, out var pc));
        Assert.Equal("PlayerNick", pc.Nickname);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcInstanceId, out var scopedNpcCode));
        Assert.Equal(npcCode, scopedNpcCode);
    }

    [Fact]
    public void SceneSnapshot_Clears_Previously_Inferred_Class_When_Combatant_Is_Identified_As_Npc()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());
        using var scene = new SceneTestHarness();
        const int npcInstanceId = 19945;
        const int targetId = 14037;

        AppendDamage(scene, npcInstanceId, targetId, 16010000, 1_841, 1_000);
        AppendDamage(scene, npcInstanceId, targetId, 16010000, 1, 1_050);

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
    public void SceneSnapshot_Keeps_Combatant_Facts_Without_DisplayName()
    {
        CombatResourceTestFixture.SetResources(BuildSkillMap(), new Dictionary<int, NpcDisplayEntry>());
        using var scene = new SceneTestHarness();
        const int sourceId = 12345;
        const int targetId = 54321;

        AppendDamage(scene, sourceId, targetId, 17070000, 1_000, 1_000);
        AppendDamage(scene, sourceId, targetId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        Assert.Equal(1_001, combatant.DamageAmount);
    }

    [Fact]
    public void ArchivePayload_Preserves_Npc_State_For_Target_Entity()
    {
        const int npcEntityId = 16710;
        const int npcCode = 2980179;
        const int playerId = 9206;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(npcEntityId, npcCode);
        scene.AppendNpcName(npcCode, catalog[npcCode].Name);
        scene.AppendNpcKind(npcEntityId, NpcKind.Monster);
        scene.AppendNickname(playerId, "TestPlayer");
        AppendDamage(scene, playerId, npcEntityId, 17070000, 36_358, 1_000);
        AppendDamage(scene, playerId, npcEntityId, 17070000, 1, 1_050);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        var identity = Assert.Single(archive.Entities, static e => e.EntityId == npcEntityId);
        Assert.Equal(npcCode, identity.NpcCode);
        Assert.Equal(NpcKind.Monster, identity.Kind);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcEntityId, out var scopedNpcCode));
        Assert.Equal(npcCode, scopedNpcCode);
        Assert.Equal(catalog[npcCode].Name, catalog[scopedNpcCode].Name);
    }

    [Fact]
    public void LiveReadModel_Reset_Preserves_Npc_Identity_For_Next_Battle_And_Archive()
    {
        const int npcEntityId = 17952;
        const int npcCode = 2980159;
        const int playerId = 11616;
        const int battle1Target = 33541;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(BuildSkillMap(), catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNickname(playerId, "TestPlayer");
        AppendDamage(scene, playerId, battle1Target, 17070000, 50_000, 1_000);
        scene.AppendNpcCode(npcEntityId, npcCode);
        scene.AppendNpcName(npcCode, catalog[npcCode].Name);
        scene.AppendNpcKind(npcEntityId, CombatResourceRegistry.ResolveNpcKind(catalog[npcCode].Kind));

        Assert.True(scene.TryGetNpcRuntimeState(npcEntityId, out var preResetState));
        Assert.Equal(npcCode, preResetState.NpcCode);

        scene.Owner.ResetCombat(Guid.NewGuid(), scene.Owner.AppliedObservationOrdinal + 1);

        Assert.True(scene.TryGetNpcRuntimeState(npcEntityId, out var postResetState));
        Assert.Equal(npcCode, postResetState.NpcCode);

        AppendDamage(scene, playerId, npcEntityId, 17070000, 14_547, 10_000);
        AppendDamage(scene, playerId, npcEntityId, 17730000, 4_092, 10_100);

        var snapshot = scene.CreateSnapshot();
        var archive = scene.Owner.CreateArchivePayload();

        Assert.Equal(npcEntityId, snapshot.TargetObservation?.InstanceId);
        var identity = Assert.Single(archive.Entities, static e => e.EntityId == npcEntityId);
        Assert.Equal(npcCode, identity.NpcCode);
        Assert.True(archive.IdentityScope.TryGetNpcCode(npcEntityId, out var scopedNpcCode));
        Assert.Equal(npcCode, scopedNpcCode);
    }

    [Fact]
    public void Scene_State_Catalog_Probe_Does_Not_Overwrite_NpcSpawn_Code_When_Value_Misses_Catalog()
    {
        const int entityId = 4370;
        const int npcCode = 2980049;
        const int sceneStateValue = 200003;
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        Assert.True(catalog.ContainsKey(npcCode));
        Assert.False(catalog.ContainsKey(sceneStateValue));
        CombatResourceTestFixture.SetResources([], catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(entityId, npcCode);
        scene.AppendNpcKind(entityId, CombatResourceRegistry.ResolveNpcKind(catalog[npcCode].Kind));
        scene.AppendNpc2136State(entityId, sequence: 6, value0: sceneStateValue);

        Assert.True(scene.TryGetNpcRuntimeState(entityId, out var state), $"Replay scene missing NPC state for entity {entityId}");
        Assert.Equal(npcCode, state.NpcCode);
        Assert.Equal((long)sceneStateValue, state.Value2136);
    }

    [Theory]
    [InlineData(16710, 2980179, 9206, 36_358)]
    [InlineData(17858, 2980049, 9849, 27_944)]
    public void Scene_NpcSpawn_And_Damage_Resolves_Npc_Identity(int entityId, int npcCode, int sourceId, int damage)
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;
        CombatResourceTestFixture.SetResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, catalog);
        using var scene = new SceneTestHarness();

        scene.AppendNpcCode(entityId, npcCode);
        scene.AppendNpcKind(entityId, CombatResourceRegistry.ResolveNpcKind(catalog[npcCode].Kind));
        AppendDamage(scene, sourceId, entityId, 17070000, damage, 1_000);
        AppendDamage(scene, sourceId, entityId, 17070000, 1, 1_050);

        Assert.True(scene.TryGetNpcRuntimeState(entityId, out var state), $"Scene missing NPC state for entity {entityId}");
        Assert.Equal(npcCode, state.NpcCode);

        var snapshot = scene.CreateSnapshot();
        Assert.Equal(entityId, snapshot.TargetObservation?.InstanceId);
    }

    private static SkillDisplayCatalog BuildSkillMap()
    {
        return
        [
            new SkillDisplayEntry(17070000, "Chain of Torment", SkillCategory.Cleric, SkillSourceType.PcSkill),
            new SkillDisplayEntry(17730000, "Additional Strike", SkillCategory.Cleric, SkillSourceType.PcSkill)
        ];
    }

    private static void AppendDamage(
        SceneTestHarness scene,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in observation, timestamp);
    }

    private static PacketObservationSource Source(long timestamp) =>
        new(timestamp, 1, 0, 0, 0, default);
}
