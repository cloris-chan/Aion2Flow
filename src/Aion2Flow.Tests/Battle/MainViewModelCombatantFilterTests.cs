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
        var store = new CombatMetricsStore();
        const int npcInstanceId = 19945;
        store.AppendNpcCode(npcInstanceId, 2100350);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        var combatant = new CombatantMetrics("Torbas Forest Talekun")
        {
            CharacterClass = CharacterClass.Elementalist
        };

        Assert.False(MainViewModel.ShouldDisplayCombatant(store, npcInstanceId, combatant));
    }

    [Fact]
    public void ShouldDisplayCombatant_Hides_Combatants_Without_Player_Class()
    {
        var store = new CombatMetricsStore();
        var combatant = new CombatantMetrics("Unknown");

        Assert.False(MainViewModel.ShouldDisplayCombatant(store, 38924, combatant));
    }

    [Fact]
    public void ShouldDisplayCombatant_Keeps_Player_Class_When_Not_Npc()
    {
        var store = new CombatMetricsStore();
        var combatant = new CombatantMetrics("Player")
        {
            CharacterClass = CharacterClass.Chanter
        };

        Assert.True(MainViewModel.ShouldDisplayCombatant(store, 12669, combatant));
    }

    [Fact]
    public void RefreshCombatStats_LegacyMode_DisplaysLegacySnapshot()
    {
        var fixture = MainViewModelFixture.Create(SceneSnapshotReadMode.Legacy);
        fixture.AppendLegacyBattle(100, "Legacy Player", 200, 1_000, 2_000);
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(100, row.Id);
        Assert.Equal(200, row.Damage);
        Assert.Equal(1d, fixture.ViewModel.BattleTimeSeconds);
        Assert.Equal(string.Empty, fixture.ViewModel.LastSceneSnapshotDiff);
    }

    [Fact]
    public void RefreshCombatStats_BothMode_DisplaysLegacySnapshotAndRecordsDiff()
    {
        var fixture = MainViewModelFixture.Create(SceneSnapshotReadMode.Both);
        fixture.AppendLegacyBattle(100, "Legacy Player", 200, 1_000, 2_000);
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(100, row.Id);
        Assert.Equal(200, row.Damage);
        Assert.Equal(1d, fixture.ViewModel.BattleTimeSeconds);
        Assert.Contains("battleTime", fixture.ViewModel.LastSceneSnapshotDiff);
        Assert.Contains("combatant:100:damage", fixture.ViewModel.LastSceneSnapshotDiff);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_DisplaysSceneSnapshot()
    {
        var fixture = MainViewModelFixture.Create(SceneSnapshotReadMode.Scene);
        fixture.AppendLegacyBattle(100, "Legacy Player", 200, 1_000, 2_000);
        fixture.AppendSceneBattle(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(300, row.Id);
        Assert.Equal(400, row.Damage);
        Assert.Equal(2d, fixture.ViewModel.BattleTimeSeconds);
        Assert.Equal(string.Empty, fixture.ViewModel.LastSceneSnapshotDiff);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_RefreshesLiveDetailFromSceneProjection()
    {
        var fixture = MainViewModelFixture.Create(SceneSnapshotReadMode.Scene);
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
        private readonly CombatMetricsEngine _engine;
        private readonly WinDivertCaptureService _captureService;

        private MainViewModelFixture(MainViewModel viewModel, CombatMetricsEngine engine, WinDivertCaptureService captureService)
        {
            ViewModel = viewModel;
            _engine = engine;
            _captureService = captureService;
        }

        public MainViewModel ViewModel { get; }

        public static MainViewModelFixture Create(SceneSnapshotReadMode readMode)
        {
            CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
            SceneDualWrite.Enabled = false;
            var settingsPath = Path.Combine(Path.GetTempPath(), $"aion2flow-test-{Guid.NewGuid():N}.json");
            var settings = new SettingsService(settingsPath);
            settings.Update(s => s.SceneSnapshotReadMode = readMode);
            var language = new LanguageService();
            var localization = new LocalizationService(language);
            var resources = new GameResourceService(language);
            var archive = new BattleArchiveService();
            var store = new CombatMetricsStore();
            var engine = new CombatMetricsEngine(store);
            var ports = new ProcessPortDiscoveryService();
            var capture = new WinDivertCaptureService(store, ports);
            var details = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);
            var viewModel = new MainViewModel(capture, ports, engine, store, language, resources, archive, details, localization, settings, null!);
            return new MainViewModelFixture(viewModel, engine, capture);
        }

        public void AppendLegacyBattle(int playerId, string name, int damage, long start, long end)
        {
            _engine.Store.AppendNickname(playerId, name);
            _engine.Store.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = 900_001,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = damage / 2,
                Timestamp = start,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
            _engine.Store.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = 900_001,
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = damage - damage / 2,
                Timestamp = end,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            });
        }

        public void AppendSceneBattle(int playerId, string name, int damage, long start, long end) => MainViewModelCombatantFilterTests.AppendSceneBattle(_captureService.Scene, playerId, name, damage, start, end);
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

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
        ];
    }
}
