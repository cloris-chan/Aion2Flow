using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.Tests.SceneRuntime;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class MainViewModelCombatantFilterTests
{
    [Theory]
    [InlineData(1010u, 0u, 200003u, 113515u, true, "map-transition")]
    [InlineData(1010u, 0u, 1010u, 0u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113515u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113526u, true, "map-instance-transition")]
    [InlineData(200003u, 0u, 200003u, 113515u, true, "map-instance-transition")]
    [InlineData(0u, 0u, 20u, 0u, true, "map-transition")]
    [InlineData(1010u, 0u, 20u, 0u, true, "map-transition")]
    [InlineData(20u, 0u, 1010u, 0u, true, "map-transition")]
    [InlineData(1010u, 0u, 130u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 1010u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 50u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 0u, 0u, false, "")]
    [InlineData(0u, 100u, 0u, 101u, true, "map-instance-transition")]
    [InlineData(600002u, 396972u, 1010u, 0u, true, "map-transition")]
    [InlineData(1010u, 0u, 1020u, 0u, true, "map-transition")]
    [InlineData(1010u, 0u, 500020u, 0u, true, "map-transition")]
    [InlineData(500020u, 0u, 1010u, 0u, true, "map-transition")]
    [InlineData(600011u, 679397u, 600012u, 679397u, true, "map-transition")]
    public void Map_Transitions_Select_Automatic_Reset_Scope(
        uint previousMapId,
        uint previousInstanceId,
        uint latestMapId,
        uint latestInstanceId,
        bool expected,
        string expectedReason)
    {
        var previous = SceneSnapshotTestFactory.Create(
            mapId: previousMapId,
            mapInstanceId: previousInstanceId,
            encounterTime: 12_000,
            combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(
            mapId: latestMapId,
            mapInstanceId: latestInstanceId);

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.Equal(expected, result);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void Map_Change_Without_Battle_Does_Not_Trigger_Reset()
    {
        var previous = SceneSnapshotTestFactory.Create(mapId: 600002);

        var latest = SceneSnapshotTestFactory.Create(mapId: 1010);

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.False(result);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Predictive_MapId_Flip_Without_Confirmation_Does_Not_Archive()
    {
        var previous = SceneSnapshotTestFactory.Create(
            mapId: 1010,
            encounterTime: 12_000,
            combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(mapId: 1010);

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Scene_Transition_Revision_Does_Not_Archive_When_Map_And_Instance_Are_Unchanged()
    {
        var previous = SceneSnapshotTestFactory.Create(
            mapId: 1010,
            mapInstanceId: 0,
            sceneTransitionRevision: 7,
            encounterTime: 12_000,
            combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(
            mapId: 1010,
            mapInstanceId: 0,
            sceneTransitionRevision: 8);

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Sub_Instance_Boss_Room_Does_Not_Archive()
    {
        var previous = SceneSnapshotTestFactory.Create(
            mapId: 910036,
            mapInstanceId: 113515,
            encounterTime: 12_000,
            combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(
            mapId: 910036,
            mapInstanceId: 113515);

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void ShouldDisplayCombatant_Hides_Known_Npc_Even_If_Class_Was_Previously_Inferred()
    {
        const int npcInstanceId = 19945;
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNpc(npcInstanceId, 2100350, NpcKind.Monster);
        fixture.AppendSceneDamage(npcInstanceId, 100, 11000010, 200, 1_000, 1);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.DoesNotContain(fixture.ViewModel.Combatants, x => x.Id == npcInstanceId);
    }

    [Fact]
    public void ShouldDisplayCombatant_Hides_Combatants_Without_Player_Class()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneDamage(38924, 100, 11000010, 200, 1_000, 1);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.DoesNotContain(fixture.ViewModel.Combatants, x => x.Id == 38924);
    }

    [Fact]
    public void ShouldDisplayCombatant_Keeps_Player_Class_When_Not_Npc()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(12669, "Player", 400, 1_000, 2_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Contains(fixture.ViewModel.Combatants, x => x.Id == 12669);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DisplaysSceneSnapshot()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal(400, row.Damage);
        Assert.Equal(2d, fixture.ViewModel.EncounterTimeSeconds);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_UsesSceneDisplayName()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Name", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal("Scene Name", fixture.ViewModel.DisplayContext!.ResolveEntityName(row.Id));
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_FiltersNpcFromSceneStore()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneNpc(900_002, 2_100_350, NpcKind.Monster);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_UsesSceneBossFocus()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 25_000, 50_000, 5_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.Equal(900_002, focus.InstanceId);
        Assert.Equal("NPC-2100351", fixture.ViewModel.DisplayContext!.ResolveNpcName(focus.InstanceId));
        Assert.Equal(25_000, focus.Hp);
        Assert.Equal(50_000, focus.MaxHp);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_RefreshesLiveDetailFromSceneProjection()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);

        Assert.Equal(300, fixture.ViewModel.CombatantDetails.SelectedCombatantId);
        Assert.Equal("Scene Player", fixture.ViewModel.CombatantDetails.DisplayContext!.ResolveEntityName(300));
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.Equal(2, fixture.ViewModel.CombatantDetails.OutgoingDamage.Hits);
        Assert.Single(fixture.ViewModel.CombatantDetails.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DoesNotRebuildLiveDetailForIrrelevantSceneCombat()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);
        var row = fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0];

        fixture.AppendSceneEncounter(301, "Other Player", 600, 6_000, 7_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Same(row, fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0]);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_RebuildsLiveDetailForRelevantSceneCombat()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);
        var firstTotal = fixture.ViewModel.CombatantDetails.OutgoingDamage.Total;

        fixture.AppendSceneDamage(300, 900_002, 11000010, 200, 6_000, 3);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal(600, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.True(fixture.ViewModel.CombatantDetails.OutgoingDamage.Total > firstTotal);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DeactivatingDetailKeepsRowsWarmForFlyoutReopen()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        var row = Assert.Single(fixture.ViewModel.Combatants);
        fixture.ViewModel.SelectedCombatant = row;
        var detailRow = fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0];

        fixture.ViewModel.SelectedCombatant = null;

        Assert.Same(detailRow, fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0]);

        fixture.ViewModel.SelectedCombatant = row;

        Assert.Same(detailRow, fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0]);
    }

    [Fact]
    public void ArchiveCurrentEncounter_SceneMode_WritesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentEncounterCommand.Execute(null);

        var history = Assert.Single(fixture.ViewModel.EncounterHistory);
        Assert.NotNull(history.Record.ScenePayload);
        var detail = history.Record.ScenePayload!.CreateDetailDelta(300);
        Assert.True(history.Record.ScenePayload!.IdentityScope.TryGetPcMetadata(300, out var archivedPc));
        Assert.Equal("Scene Player", archivedPc.Nickname);
        Assert.Equal(400, detail.Combatant!.Value.OutgoingDamage);
    }

    [Fact]
    public void ArchiveCurrentEncounter_SceneMode_ArchivedDetailUsesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentEncounterCommand.Execute(null);

        var row = Assert.Single(fixture.ViewModel.Combatants);
        fixture.ViewModel.SelectedCombatant = null;
        fixture.ViewModel.SelectedCombatant = row;

        Assert.Equal(300, fixture.ViewModel.CombatantDetails.SelectedCombatantId);
        Assert.Equal("Scene Player", fixture.ViewModel.CombatantDetails.DisplayContext!.ResolveEntityName(300));
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.NotNull(fixture.ViewModel.EncounterHistory[0].Record.ScenePayload);
    }

    [Fact]
    public void ArchiveCurrentEncounter_SceneMode_ArchivedDisplayUsesSnapshot()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentEncounterCommand.Execute(null);
        var history = Assert.Single(fixture.ViewModel.EncounterHistory);
        fixture.ViewModel.ReturnToLiveCommand.Execute(null);
        fixture.ViewModel.SelectedEncounterHistory = history;

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal("Scene Player", fixture.ViewModel.DisplayContext!.ResolveEntityName(row.Id));
        Assert.NotNull(history.Record.ScenePayload);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_AutoArchiveWritesScenePayloadOnMapTransition()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneMap(200003, 113515);
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.AppendSceneMap(200004, 113516);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("map-transition", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.NotNull(record.ScenePayload);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.Value.OutgoingDamage);
        Assert.NotNull(record.ScenePayload);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_AutoArchivesWhenPreviousMapIsUnknown()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.AppendSceneMap(20, 0);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("map-transition", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.NotNull(record.ScenePayload);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.Value.OutgoingDamage);
    }

    [Fact]
    public void ResetCommand_SceneMode_ArchivesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ResetCommand.Execute(null);

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("manual-reset", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.NotNull(record.ScenePayload);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.Value.OutgoingDamage);
        Assert.NotNull(record.ScenePayload);
    }

    [Fact]
    public void ResetLiveModels_ResetsSceneSnapshot()
    {
        var fixture = MainViewModelFixture.Create();
        var sink = fixture.CreateLiveSink();
        AppendLiveBattle(sink, 100, "Player", 800, 1_000, 2_000, 1);
        var firstScene = fixture.CreateSceneSnapshot();

        fixture.ViewModel.ResetLiveModelsForTesting();

        AppendLiveBattle(sink, 100, "Player", 900, 3_000, 4_000, 3);
        var secondScene = fixture.CreateSceneSnapshot();

        Assert.NotEqual(firstScene.EncounterId, secondScene.EncounterId);
        Assert.Equal(900, secondScene.Combatants[100].DamageAmount);
        Assert.True(fixture.MetadataRegistry.TryGetPcMetadata(100, out var livePc));
        Assert.Equal("Player", livePc.Nickname);
    }

    [Fact]
    public void FrameTick_Stopped_DoesNotRefreshLiveStats()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.ProcessUiFrameForTesting();

        Assert.Empty(fixture.ViewModel.Combatants);
    }

    [Fact]
    public void FrameTick_Capturing_RefreshesLiveStats()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.ViewModel.IsCapturing = true;
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.ProcessUiFrameForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal(400, row.Damage);
    }

    [Fact]
    public void FrameTick_Capturing_RefreshesMapWithoutCombat()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.ViewModel.IsCapturing = true;
        var changed = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        fixture.AppendSceneMap(1010, 0);
        fixture.ViewModel.ProcessUiFrameForTesting();
        fixture.FlushFrame();

        Assert.Equal(1010u, fixture.ViewModel.LiveSceneMapId);
        Assert.Contains(nameof(MainViewModel.LiveSceneMapId), changed);

        changed.Clear();
        fixture.AppendSceneMap(500015, 719460);
        fixture.ViewModel.ProcessUiFrameForTesting();
        fixture.FlushFrame();

        Assert.Equal(500015u, fixture.ViewModel.LiveSceneMapId);
        Assert.Contains(nameof(MainViewModel.LiveSceneMapId), changed);

        changed.Clear();
        fixture.AppendSceneMap(1010, 0);
        fixture.ViewModel.ProcessUiFrameForTesting();
        fixture.FlushFrame();

        Assert.Equal(1010u, fixture.ViewModel.LiveSceneMapId);
        Assert.Contains(nameof(MainViewModel.LiveSceneMapId), changed);
    }

    [Fact]
    public void FrameTick_ArchiveView_DoesNotOverwriteDisplayedArchive()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Archived Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.ArchiveCurrentEncounterCommand.Execute(null);

        fixture.ViewModel.IsCapturing = true;
        fixture.AppendSceneEncounter(301, "Live Player", 800, 6_000, 8_000);
        fixture.ViewModel.ProcessUiFrameForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal("Archived Player", fixture.ViewModel.DisplayContext!.ResolveEntityName(row.Id));
    }

    [Fact]
    public void SceneLiveReadModel_Reset_StartsNewBattleWithoutDroppingIdentity()
    {
        var scene = new SceneLiveReadModel();
        AppendSceneEncounter(scene, 300, "Scene Player", 400, 3_000, 5_000);
        var first = scene.Owner.CreateSnapshot();

        scene.Reset();
        var reset = scene.Owner.CreateSnapshot();

        AppendSceneEncounter(scene, 300, "Scene Player", 401, 6_000, 7_000);
        var second = scene.Owner.CreateSnapshot();

        Assert.NotEqual(first.EncounterId, reset.EncounterId);
        Assert.Empty(reset.Combatants);
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(300, out var scenePc));
        Assert.Equal("Scene Player", scenePc.Nickname);
        Assert.Equal(401, second.Combatants[300].DamageAmount);
    }

    private sealed class MainViewModelFixture
    {
        private readonly WinDivertCaptureService _captureService;
        private readonly UiFrameBatchService _frameBatch;

        private MainViewModelFixture(MainViewModel viewModel, WinDivertCaptureService captureService, EncounterArchiveService archive, UiFrameBatchService frameBatch)
        {
            ViewModel = viewModel;
            _captureService = captureService;
            Archive = archive;
            _frameBatch = frameBatch;
        }

        public MainViewModel ViewModel { get; }
        public EncounterArchiveService Archive { get; }
        public RuntimeMetadataRegistry MetadataRegistry => _captureService.Scene.Owner.MetadataRegistry;

        public static MainViewModelFixture Create()
        {
            CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
            var settingsPath = Path.Combine(Path.GetTempPath(), $"aion2flow-test-{Guid.NewGuid():N}.json");
            var settings = new SettingsService(settingsPath);
            var language = new LanguageService();
            var localization = new LocalizationService(language);
            var resources = new GameResourceService(language);
            var archive = new EncounterArchiveService();
            var ports = new ProcessPortDiscoveryService();
            var capture = new WinDivertCaptureService(ports);
            var frameBatch = new UiFrameBatchService();
            var details = new CombatantDetailsFlyoutViewModel(localization, frameBatch);
            var viewModel = new MainViewModel(capture, ports, language, resources, archive, details, localization, null!, frameBatch);
            return new MainViewModelFixture(viewModel, capture, archive, frameBatch);
        }

        public void AppendSceneEncounter(int playerId, string name, int damage, long start, long end) => MainViewModelCombatantFilterTests.AppendSceneEncounter(_captureService.Scene, playerId, name, damage, start, end);

        public IRuntimeObservationSink CreateLiveSink() => SceneSinkFactory.CreateForLive(_captureService.Scene)();

        public SceneCombatSnapshot CreateSceneSnapshot() => _captureService.Scene.Owner.CreateSnapshot();

        public void AppendSceneDamage(int sourceId, int targetId, int skillCode, int damage, long timestamp, long batchOrdinal) => MainViewModelCombatantFilterTests.AppendSceneDamage(_captureService.Scene, sourceId, targetId, skillCode, damage, timestamp, batchOrdinal);

        public void AppendSceneNpc(int instanceId, int npcCode, NpcKind kind) => MainViewModelCombatantFilterTests.AppendSceneNpc(_captureService.Scene, instanceId, npcCode, kind);

        public void AppendSceneBossFocus(int instanceId, string name, int hp, int maxHp, long timestamp) => MainViewModelCombatantFilterTests.AppendSceneBossFocus(_captureService.Scene, instanceId, name, hp, maxHp, timestamp);

        public void AppendSceneMap(uint mapId, uint instanceId) => MainViewModelCombatantFilterTests.AppendSceneMap(_captureService.Scene, mapId, instanceId);

        public void FlushFrame() => _frameBatch.FlushFrame();
    }

    private static void AppendSceneEncounter(SceneLiveReadModel scene, int playerId, string name, int damage, long start, long end)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
        sink.AppendNickname(playerId, name);
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = 900_002,
            SkillCode = 11000010,
            OriginalSkillCode = 11000010,
            Damage = damage / 2,
            Timestamp = start,
            BatchOrdinal = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = 900_002,
            SkillCode = 11000010,
            OriginalSkillCode = 11000010,
            Damage = damage - damage / 2,
            Timestamp = end,
            BatchOrdinal = 2,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);
    }

    private static void AppendLiveBattle(IRuntimeObservationSink sink, int playerId, string name, int damage, long start, long end, long firstBatchOrdinal)
    {
        sink.AppendNickname(playerId, name);
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = 900_002,
            SkillCode = 11000010,
            OriginalSkillCode = 11000010,
            Damage = damage / 2,
            Timestamp = start,
            BatchOrdinal = firstBatchOrdinal,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = 900_002,
            SkillCode = 11000010,
            OriginalSkillCode = 11000010,
            Damage = damage - damage / 2,
            Timestamp = end,
            BatchOrdinal = firstBatchOrdinal + 1,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        sink.CompleteBatch(firstBatchOrdinal);
        sink.CompleteBatch(firstBatchOrdinal + 1);
    }

    private static void AppendSceneNpc(SceneLiveReadModel scene, int instanceId, int npcCode, NpcKind kind)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
        sink.AppendNpcCode(instanceId, npcCode);
        sink.AppendNpcKind(instanceId, kind);
    }

    private static void AppendSceneBossFocus(SceneLiveReadModel scene, int instanceId, string name, int hp, int maxHp, long timestamp)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
        sink.AppendNpcName(2_100_351, name);
        sink.AppendNpcCode(instanceId, 2_100_351);
        sink.AppendNpcKind(instanceId, NpcKind.Boss);
        sink.SetNpcBattle(instanceId, true, timestamp - 100);
        sink.AppendNpcHp(instanceId, hp, maxHp, timestamp);
    }

    private static void AppendSceneDamage(SceneLiveReadModel scene, int sourceId, int targetId, int skillCode, int damage, long timestamp, long batchOrdinal)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            Timestamp = timestamp,
            BatchOrdinal = batchOrdinal,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        sink.CompleteBatch(batchOrdinal);
    }

    private static void AppendSceneMap(SceneLiveReadModel scene, uint mapId, uint instanceId)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
        sink.StageDestinationMap(mapId);
        sink.StageDestinationMapInstance(instanceId);
        sink.MarkSceneArrival();
    }

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
        ];
    }
}
