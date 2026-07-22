using Avalonia.Media;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.Tests.SceneRuntime;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class MainViewModelCombatantFilterTests
{
    [Fact]
    public void CurrentPlaybackContextUsesSessionBoundLiveSource()
    {
        var fixture = MainViewModelFixture.Create();

        var context = fixture.ViewModel.CreateCurrentPlaybackContext();

        Assert.Equal(ScenePlaybackSourceKind.Live, context.Source.SourceKind);
        Assert.Equal(fixture.SceneId, context.Source.EncounterId);
        Assert.Same(fixture.MetadataRegistry, context.DisplayContext.MetadataRegistry);
        Assert.True(context.Source.CreateTimelineSegment().IsLiveGrowing);
        context.Source.Dispose();
    }

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
    public void Map_Transitions_Select_Automatic_Reset_Scope(uint previousMapId, uint previousInstanceId, uint latestMapId, uint latestInstanceId, bool expected, string expectedReason)
    {
        var previous = SceneSnapshotTestFactory.Create(mapId: previousMapId, mapInstanceId: previousInstanceId, encounterTime: 12_000, combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

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
        var previous = SceneSnapshotTestFactory.Create(mapId: 1010, encounterTime: 12_000, combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(mapId: 1010);

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Scene_Transition_Revision_Does_Not_Archive_When_Map_And_Instance_Are_Unchanged()
    {
        var previous = SceneSnapshotTestFactory.Create(mapId: 1010, mapInstanceId: 0, sceneTransitionRevision: 7, encounterTime: 12_000, combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(mapId: 1010, mapInstanceId: 0, sceneTransitionRevision: 8);

        Assert.False(MainViewModel.TryResolveMapTransitionResetReason(previous, latest, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Sub_Instance_Boss_Room_Does_Not_Archive()
    {
        var previous = SceneSnapshotTestFactory.Create(mapId: 910036, mapInstanceId: 113515, encounterTime: 12_000, combatants: [SceneSnapshotTestFactory.Combatant(1, SceneSnapshotTestFactory.VisibleMetrics())]);

        var latest = SceneSnapshotTestFactory.Create(mapId: 910036, mapInstanceId: 113515);

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
        fixture.AppendSceneDamage(38924, 100, 999_999, 200, 1_000, 1);
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
    public void RefreshCombatStats_AllScope_UsesExistingVisiblePlayerLogic()
    {
        var fixture = MainViewModelFixture.Create();
        AppendScopedCombatants(fixture);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([100, 200, 300, 400], fixture.ViewModel.Combatants.Select(static row => row.Id));
    }

    [Fact]
    public void RefreshCombatStats_SelfScope_ShowsOnlyLocalPlayer()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        AppendScopedCombatants(fixture);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([100], fixture.ViewModel.Combatants.Select(static row => row.Id));
    }

    [Fact]
    public void RefreshCombatStats_SelfScope_UsesLocalPlayerIdentityWithoutClass()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(200, "Other");
        fixture.AppendSceneDamage(100, 900_002, 999_999, 500, 3_000, 1);
        fixture.AppendSceneDamage(200, 900_002, 11000010, 1_000, 3_100, 2);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(100, row.Id);
        Assert.Null(row.CharacterClass);
    }

    [Fact]
    public void RefreshCombatStats_PartyScope_ShowsLocalPlayerAndPartyMembers()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Party;
        AppendScopedCombatants(fixture);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([100, 200], fixture.ViewModel.Combatants.Select(static row => row.Id));
    }

    [Fact]
    public void RefreshCombatStats_ForceScope_ShowsLocalPlayerPartyAndForceMembers()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Force;
        AppendScopedCombatants(fixture);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([100, 200, 300], fixture.ViewModel.Combatants.Select(static row => row.Id));
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
    public void RefreshCombatStats_SceneMode_RebuildsDisplayContextWhenMetadataChanges()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();
        var contextBeforeMetadata = fixture.ViewModel.DisplayContext;

        fixture.MetadataRegistry.UpsertPcMetadata(300, "Scene Player", legionName: "Aether");
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.NotSame(contextBeforeMetadata, fixture.ViewModel.DisplayContext);
        Assert.True(fixture.ViewModel.DisplayContext!.TryResolvePcMetadata(300, out var metadata));
        Assert.Equal("Aether", metadata.LegionName);
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
    public void RefreshCombatStats_FocusStatusSetting_HidesOnlyFocusRows()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 25_000, 50_000, 5_500);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);

        fixture.Settings.ShowFocusStatusBar = false;
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Empty(fixture.ViewModel.BossFocuses);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);

        fixture.Settings.ShowFocusStatusBar = true;
        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_ScopeSelf_HidesTrainingDummyFocusActivatedOnlyByOutOfScopeCombatant()
    {
        const int dummyNpcCode = 2_400_032;
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(400, "Other");
        fixture.AppendSceneDamage(400, 900_002, 11000010, 400, 3_000, 1);
        fixture.AppendSceneBossFocus(900_002, dummyNpcCode, NpcKind.TrainingDummy, 80_000, 100_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Empty(fixture.ViewModel.Combatants);
        Assert.Empty(fixture.ViewModel.BossFocuses);
    }

    [Fact]
    public void RefreshCombatStats_ScopeSelf_HidesOutOfScopeBossFocusAndBossShare()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(400, "Other");
        fixture.AppendSceneDamage(100, 900_010, 11000010, 600, 2_500, 1);
        fixture.AppendSceneDamage(400, 900_002, 11000010, 400, 3_000, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(100, row.Id);
        Assert.Empty(fixture.ViewModel.BossFocuses);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_ScopeSelf_ShowsBossFocusActivatedByLocalCombatant()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(400, "Other");
        fixture.AppendSceneDamage(100, 900_002, 11000010, 600, 2_500, 1);
        fixture.AppendSceneDamage(400, 900_003, 11000010, 400, 3_000, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 3_500);
        fixture.AppendSceneBossFocus(900_003, "Other Boss", 700, 1_000, 3_600);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.Equal(100, row.Id);
        Assert.Equal(900_002, focus.InstanceId);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_ScopeSelf_ShowsBossFocusActivatedByClasslessLocalCombatant()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneDamage(100, 900_002, 999_999, 300, 2_500, 1);
        fixture.AppendSceneDamage(100, 900_002, 999_999, 300, 2_600, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.Equal(100, row.Id);
        Assert.Null(row.CharacterClass);
        Assert.Equal(900_002, focus.InstanceId);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_ScopeSelf_OutOfScopeDamageDoesNotRefreshBossFocusTimeout()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantStatisticsScope = CombatantStatisticsScope.Self;
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(400, "Other");
        fixture.AppendSceneDamage(100, 900_002, 11000010, 600, 1_000, 1);
        fixture.AppendSceneDamage(400, 900_002, 11000010, 400, 12_000, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 12_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(100, row.Id);
        Assert.Empty(fixture.ViewModel.BossFocuses);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_MergesTrainingDummyFocusDisplayByNpcCode()
    {
        const int dummyNpcCode = 2_400_032;
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.AppendSceneBossFocus(900_002, dummyNpcCode, NpcKind.TrainingDummy, 80_000, 100_000, 5_000);
        fixture.AppendSceneBossFocus(900_003, dummyNpcCode, NpcKind.TrainingDummy, 60_000, 100_000, 5_500);
        fixture.AppendSceneBossFocus(900_004, dummyNpcCode, NpcKind.TrainingDummy, 40_000, 100_000, 5_200);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.Equal(-dummyNpcCode, focus.DisplayKey);
        Assert.Equal(900_003, focus.InstanceId);
        Assert.Equal(dummyNpcCode, focus.NpcCode);
        Assert.Equal(3, focus.InstanceCount);
        Assert.True(focus.HasMultipleInstances);
        Assert.Equal("x3", focus.InstanceCountText);
        Assert.Equal(60_000, focus.Hp);
        Assert.Equal(100_000, focus.MaxHp);
        Assert.Equal(3, fixture.ViewModel.DisplayContext!.Snapshot.BossFocuses.Count);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_TrainingDummyFocusDoesNotShowBossShareColumn()
    {
        const int dummyNpcCode = 2_400_032;
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 100, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_003, 11000010, 200, 3_100, 2);
        fixture.AppendSceneDamage(300, 900_004, 11000010, 300, 3_200, 3);
        fixture.AppendSceneBossFocus(900_002, dummyNpcCode, NpcKind.TrainingDummy, 80_000, 100_000, 5_000);
        fixture.AppendSceneBossFocus(900_003, dummyNpcCode, NpcKind.TrainingDummy, 60_000, 100_000, 5_500);
        fixture.AppendSceneBossFocus(900_004, dummyNpcCode, NpcKind.TrainingDummy, 40_000, 100_000, 5_200);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0d, row.BossShareRatio);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_CalculatesRelativeDpsBars()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneNickname(301, "Second");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 5_000, 2);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 3_000, 3);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 5_000, 4);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([300, 301], fixture.ViewModel.Combatants.Select(static row => row.Id));
        var firstBar = fixture.ViewModel.Combatants[0].BarSegment;
        var secondBar = fixture.ViewModel.Combatants[1].BarSegment;
        Assert.True(firstBar.HasValue);
        Assert.True(secondBar.HasValue);
        Assert.Equal(1d, firstBar.Value.Ratio, 6);
        Assert.Equal(0.5d, secondBar.Value.Ratio, 6);
        Assert.NotNull(firstBar.Value.Brush);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_TotalDamageBarMode_UsesStableColors()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantSortMetric = CombatantSortMetric.TotalDamage;
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneNickname(301, "Second");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 5_000, 2);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 3_000, 3);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 5_000, 4);

        fixture.ViewModel.RefreshCombatStatsForTesting();
        var firstColor = fixture.ViewModel.Combatants[0].BarSegment!.Value.Brush;
        var secondColor = fixture.ViewModel.Combatants[1].BarSegment!.Value.Brush;

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal([300, 301], fixture.ViewModel.Combatants.Select(static row => row.Id));
        Assert.Equal(1d, fixture.ViewModel.Combatants[0].BarSegment!.Value.Ratio, 6);
        Assert.Equal(0.5d, fixture.ViewModel.Combatants[1].BarSegment!.Value.Ratio, 6);
        Assert.Same(firstColor, fixture.ViewModel.Combatants[0].BarSegment!.Value.Brush);
        Assert.Same(secondColor, fixture.ViewModel.Combatants[1].BarSegment!.Value.Brush);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_GeneratesBarHuesFromEncounterBase()
    {
        var fixture = MainViewModelFixture.Create();
        for (var i = 0; i < 10; i++)
        {
            var playerId = 300 + i;
            fixture.AppendSceneNickname(playerId, $"Player {i}");
            fixture.AppendSceneDamage(playerId, 900_002, 11000010, 1_000 - i * 50, 3_000 + i, i + 1);
        }

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var colors = fixture.ViewModel.Combatants.Select(static row => Assert.IsType<SolidColorBrush>(row.BarSegment!.Value.Brush).Color).ToArray();
        Assert.All(colors, static color => Assert.Equal(byte.MaxValue, color.A));
        Assert.All(colors, static color =>
        {
            var (_, saturation, lightness) = GetHsl(color);
            Assert.InRange(saturation, 0.51d, 0.69d);
            Assert.InRange(lightness, 0.51d, 0.65d);
        });
        for (var i = 1; i < 9; i++)
            Assert.InRange(GetHueDistance(colors[i - 1], colors[i]), 39d, 41d);
        Assert.InRange(GetHueDistance(colors[0], colors[9]), 19d, 21d);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossBarRendersOnlyRemainingHp()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneNickname(301, "Second");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 5_000, 2);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 3_000, 3);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 250, 5_000, 4);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 5_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        var remainingHp = Assert.IsType<ProgressSegment>(focus.BarSegment);
        Assert.Equal(0.7d, remainingHp.Ratio, 6);
        Assert.Equal(Color.FromArgb(0xF0, 0xDF, 0x21, 0x4A), Assert.IsAssignableFrom<ISolidColorBrush>(remainingHp.Brush).Color);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossBarsIgnoreDamageContributionDistribution()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneNickname(301, "Second");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 900, 3_000, 1);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 100, 5_000, 2);
        fixture.AppendSceneDamage(300, 900_003, 11000010, 100, 6_000, 3);
        fixture.AppendSceneDamage(301, 900_003, 11000010, 900, 7_000, 4);
        fixture.AppendSceneBossFocus(900_002, "Boss A", 500, 1_000, 7_500);
        fixture.AppendSceneBossFocus(900_003, "Boss B", 500, 1_000, 7_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.Equal(2, fixture.ViewModel.BossFocuses.Count);
        var firstBoss = fixture.ViewModel.BossFocuses.Single(static boss => boss.InstanceId == 900_002);
        var secondBoss = fixture.ViewModel.BossFocuses.Single(static boss => boss.InstanceId == 900_003);
        Assert.Equal(0.5d, Assert.IsType<ProgressSegment>(firstBoss.BarSegment).Ratio, 6);
        Assert.Equal(0.5d, Assert.IsType<ProgressSegment>(secondBoss.BarSegment).Ratio, 6);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossBarIsEmptyWhenHpUnknown()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 1_000, 3_000, 1);
        fixture.AppendSceneBossFocusUnknownHp(900_002, "Scene Boss", 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var focus = Assert.Single(fixture.ViewModel.BossFocuses);
        Assert.False(focus.HasHp);
        Assert.Null(focus.BarSegment);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_NoBoss_ShowsDpsAndTotalColumns()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Same(fixture.ViewModel.CombatantColumns, row.Columns);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamagePerSecondColumn);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamageColumn);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0d, row.BossShareRatio);
    }

    [Fact]
    public void MainMetricVisibility_UpdatesImmediatelyWithoutChangingTotals()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();
        var totalDamagePerSecond = fixture.ViewModel.TotalFilteredDamagePerSecond;

        fixture.Settings.ShowDamagePerSecondColumn = false;
        fixture.Settings.ShowDamageColumn = false;
        fixture.Settings.ShowTotalDamagePerSecond = false;

        Assert.False(fixture.ViewModel.CombatantColumns.ShowDamagePerSecondColumn);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowDamageColumn);
        Assert.False(fixture.ViewModel.CombatantColumns.ShowTotalDamagePerSecond);
        Assert.True(totalDamagePerSecond > 0);
        Assert.Equal(totalDamagePerSecond, fixture.ViewModel.TotalFilteredDamagePerSecond);
    }

    [Theory]
    [InlineData(SceneKind.Boss, true)]
    [InlineData(SceneKind.Standard, false)]
    public void ArchivedEncounter_BossShareColumnUsesArchivedSceneKind(SceneKind archivedSceneKind, bool expectedVisible)
    {
        var fixture = MainViewModelFixture.Create(archivedSceneKind);
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();
        fixture.ViewModel.ArchiveCurrentEncounterCommand.Execute(null);
        var history = Assert.Single(fixture.ViewModel.EncounterHistory);

        fixture.ViewModel.ReturnToLiveCommand.Execute(null);
        fixture.ViewModel.SelectedEncounterHistory = history;

        Assert.Equal(expectedVisible, fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossColumn_DpsSortShowsDpsTotalAndBossShare()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 250, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 250, 3_100, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Same(fixture.ViewModel.CombatantColumns, row.Columns);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamagePerSecondColumn);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamageColumn);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0.5d, row.BossShareRatio, 6);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossColumn_TotalDamageSortShowsDpsTotalAndBossShare()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Settings.CombatantSortMetric = CombatantSortMetric.TotalDamage;
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 125, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 125, 3_100, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 500, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Same(fixture.ViewModel.CombatantColumns, row.Columns);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamagePerSecondColumn);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowDamageColumn);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0.25d, row.BossShareRatio, 6);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossColumn_ShowsZeroShareRows()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneNickname(301, "Second");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 500, 3_000, 1);
        fixture.AppendSceneDamage(301, 900_900, 11000010, 250, 3_100, 2);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 700, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        var zeroShareRow = fixture.ViewModel.Combatants.Single(static row => row.Id == 301);
        Assert.Equal(0d, zeroShareRow.BossShareRatio);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossColumn_UsesEffectiveHpAfterHealing()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 1_000, 1_000, 1_000);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 500, 1_000, 2_000);
        fixture.AppendSceneBossFocus(900_002, "Scene Boss", 800, 1_000, 3_000);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 325, 3_500, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 325, 3_600, 2);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0.5d, row.BossShareRatio, 6);
    }

    [Fact]
    public void RefreshCombatStats_SceneMode_BossColumn_AggregatesMultipleBossShares()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneNickname(300, "First");
        fixture.AppendSceneDamage(300, 900_003, 11000010, 50, 3_000, 1);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 100, 3_100, 2);
        fixture.AppendSceneBossFocus(900_003, "Boss B", 250, 500, 3_500);
        fixture.AppendSceneBossFocus(900_002, "Boss A", 700, 1_000, 3_500);

        fixture.ViewModel.RefreshCombatStatsForTesting();

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.True(fixture.ViewModel.CombatantColumns.ShowBossShareColumn);
        Assert.Equal(0.1d, row.BossShareRatio, 6);
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
    public void RefreshCombatStats_SceneMode_SwitchingBackToPlayerReplacesDetailEventContext()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneIdentity(300, "First Player");
        fixture.AppendSceneIdentity(301, "Second Player");
        fixture.AppendSceneDamage(300, 900_002, 11000010, 400, 3_000, 1);
        fixture.AppendSceneDamage(301, 900_002, 11000010, 600, 4_000, 2);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        var firstPlayer = fixture.ViewModel.Combatants.Single(static row => row.Id == 300);
        var secondPlayer = fixture.ViewModel.Combatants.Single(static row => row.Id == 301);

        fixture.ViewModel.SelectedCombatant = firstPlayer;
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);

        fixture.ViewModel.SelectedCombatant = secondPlayer;
        Assert.Equal(600, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);

        fixture.ViewModel.SelectedCombatant = firstPlayer;
        Assert.Equal(400, fixture.ViewModel.CombatantDetails.OutgoingDamage.Total);
        Assert.Equal(300, fixture.ViewModel.CombatantDetails.SelectedCombatantId);
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
    public void RefreshCombatStats_SceneMode_AutoArchiveKeepsPreviousEncounterMapOnMapTransition()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.AppendSceneMap(600002, 396972);
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        fixture.AppendSceneMap(1010, 0);
        fixture.ViewModel.RefreshCombatStatsForTesting();

        var record = Assert.Single(fixture.Archive.History);
        Assert.Equal("map-transition", record.Trigger);
        Assert.True(record.IsAutomatic);
        Assert.Equal(600002u, record.Snapshot.MapId);
        Assert.Equal(396972u, record.Snapshot.MapInstanceId);
        Assert.Equal(400, record.ScenePayload!.CreateDetailDelta(300).Combatant!.Value.OutgoingDamage);
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
        AppendLiveBattle(sink, fixture.SceneStartedMilliseconds, 100, "Player", 800, 1_000, 2_000, 1);
        var firstScene = fixture.CreateSceneSnapshot();

        fixture.ViewModel.ResetLiveModelsForTesting();

        AppendLiveBattle(sink, fixture.SceneStartedMilliseconds, 100, "Player", 900, 3_000, 4_000, 3);
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
    public void FrameTick_Capturing_ProjectsDirtyStateAtTwentyFiveHertz()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.ViewModel.IsCapturing = true;
        fixture.AppendSceneEncounter(300, "Scene Player", 400, 3_000, 5_000);

        fixture.ViewModel.ProcessUiFrameForTesting(TimeSpan.Zero);

        var row = Assert.Single(fixture.ViewModel.Combatants);
        Assert.Equal(400, row.Damage);
        Assert.Equal(1, fixture.ProjectionCacheStats.SnapshotBuilds);

        fixture.AppendSceneDamage(300, 900_002, 11000010, 100, 6_000, 3);
        fixture.ViewModel.ProcessUiFrameForTesting(TimeSpan.FromMilliseconds(20));

        Assert.Equal(400, row.Damage);
        Assert.Equal(1, fixture.ProjectionCacheStats.SnapshotBuilds);

        fixture.ViewModel.ProcessUiFrameForTesting(MainViewModel.LiveProjectionInterval);

        Assert.Equal(500, row.Damage);
        Assert.Equal(2, fixture.ProjectionCacheStats.SnapshotBuilds);

        fixture.ViewModel.ProcessUiFrameForTesting(TimeSpan.FromMilliseconds(80));

        Assert.Equal(2, fixture.ProjectionCacheStats.SnapshotBuilds);
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
        public SettingsFlyoutViewModel Settings => ViewModel.SettingsFlyout;
        public EncounterArchiveService Archive { get; }
        public RuntimeMetadataRegistry MetadataRegistry => _captureService.Scene.Owner.MetadataRegistry;
        public ProjectionCacheStats ProjectionCacheStats => _captureService.Scene.Owner.ProjectionCacheStats;
        public Guid SceneId => _captureService.Scene.SessionId;
        public long SceneStartedMilliseconds => _captureService.Scene.SessionStarted.ToUnixTimeMilliseconds();

        public static MainViewModelFixture Create(SceneKind sceneKind = SceneKind.Standard)
        {
            CombatResourceTestFixture.SetResources(BuildSkillMap(), ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog);
            var settingsPath = Path.Combine(Path.GetTempPath(), $"aion2flow-test-{Guid.NewGuid():N}.json");
            var settings = new SettingsService(settingsPath);
            settings.Update(s => s.SceneKind = sceneKind);
            var language = new LanguageService();
            language.SetLanguage(LanguageService.TraditionalChinese);
            var localization = new LocalizationService(language);
            var resources = new GameResourceService(language);
            var archive = new EncounterArchiveService();
            var ports = new ProcessPortDiscoveryService();
            var capture = new WinDivertCaptureService(ports);
            capture.Scene.Reset(DateTimeOffset.Now);
            var frameBatch = new UiFrameBatchService();
            var details = new CombatantDetailsFlyoutViewModel(localization, frameBatch);
            var playerNameDisplay = new PlayerNameDisplayService(settings, localization);
            var uiScale = new UiScaleService(settings);
            var settingsViewModel = new SettingsFlyoutViewModel(localization, language, settings, playerNameDisplay, uiScale, new AppUpdateService(), new ProcessForegroundWatcher(), new GlobalHotkeyService());
            var viewModel = new MainViewModel(capture, ports, language, resources, archive, details, localization, settingsViewModel, frameBatch);
            return new MainViewModelFixture(viewModel, capture, archive, frameBatch);
        }

        public void AppendSceneEncounter(int playerId, string name, int damage, long start, long end) => MainViewModelCombatantFilterTests.AppendSceneEncounter(_captureService.Scene, playerId, name, damage, start, end);

        public void AppendSceneNickname(int playerId, string name) => MainViewModelCombatantFilterTests.AppendSceneNickname(_captureService.Scene, playerId, name);

        public void AppendSceneIdentity(int playerId, string name, bool isLocalPlayer = false) => MainViewModelCombatantFilterTests.AppendSceneIdentity(_captureService.Scene, playerId, name, isLocalPlayer);

        public void AppendScenePlayerGroupMember(int playerId, PlayerGroupMembership membership) => MainViewModelCombatantFilterTests.AppendScenePlayerGroupMember(_captureService.Scene, playerId, membership);

        public IRuntimeObservationSink CreateLiveSink() => SceneSinkFactory.CreateForLive(_captureService.Scene)();

        public SceneCombatSnapshot CreateSceneSnapshot() => _captureService.Scene.Owner.CreateSnapshot();

        public void AppendSceneDamage(int sourceId, int targetId, int skillCode, int damage, long timestamp, long flushId) => MainViewModelCombatantFilterTests.AppendSceneDamage(_captureService.Scene, sourceId, targetId, skillCode, damage, timestamp, flushId);

        public void AppendSceneNpc(int instanceId, int npcCode, NpcKind kind) => MainViewModelCombatantFilterTests.AppendSceneNpc(_captureService.Scene, instanceId, npcCode, kind);

        public void AppendSceneBossFocus(int instanceId, string name, int hp, int maxHp, long timestamp) => MainViewModelCombatantFilterTests.AppendSceneBossFocus(_captureService.Scene, instanceId, name, hp, maxHp, timestamp);

        public void AppendSceneBossFocus(int instanceId, int npcCode, NpcKind kind, int hp, int maxHp, long timestamp) => MainViewModelCombatantFilterTests.AppendSceneBossFocus(_captureService.Scene, instanceId, npcCode, kind, hp, maxHp, timestamp);

        public void AppendSceneBossFocusUnknownHp(int instanceId, string name, long timestamp) => MainViewModelCombatantFilterTests.AppendSceneBossFocusUnknownHp(_captureService.Scene, instanceId, name, timestamp);

        public void AppendSceneMap(uint mapId, uint instanceId) => MainViewModelCombatantFilterTests.AppendSceneMap(_captureService.Scene, mapId, instanceId);

        public void FlushFrame() => _frameBatch.FlushFrame();
    }

    private static void AppendSceneEncounter(SceneLiveReadModel scene, int playerId, string name, int damage, long start, long end)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        var origin = scene.SessionStarted.ToUnixTimeMilliseconds();
        sink.AppendNickname(playerId, name);
        AppendDamage(sink, playerId, 900_002, 11000010, damage / 2, origin + start, 1);
        AppendDamage(sink, playerId, 900_002, 11000010, damage - damage / 2, origin + end, 2);
        sink.CompleteFlush(1);
        sink.CompleteFlush(2);
    }

    private static void AppendSceneNickname(SceneLiveReadModel scene, int playerId, string name)
        => AppendSceneIdentity(scene, playerId, name);

    private static void AppendSceneIdentity(SceneLiveReadModel scene, int playerId, string name, bool isLocalPlayer = false)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        sink.AppendNickname(playerId, name, isLocalPlayer: isLocalPlayer);
    }

    private static void AppendScenePlayerGroupMember(SceneLiveReadModel scene, int playerId, PlayerGroupMembership membership)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        var source = SyntheticObservationExtensions.Source();
        sink.AppendPlayerGroupMember(in source, playerId, in membership);
    }

    private static void AppendLiveBattle(IRuntimeObservationSink sink, long sceneStartedMilliseconds, int playerId, string name, int damage, long start, long end, long firstFlushId)
    {
        sink.AppendNickname(playerId, name);
        AppendDamage(sink, playerId, 900_002, 11000010, damage / 2, sceneStartedMilliseconds + start, firstFlushId);
        AppendDamage(sink, playerId, 900_002, 11000010, damage - damage / 2, sceneStartedMilliseconds + end, firstFlushId + 1);
        sink.CompleteFlush(firstFlushId);
        sink.CompleteFlush(firstFlushId + 1);
    }

    private static void AppendSceneNpc(SceneLiveReadModel scene, int instanceId, int npcCode, NpcKind kind)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        sink.AppendNpcCode(instanceId, npcCode);
        sink.AppendNpcKind(instanceId, kind);
    }

    private static void AppendSceneBossFocus(SceneLiveReadModel scene, int instanceId, string name, int hp, int maxHp, long timestamp)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        var origin = scene.SessionStarted.ToUnixTimeMilliseconds();
        sink.AppendNpcName(2_100_351, name);
        sink.AppendNpcCode(instanceId, 2_100_351);
        sink.AppendNpcKind(instanceId, NpcKind.Boss);
        sink.SetNpcBattle(instanceId, true, origin + timestamp - 100);
        sink.AppendNpcHp(instanceId, hp, maxHp, origin + timestamp);
    }

    private static void AppendSceneBossFocus(SceneLiveReadModel scene, int instanceId, int npcCode, NpcKind kind, int hp, int maxHp, long timestamp)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        var origin = scene.SessionStarted.ToUnixTimeMilliseconds();
        sink.AppendNpcCode(instanceId, npcCode);
        sink.AppendNpcKind(instanceId, kind);
        sink.SetNpcBattle(instanceId, true, origin + timestamp - 100);
        sink.AppendNpcHp(instanceId, hp, maxHp, origin + timestamp);
    }

    private static void AppendSceneBossFocusUnknownHp(SceneLiveReadModel scene, int instanceId, string name, long timestamp)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        var origin = scene.SessionStarted.ToUnixTimeMilliseconds();
        sink.AppendNpcName(2_100_351, name);
        sink.AppendNpcCode(instanceId, 2_100_351);
        sink.AppendNpcKind(instanceId, NpcKind.Boss);
        sink.SetNpcBattle(instanceId, true, origin + timestamp);
    }

    private static void AppendSceneDamage(SceneLiveReadModel scene, int sourceId, int targetId, int skillCode, int damage, long timestamp, long flushId)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        AppendDamage(sink, sourceId, targetId, skillCode, damage, scene.SessionStarted.ToUnixTimeMilliseconds() + timestamp, flushId);
        sink.CompleteFlush(flushId);
    }

    private static void AppendDamage(
        IRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp,
        long flushId)
    {
        var source = new PacketObservationSource(timestamp, flushId, 0, 0, 0, default);
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1
        };
        sink.AppendCombatWireObservation(in source, sourceId, targetId, in observation);
    }

    private static void AppendSceneMap(SceneLiveReadModel scene, uint mapId, uint instanceId)
    {
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextFlushId);
        sink.StageDestinationMap(mapId);
        sink.StageDestinationMapInstance(instanceId);
    }

    private static void AppendScopedCombatants(MainViewModelFixture fixture)
    {
        fixture.AppendSceneIdentity(100, "Self", isLocalPlayer: true);
        fixture.AppendSceneIdentity(200, "Party");
        fixture.AppendSceneIdentity(300, "Force");
        fixture.AppendSceneIdentity(400, "Other");
        fixture.AppendScenePlayerGroupMember(100, PlayerGroupMembership.Force(7, 1, 1));
        fixture.AppendScenePlayerGroupMember(200, PlayerGroupMembership.Force(7, 1, 2));
        fixture.AppendScenePlayerGroupMember(300, PlayerGroupMembership.Force(7, 3, 1));
        fixture.AppendSceneDamage(100, 900_002, 11000010, 1_000, 3_000, 1);
        fixture.AppendSceneDamage(200, 900_002, 11000010, 800, 3_100, 2);
        fixture.AppendSceneDamage(300, 900_002, 11000010, 600, 3_200, 3);
        fixture.AppendSceneDamage(400, 900_002, 11000010, 400, 3_300, 4);
    }

    private static double GetHueDistance(Color left, Color right)
    {
        var delta = Math.Abs(GetHue(left) - GetHue(right));
        return Math.Min(delta, 360d - delta);
    }

    private static double GetHue(Color color) => GetHsl(color).Hue;

    private static (double Hue, double Saturation, double Lightness) GetHsl(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = (max + min) / 2d;
        if (delta == 0)
            return (0, 0, lightness);

        var hue = max == r
            ? 60d * (((g - b) / delta) % 6d)
            : max == g
                ? 60d * ((b - r) / delta + 2d)
                : 60d * ((r - g) / delta + 4d);
        var saturation = delta / (1d - Math.Abs(2d * lightness - 1d));
        return (hue < 0 ? hue + 360d : hue, saturation, lightness);
    }

    private static SkillDisplayCatalog BuildSkillMap()
    {
        return
        [
            new SkillDisplayEntry(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill)
        ];
    }
}
