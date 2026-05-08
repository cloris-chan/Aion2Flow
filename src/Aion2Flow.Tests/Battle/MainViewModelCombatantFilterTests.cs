using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class MainViewModelCombatantFilterTests
{
    [Theory]
    [InlineData(1010u, 0u, 200003u, 113515u, true, "map-transition")]
    [InlineData(1010u, 0u, 1010u, 0u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113515u, false, "")]
    [InlineData(200003u, 113515u, 200003u, 113526u, true, "map-instance-transition")]
    [InlineData(200003u, 0u, 200003u, 113515u, true, "map-instance-transition")]
    [InlineData(0u, 0u, 1010u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 50u, 0u, true, "map-transition")]
    [InlineData(0u, 0u, 0u, 0u, false, "")]
    [InlineData(600002u, 396972u, 1010u, 0u, true, "map-transition")]
    public void Map_Transitions_Select_Automatic_Reset_Scope(
        uint previousMapId,
        uint previousInstanceId,
        uint latestMapId,
        uint latestInstanceId,
        bool expected,
        string expectedReason)
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = previousMapId,
            MapInstanceId = previousInstanceId,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = latestMapId,
            MapInstanceId = latestInstanceId
        };

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.Equal(expected, result);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void Map_Change_Without_Battle_Does_Not_Trigger_Reset()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 600002
        };

        var latest = new DamageMeterSnapshot
        {
            MapId = 1010
        };

        var result = MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason);

        Assert.False(result);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Predictive_MapId_Flip_Without_Confirmation_Does_Not_Archive()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 1010,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = 1010
        };

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Sub_Instance_Boss_Room_Does_Not_Archive()
    {
        var previous = new DamageMeterSnapshot
        {
            MapId = 910036,
            MapInstanceId = 113515,
            BattleTime = 12_000
        };
        previous.Combatants[1] = new CombatantMetrics("Tester");

        var latest = new DamageMeterSnapshot
        {
            MapId = 910036,
            MapInstanceId = 113515
        };

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
        fixture.AppendSceneBattle(12669, "Player", 400, 1_000, 2_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Contains(fixture.ViewModel.Combatants, x => x.Id == 12669);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DisplaysSceneSnapshot()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal(400, row.Damage);
        Assert.Equal(2d, fixture.ViewModel.BattleTimeSeconds);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_UsesSceneDisplayName()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Name", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal("Scene Name", row.DisplayName);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_FiltersNpcFromSceneStore()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneNpc(900_002, 2_100_350, NpcKind.Monster);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_UsesSceneBossFocus()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 25_000, 50_000, 5_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.Equal(900_002, focus.InstanceId);
        Assert.Equal("Scene Boss", focus.DisplayName);
        Assert.Equal(25_000, focus.Hp);
        Assert.Equal(50_000, focus.MaxHp);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_RefreshesLiveDetailFromSceneProjection()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);

        Assert.Equal("Scene Player", fixture.ViewModel.CombatantDetails.CombatantName);
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.Equal(2, fixture.ViewModel.CombatantDetails.OutgoingDamage.Hits);
        Assert.Equal(2, fixture.ViewModel.CombatantDetails.LastRefreshBaselineCounters.DetailEventCount);
        Assert.Single(fixture.ViewModel.CombatantDetails.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DoesNotRebuildLiveDetailForIrrelevantSceneCombat()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);
        var row = fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0];

        fixture.AppendSceneBattle(301, "Other Player", 600, 6_000, 7_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Same(row, fixture.ViewModel.CombatantDetails.OutgoingDamage.Rows[0]);
        Assert.Equal(2, fixture.ViewModel.CombatantDetails.LastRefreshBaselineCounters.DetailEventCount);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_RebuildsLiveDetailForRelevantSceneCombat()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.SelectedCombatant = Assert.Single(fixture.ViewModel.Combatants);
        var firstTotal = fixture.ViewModel.CombatantDetails.OutgoingDamage.Total;

        fixture.AppendSceneDamage(300, 900_002, 11000010, 200, 6_000, 3);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal(600, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.True(fixture.ViewModel.CombatantDetails.OutgoingDamage.Total > firstTotal);
        Assert.Equal(3, fixture.ViewModel.CombatantDetails.LastRefreshBaselineCounters.DetailEventCount);
    }

    [Fact]
    public void ArchiveCurrentBattle_SceneMode_WritesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentBattleCommand.Execute(null);

        var history = Assert.Single(fixture.ViewModel.BattleHistory);
        Assert.NotNull(history.Record.ScenePayload);
        Assert.Equal("Scene Player", history.Record.ScenePayload!.DisplayNames[300]);
        Assert.Equal(400, history.Record.ScenePayload.CreateDetailDelta(300).Combatant!.OutgoingDamage);
    }

    [Fact]
    public void ArchiveCurrentBattle_SceneMode_ArchivedDetailUsesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentBattleCommand.Execute(null);

        var row = Assert.Single(fixture.ViewModel.Combatants);
        fixture.ViewModel.SelectedCombatant = null;
        fixture.ViewModel.SelectedCombatant = row;

        Assert.Equal("Scene Player", fixture.ViewModel.CombatantDetails.CombatantName);
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.Equal(2, fixture.ViewModel.CombatantDetails.LastRefreshBaselineCounters.DetailEventCount);
        Assert.NotNull(fixture.ViewModel.BattleHistory[0].Record.ScenePayload);
    }

    [Fact]
    public void ArchiveCurrentBattle_SceneMode_ArchivedDisplayUsesSnapshot()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ArchiveCurrentBattleCommand.Execute(null);
        var history = Assert.Single(fixture.ViewModel.BattleHistory);
        fixture.ViewModel.ReturnToLiveCommand.Execute(null);
        fixture.ViewModel.SelectedBattleHistory = history;

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal("Scene Player", row.DisplayName);
        Assert.NotNull(history.Record.ScenePayload);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_AutoArchiveWritesScenePayloadOnMapTransition()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneMap(200003, 113515);
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.AppendSceneMap(200004, 113516);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("map-transition", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.NotNull(record.ScenePayload);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.OutgoingDamage);
        Assert.NotNull(record.ScenePayload);
    }

    [Fact]
    public void ResetCommand_SceneMode_ArchivesScenePayload()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.ViewModel.ResetCommand.Execute(null);

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("manual-reset", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.NotNull(record.ScenePayload);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.OutgoingDamage);
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

        Assert.NotEqual(firstScene.BattleId, secondScene.BattleId);
        Assert.Equal(900, secondScene.Combatants[100].DamageAmount);
        Assert.Equal("Player", secondScene.Combatants[100].Nickname);
    }

    [Fact]
    public void SceneLiveReadModel_Reset_StartsNewBattleWithoutDroppingIdentity()
    {
        var scene = new SceneLiveReadModel();
        AppendSceneBattle(scene, 300, "Scene Player", 400, 3_000, 5_000);
        var first = scene.Owner.CreateSnapshot();

        scene.Reset();
        var reset = scene.Owner.CreateSnapshot();

        AppendSceneBattle(scene, 300, "Scene Player", 401, 6_000, 7_000);
        var second = scene.Owner.CreateSnapshot();

        Assert.NotEqual(first.BattleId, reset.BattleId);
        Assert.Empty(reset.Combatants);
        Assert.Equal("Scene Player", second.Combatants[300].Nickname);
        Assert.Equal(401, second.Combatants[300].DamageAmount);
    }

    private sealed class MainViewModelFixture
    {
        private readonly WinDivertCaptureService _captureService;

        private MainViewModelFixture(MainViewModel viewModel, WinDivertCaptureService captureService, BattleArchiveService archive)
        {
            ViewModel = viewModel;
            _captureService = captureService;
            Archive = archive;
        }

        public MainViewModel ViewModel { get; }
        public BattleArchiveService Archive { get; }

        public static MainViewModelFixture Create()
        {
            CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
            var settingsPath = Path.Combine(Path.GetTempPath(), $"aion2flow-test-{Guid.NewGuid():N}.json");
            var settings = new SettingsService(settingsPath);
            var language = new LanguageService();
            var localization = new LocalizationService(language);
            var resources = new GameResourceService(language);
            var archive = new BattleArchiveService();
            var ports = new ProcessPortDiscoveryService();
            var capture = new WinDivertCaptureService(ports);
            var details = new CombatantDetailsFlyoutViewModel(localization);
            var viewModel = new MainViewModel(capture, ports, language, resources, archive, details, localization, null!);
            return new MainViewModelFixture(viewModel, capture, archive);
        }

        public void AppendSceneBattle(int playerId, string name, int damage, long start, long end) => MainViewModelCombatantFilterTests.AppendSceneBattle(_captureService.Scene, playerId, name, damage, start, end);

        public IRuntimeObservationSink CreateLiveSink() => SceneSinkFactory.CreateForLive(_captureService.Scene)();

        public DamageMeterSnapshot CreateSceneSnapshot() => _captureService.Scene.Owner.CreateSnapshot();

        public void AppendSceneDamage(int sourceId, int targetId, int skillCode, int damage, long timestamp, long batchOrdinal) => MainViewModelCombatantFilterTests.AppendSceneDamage(_captureService.Scene, sourceId, targetId, skillCode, damage, timestamp, batchOrdinal);

        public void AppendSceneNpc(int instanceId, int npcCode, NpcKind kind) => MainViewModelCombatantFilterTests.AppendSceneNpc(_captureService.Scene, instanceId, npcCode, kind);

        public void AppendSceneBossFocus(int instanceId, string name, int hp, int maxHp, long timestamp) => MainViewModelCombatantFilterTests.AppendSceneBossFocus(_captureService.Scene, instanceId, name, hp, maxHp, timestamp);

        public void AppendSceneMap(uint mapId, uint instanceId) => MainViewModelCombatantFilterTests.AppendSceneMap(_captureService.Scene, mapId, instanceId);
    }

    private static void AppendSceneBattle(SceneLiveReadModel scene, int playerId, string name, int damage, long start, long end)
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
