using System.Globalization;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Tests.Protocol;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class CombatantDetailsFlyoutViewModelTests
{
    [Fact]
    public void SelectBattleCombatant_Builds_Live_Battle_Sections_And_Filters_By_Target()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;
        const int addId = 9002;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNickname(healerId, "Helper");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, playerId, 12000010, 250, 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, bossId, playerId, 99000010, 180, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, healerId, playerId, 13000010, 90, 4_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 200, 5_500, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(3, viewModel.OutgoingDamage.Hits);
        Assert.Equal(250, viewModel.OutgoingHealing.Total);
        Assert.Equal(180, viewModel.IncomingDamage.Total);
        Assert.Equal(340, viewModel.IncomingHealing.Total);
        Assert.Equal(2, viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.Count);
        Assert.Single(viewModel.OutgoingDetail.SupportCounterpartFilter.Counterparts);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        Assert.Equal(800, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDamage.Rows);
        Assert.Equal("Strike", SkillName(viewModel.OutgoingDamage.Rows[0]));
        Assert.Equal(800, viewModel.OutgoingDamage.Rows[0].TotalAmount);
    }

    [Fact]
    public void SelectBattleCombatant_Uses_Filtered_Damage_Duration_For_Subset_Counterparts()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;
        const int farTargetId = 9003;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 20_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 500, 40_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 500, 50_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, farTargetId, 11000010, 500, 70_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, snapshot, playerId);

        Assert.Equal(snapshot.EncounterTime / 1000d, viewModel.OutgoingDamage.DurationSeconds, 10);
        Assert.Equal(2500d / viewModel.OutgoingDamage.DurationSeconds, viewModel.OutgoingDamage.PerSecond, 10);

        SelectCounterparts(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId, addId);

        Assert.Equal(2000, viewModel.OutgoingDamage.Total);
        Assert.Equal(40d, viewModel.OutgoingDamage.DurationSeconds, 10);
        Assert.Equal(50d, viewModel.OutgoingDamage.PerSecond, 10);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(10d, viewModel.OutgoingDamage.DurationSeconds, 10);
        Assert.Equal(100d, viewModel.OutgoingDamage.PerSecond, 10);
    }

    [Fact]
    public void SelectBattleCombatant_Uses_Filtered_Support_Duration_For_Subset_Counterparts()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int allyOneId = 1002;
        const int allyTwoId = 1003;
        const int allyThreeId = 1004;
        const int healerOneId = 1005;
        const int healerTwoId = 1006;
        const int healerThreeId = 1007;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNickname(allyOneId, "Alpha");
        scene.AppendNickname(allyTwoId, "Bravo");
        scene.AppendNickname(allyThreeId, "Charlie");
        scene.AppendNickname(healerOneId, "Healer A");
        scene.AppendNickname(healerTwoId, "Healer B");
        scene.AppendNickname(healerThreeId, "Healer C");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 80_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, allyOneId, 12000010, 500, 10_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyOneId, 12000010, 500, 20_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyTwoId, 12000010, 500, 40_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyTwoId, 12000010, 500, 50_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyThreeId, 12000010, 500, 70_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyOneId, 14000010, 300, 12_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, playerId, allyOneId, 14000010, 300, 22_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, playerId, allyTwoId, 14000010, 300, 42_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, playerId, allyTwoId, 14000010, 300, 52_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, playerId, allyThreeId, 14000010, 300, 72_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, healerOneId, playerId, 13000010, 400, 15_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, healerOneId, playerId, 13000010, 400, 25_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, healerTwoId, playerId, 13000010, 400, 45_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, healerTwoId, playerId, 13000010, 400, 55_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, healerThreeId, playerId, 13000010, 400, 75_000, CombatEventKind.Healing, CombatValueKind.Healing);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, snapshot, playerId);

        Assert.Equal(snapshot.EncounterTime / 1000d, viewModel.OutgoingHealing.DurationSeconds, 10);
        Assert.Equal(snapshot.EncounterTime / 1000d, viewModel.OutgoingShield.DurationSeconds, 10);
        Assert.Equal(snapshot.EncounterTime / 1000d, viewModel.IncomingHealing.DurationSeconds, 10);

        SelectCounterparts(viewModel.OutgoingDetail.SupportCounterpartFilter, allyOneId, allyTwoId);

        Assert.Equal(2000, viewModel.OutgoingHealing.Total);
        Assert.Equal(40d, viewModel.OutgoingHealing.DurationSeconds, 10);
        Assert.Equal(50d, viewModel.OutgoingHealing.PerSecond, 10);
        Assert.Equal(1200, viewModel.OutgoingShield.Total);
        Assert.Equal(40d, viewModel.OutgoingShield.DurationSeconds, 10);
        Assert.Equal(30d, viewModel.OutgoingShield.PerSecond, 10);

        SelectCounterparts(viewModel.IncomingDetail.SupportCounterpartFilter, healerOneId, healerTwoId);

        Assert.Equal(1600, viewModel.IncomingHealing.Total);
        Assert.Equal(40d, viewModel.IncomingHealing.DurationSeconds, 10);
        Assert.Equal(40d, viewModel.IncomingHealing.PerSecond, 10);
    }

    [Fact]
    public void SelectBattleCombatant_Uses_One_Second_Minimum_For_Single_Filtered_Damage_Event()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 700, 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 300, 50_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, snapshot, playerId);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        Assert.Equal(700, viewModel.OutgoingDamage.Total);
        Assert.Equal(1d, viewModel.OutgoingDamage.DurationSeconds, 10);
        Assert.Equal(700d, viewModel.OutgoingDamage.PerSecond, 10);
    }

    [Fact]
    public void SelectBattleCombatant_Uses_One_Second_Minimum_For_Subsecond_Filtered_Support_Events()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int allyId = 1002;
        const int farAllyId = 1003;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNickname(allyId, "Alpha");
        scene.AppendNickname(farAllyId, "Bravo");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 50_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, allyId, 12000010, 400, 10_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, allyId, 12000010, 600, 10_500, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, farAllyId, 12000010, 200, 40_000, CombatEventKind.Healing, CombatValueKind.Healing);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, snapshot, playerId);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.SupportCounterpartFilter, allyId);

        Assert.Equal(1000, viewModel.OutgoingHealing.Total);
        Assert.Equal(1d, viewModel.OutgoingHealing.DurationSeconds, 10);
        Assert.Equal(1000d, viewModel.OutgoingHealing.PerSecond, 10);
    }

    [Fact]
    public void SelectSceneEncounterCombatant_Builds_Live_Detail_From_Scene_Projection()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;
        const int addId = 9002;

        sink.AppendNickname(playerId, "Perigee");
        sink.AppendNickname(healerId, "Helper");
        AppendScenePacket(sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, playerId, playerId, 12000010, 250, 2_000, CombatEventKind.Healing, CombatValueKind.Healing, 2);
        AppendScenePacket(sink, bossId, playerId, 99000010, 180, 3_000, CombatEventKind.Damage, CombatValueKind.Damage, 3);
        AppendScenePacket(sink, healerId, playerId, 13000010, 90, 4_000, CombatEventKind.Healing, CombatValueKind.Healing, 4);
        AppendScenePacket(sink, playerId, bossId, 11000010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, 5);
        AppendScenePacket(sink, playerId, addId, 11000010, 200, 5_500, CombatEventKind.Damage, CombatValueKind.Damage, 6);
        for (var i = 1; i <= 6; i++)
            sink.CompleteBatch(i);

        var snapshot = scene.Owner.CreateSnapshot();
        var detail = scene.Owner.CreateDetailDelta(snapshot, playerId);
        viewModel.SelectSceneEncounterCombatant(snapshot.EncounterId, playerId, snapshot, detail);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(3, viewModel.OutgoingDamage.Hits);
        Assert.Equal(250, viewModel.OutgoingHealing.Total);
        Assert.Equal(180, viewModel.IncomingDamage.Total);
        Assert.Equal(340, viewModel.IncomingHealing.Total);
        Assert.Equal(2, viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.Count);
        Assert.Single(viewModel.OutgoingDetail.SupportCounterpartFilter.Counterparts);
        Assert.Contains(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, x => x.CombatantId == bossId);
        Assert.Contains(viewModel.OutgoingDetail.SupportCounterpartFilter.Counterparts, x => x.CombatantId == playerId);
        Assert.Contains(viewModel.IncomingDetail.SupportCounterpartFilter.Counterparts, x => x.CombatantId == healerId);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        Assert.Equal(800, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDamage.Rows);
        Assert.Equal("Strike", SkillName(viewModel.OutgoingDamage.Rows[0]));
        Assert.Equal(800, viewModel.OutgoingDamage.Rows[0].TotalAmount);
    }

    [Fact]
    public void SelectSceneEncounterCombatant_Includes_Summon_Detail_Folded_To_Owner()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int summonId = 5001;
        const int bossId = 9001;

        sink.AppendNickname(playerId, "Perigee");
        sink.AppendSummon(playerId, summonId);
        AppendScenePacket(sink, summonId, bossId, 11000010, 700, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, summonId, bossId, 11000010, 300, 11_000, CombatEventKind.Damage, CombatValueKind.Damage, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var snapshot = scene.Owner.CreateSnapshot();
        var detail = scene.Owner.CreateDetailDelta(snapshot, playerId);
        viewModel.SelectSceneEncounterCombatant(snapshot.EncounterId, playerId, snapshot, detail);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void SelectSceneEncounterCombatant_Uses_Archived_EncounterId_Context()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var archive = new EncounterArchiveService();
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int bossId = 9001;

        sink.AppendNickname(playerId, "Perigee");
        AppendScenePacket(sink, playerId, bossId, 11000010, 600, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, playerId, bossId, 11000010, 400, 15_000, CombatEventKind.Damage, CombatValueKind.Damage, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var payload = scene.Owner.CreateArchivePayload(scene.Owner.CreateSnapshot());
        var record = archive.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);

        scene.Reset();
        SelectArchivedSceneCombatant(viewModel, record!, playerId);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void SelectSceneEncounterCombatant_Uses_Archived_ScenePayload()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var archive = new EncounterArchiveService();
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int bossId = 9001;

        sink.AppendNickname(playerId, "Scene Player");
        AppendScenePacket(sink, playerId, bossId, 11000010, 600, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, playerId, bossId, 11000010, 400, 15_000, CombatEventKind.Damage, CombatValueKind.Damage, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var payload = scene.Owner.CreateArchivePayload(scene.Owner.CreateSnapshot());
        var record = archive.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Equal(payload.Snapshot.EncounterId, record!.EncounterId);

        scene.Reset();
        SelectArchivedSceneCombatant(viewModel, record, playerId);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Selected_Combatant_Healing_Details_Outside_Damage_Window()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, playerId, 12000010, 150, 1_500, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, playerId, 13000010, 250, 2_500, CombatEventKind.Healing, CombatValueKind.Healing);

        var snapshot = scene.CreateSnapshot();

        Assert.Equal(400, snapshot.Combatants[playerId].HealingAmount);

        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(400, viewModel.OutgoingHealing.Total);
        Assert.Equal(400, viewModel.IncomingHealing.Total);
        Assert.Equal(2, viewModel.OutgoingHealing.Rows.Count);
        Assert.Contains(viewModel.OutgoingHealing.Rows, static row => SkillName(row) == "Second Wind");
        Assert.Contains(viewModel.OutgoingHealing.Rows, static row => SkillName(row) == "Support Heal");
    }

    [Fact]
    public void SelectSceneEncounterCombatant_Uses_Archived_ScenePayload_For_Summon_Attribution()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var archive = new EncounterArchiveService();
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int summonId = 5001;
        const int bossId = 9001;

        sink.AppendNickname(playerId, "Perigee");
        sink.AppendSummon(playerId, summonId);
        AppendScenePacket(sink, summonId, bossId, 11000010, 700, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, summonId, bossId, 11000010, 300, 11_000, CombatEventKind.Damage, CombatValueKind.Damage, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var payload = scene.Owner.CreateArchivePayload(scene.Owner.CreateSnapshot());
        var record = archive.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);

        scene.Reset();
        SelectArchivedSceneCombatant(viewModel, record!, playerId);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
    }

    [Fact]
    public void SelectBattleCombatant_Splits_Healing_And_Shield_Sections_And_Shares_Recovery_Scope()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int allyId = 1002;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNickname(allyId, "Helper");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 450, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, playerId, 12000010, 250, 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, playerId, playerId, 14000010, 300, 3_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(scene.Sink, playerId, allyId, 14000010, 200, 4_000, CombatEventKind.Healing, CombatValueKind.Shield);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(250, viewModel.OutgoingHealing.Total);
        Assert.Equal(500, viewModel.OutgoingShield.Total);
        Assert.Equal(250, viewModel.IncomingHealing.Total);
        Assert.Equal(300, viewModel.IncomingShield.Total);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
        Assert.Equal(2, viewModel.OutgoingDetail.SupportCounterpartFilter.Counterparts.Count);

        SelectOnlyCounterpart(viewModel.OutgoingDetail.SupportCounterpartFilter, allyId);

        Assert.Equal(0, viewModel.OutgoingHealing.Total);
        Assert.Equal(200, viewModel.OutgoingShield.Total);
        Assert.Single(viewModel.OutgoingShield.Rows);
        Assert.Equal("Barrier Ward", SkillName(viewModel.OutgoingShield.Rows[0]));
    }

    [Fact]
    public void SelectBattleCombatant_Does_Not_Treat_Hostile_Shield_Absorption_As_Support_Source()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNickname(healerId, "Helper");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 450, 500, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, healerId, playerId, 13000010, 90, 1_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(scene.Sink, bossId, playerId, 14000010, 300, 2_000, CombatEventKind.Support, CombatValueKind.Shield);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(90, viewModel.IncomingHealing.Total);
        Assert.Equal(300, viewModel.IncomingShield.Total);
        Assert.Contains(viewModel.IncomingDetail.SupportCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == healerId);
        Assert.DoesNotContain(viewModel.IncomingDetail.SupportCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == bossId);
    }

    [Fact]
    public void SelectBattleCombatant_Does_Not_Count_DamageTagged_Periodic_Support_On_Allies_As_Outgoing_Damage()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int allyOneId = 1002;
        const int allyTwoId = 1003;
        const int allyThreeId = 1004;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Tata");
        scene.AppendNickname(allyOneId, "Alpha");
        scene.AppendNickname(allyTwoId, "Bravo");
        scene.AppendNickname(allyThreeId, "Charlie");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);

        AppendPeriodicTargetPacket(allyOneId, 2_000);
        AppendPeriodicTargetPacket(allyTwoId, 3_000);
        AppendPeriodicTargetPacket(allyThreeId, 4_000);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(500, snapshot.Combatants[playerId].DamageAmount);
        Assert.Equal(500, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
        Assert.Contains(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == bossId);
        Assert.DoesNotContain(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == allyOneId);
        Assert.DoesNotContain(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == allyTwoId);
        Assert.DoesNotContain(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == allyThreeId);
        Assert.Single(viewModel.OutgoingDamage.Rows);
        Assert.Equal("Strike", SkillName(viewModel.OutgoingDamage.Rows[0]));

        void AppendPeriodicTargetPacket(int targetId, long timestamp)
        {
            var packet = new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = targetId,
                SkillCode = 17730000,
                OriginalSkillCode = 17730000,
                Damage = 11847,
                Timestamp = timestamp
            };

            packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 9);
            packet.EventKind = CombatEventClassifier.Classify(packet);
            packet.ValueKind = CombatEventClassifier.ClassifyValueKind(packet);
            scene.AppendCombatPacket(packet);
        }
    }

    [Fact]
    public void SelectBattleCombatant_Does_Not_Count_DamageTagged_Self_Support_As_Outgoing_Damage()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "RIpplinger");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var selfPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17730000,
            OriginalSkillCode = 17730000,
            Damage = 60321,
            Timestamp = 2_000
        };
        selfPacket.EventKind = CombatEventClassifier.Classify(selfPacket);
        selfPacket.ValueKind = CombatEventClassifier.ClassifyValueKind(selfPacket);
        scene.AppendCombatPacket(selfPacket);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(500, snapshot.Combatants[playerId].DamageAmount);
        Assert.Equal(500, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
        Assert.Contains(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == bossId);
        Assert.DoesNotContain(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == playerId);
    }

    [Fact]
    public void SelectBattleCombatant_Preserves_Live_Scope_Filter_Across_Refreshes()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        scene.AppendNickname(playerId, "Perigee");
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 200, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);
        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        SelectSceneCombatant(viewModel, scene, playerId);

        AssertSelectedCounterpartIds(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);
        Assert.Equal(800, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDamage.Rows);
    }

    [Fact]
    public void SelectBattleCombatant_Preserves_Counterpart_ViewModel_Identity_Across_Relevant_Refreshes()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        scene.AppendNickname(playerId, "Perigee");
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, addId, 11000010, 200, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var originalBossCounterpart = Assert.Single(
            viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts,
            static counterpart => counterpart.CombatantId == bossId);

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);

        snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var refreshedBossCounterpart = Assert.Single(
            viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts,
            static counterpart => counterpart.CombatantId == bossId);

        Assert.Same(originalBossCounterpart, refreshedBossCounterpart);
        Assert.Equal(800, refreshedBossCounterpart.DamageAmount);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
    }

    [Fact]
    public void SelectBattleCombatant_Builds_PerSkill_Damage_Modifier_Summaries()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3, modifiers: DamageModifiers.Back | DamageModifiers.Smite);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 400, 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry | DamageModifiers.Perfect);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 200, 4_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry | DamageModifiers.DefensivePerfect);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 50, 6_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block | DamageModifiers.DefensivePerfect);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal(6, row.Hits);
        AssertModifierValues(row.Criticals, row.CriticalRate, 1, 6);
        AssertModifierValues(row.Perfect, row.PerfectRate, 1, 6);
        AssertModifierValues(row.Smite, row.SmiteRate, 1, 6);
        AssertModifierValues(row.Parry, row.ParryRate, 2, 6);
        AssertModifierValues(row.PerfectParry, row.PerfectParryRate, 1, 6);
        AssertModifierValues(row.Endurance, row.EnduranceRate, 1, 6);
        AssertModifierValues(row.Back, row.BackRate, 1, 6);
        AssertModifierValues(row.Block, row.BlockRate, 2, 6);
        AssertModifierValues(row.PerfectBlock, row.PerfectBlockRate, 1, 6);
        AssertModifierValues(row.Evades, row.EvadeRate, 0, 6);
        AssertModifierValues(viewModel.OutgoingDamage.ParryCount, viewModel.OutgoingDamage.ParryRate, 2, 6);
        AssertModifierValues(viewModel.OutgoingDamage.PerfectParryCount, viewModel.OutgoingDamage.PerfectParryRate, 1, 6);
        AssertModifierValues(viewModel.OutgoingDamage.BlockCount, viewModel.OutgoingDamage.BlockRate, 2, 6);
        AssertModifierValues(viewModel.OutgoingDamage.PerfectBlockCount, viewModel.OutgoingDamage.PerfectBlockRate, 1, 6);
    }

    [Fact]
    public void SelectBattleCombatant_Tracks_MultiHit_Modifiers_Without_Inflating_Direct_Hits()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(13060250, "突襲", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 13060250, 35515, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 2, marker: 1);
        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            13060250,
            152936,
            2_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            type: 3,
            modifiers: DamageModifiers.Back | DamageModifiers.Smite | DamageModifiers.MultiHit,
            marker: 4,
            multiHitCount: 1);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal(2, row.Hits);
        Assert.Equal(188451, row.TotalAmount);
        AssertModifierValues(row.Criticals, row.CriticalRate, 1, 2);
        AssertModifierValues(row.Smite, row.SmiteRate, 1, 2);
        AssertModifierValues(row.MultiHit, row.MultiHitRate, 1, 2);
        AssertModifierValues(row.Back, row.BackRate, 1, 2);
    }

    [Fact]
    public void SelectBattleCombatant_Counts_MultiHit_Once_Per_Activation_Group()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(17010230, "大地報應", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "主神恩寵", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            17010230,
            19958,
            1_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            type: 3,
            marker: 1,
            modifiers: DamageModifiers.Back | DamageModifiers.Perfect | DamageModifiers.MultiHit,
            multiHitCount: 2);
        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            17730000,
            16790,
            2_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            type: 3,
            marker: 2,
            modifiers: DamageModifiers.Back);
        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            17010230,
            19322,
            3_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            type: 3,
            marker: 3,
            modifiers: DamageModifiers.Back | DamageModifiers.MultiHit,
            multiHitCount: 2);
        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            17730000,
            16369,
            4_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            type: 3,
            marker: 4,
            modifiers: DamageModifiers.Back);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var rows = viewModel.OutgoingDamage.Rows.OrderBy(row => row.SkillCode).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("大地報應", SkillName(rows[0]));
        AssertModifierValues(rows[0].MultiHit, rows[0].MultiHitRate, 2, 2);
        Assert.Equal("主神恩寵", SkillName(rows[1]));
        AssertModifierValues(rows[1].MultiHit, rows[1].MultiHitRate, 0, 2);
        AssertModifierValues(viewModel.OutgoingDamage.MultiHitCount, viewModel.OutgoingDamage.MultiHitRate, 2, 4);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Very_Large_Periodic_Damage_Totals_Consistent()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 11000010, int.MaxValue, 1_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, int.MaxValue, 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, int.MaxValue, 3_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 11000010, int.MaxValue, 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var expectedDamage = 4L * int.MaxValue;

        Assert.Equal(expectedDamage, snapshot.Combatants[playerId].DamageAmount);
        Assert.Equal(expectedDamage, viewModel.OutgoingDamage.Total);
        Assert.Equal(0, viewModel.OutgoingDamage.Hits);
        Assert.Equal(4, viewModel.OutgoingDamage.PeriodicHits);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);
        Assert.Equal(expectedDamage, row.TotalAmount);
        Assert.Equal(0, row.Hits);
        Assert.Equal(4, row.PeriodicHits);
    }

    [Fact]
    public void SelectBattleCombatant_Damage_Hits_And_Modifier_Rates_Ignore_Periodic_Ticks()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(17010010, "破滅之語", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17020010, "痛苦連鎖", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17030010, "弱化之印", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");

        AppendPacket(scene.Sink, playerId, bossId, 17010010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3);
        AppendPacket(scene.Sink, playerId, bossId, 17010010, 100, 1_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 17010010, 100, 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        AppendPacket(scene.Sink, playerId, bossId, 17020010, 450, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, bossId, 17020010, 90, 3_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 17020010, 90, 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        AppendPacket(scene.Sink, playerId, bossId, 17030010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Back);
        AppendPacket(scene.Sink, playerId, bossId, 17030010, 250, 5_500, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, bossId, 17030010, 80, 6_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 17030010, 80, 6_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(scene.Sink, playerId, bossId, 17030010, 80, 7_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(4, viewModel.OutgoingDamage.Hits);
        Assert.Equal(7, viewModel.OutgoingDamage.PeriodicHits);
        AssertModifierValues(viewModel.OutgoingDamage.Criticals, viewModel.OutgoingDamage.CriticalRate, 1, 4);
        AssertModifierValues(viewModel.OutgoingDamage.BackCount, viewModel.OutgoingDamage.BackRate, 1, 4);

        Assert.Collection(
            viewModel.OutgoingDamage.Rows.OrderBy(static row => SkillName(row), StringComparer.Ordinal),
            row =>
            {
                Assert.Equal("弱化之印", SkillName(row));
                Assert.Equal(2, row.Hits);
                Assert.Equal(3, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 0, 2);
                AssertModifierValues(row.Back, row.BackRate, 1, 2);
            },
            row =>
            {
                Assert.Equal("痛苦連鎖", SkillName(row));
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 0, 1);
            },
            row =>
            {
                Assert.Equal("破滅之語", SkillName(row));
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 1, 1);
            });
    }

    [Fact]
    public void SelectBattleCombatant_Tracks_Evade_And_Block_Defense_Outcomes()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(1100020, "Croka Light Beam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 500, CombatEventKind.Damage, CombatValueKind.Damage);

        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance | DamageModifiers.Regeneration);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 0, 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 0, 4_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 11, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 6_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 0, 7_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 11, 8_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 9_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block | DamageModifiers.Perfect);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry | DamageModifiers.DefensivePerfect);
        AppendPacket(scene.Sink, bossId, playerId, 1100020, 1, 11_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block | DamageModifiers.DefensivePerfect);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var row = Assert.Single(viewModel.IncomingDamage.Rows);

        Assert.Equal(28, viewModel.IncomingDamage.Total);
        Assert.Equal(11, viewModel.IncomingDamage.Attempts);
        Assert.Equal(8, viewModel.IncomingDamage.Hits);
        Assert.Equal(3, viewModel.IncomingDamage.Evades);
        Assert.Equal(11, row.Attempts);
        Assert.Equal(8, row.Hits);
        AssertModifierValues(row.Parry, row.ParryRate, 2, 8);
        AssertModifierValues(row.PerfectParry, row.PerfectParryRate, 1, 8);
        AssertModifierValues(row.Endurance, row.EnduranceRate, 3, 8);
        AssertModifierValues(row.Regeneration, row.RegenerationRate, 1, 8);
        AssertModifierValues(row.Block, row.BlockRate, 3, 8);
        AssertModifierValues(row.PerfectBlock, row.PerfectBlockRate, 1, 8);
        AssertModifierValues(row.Perfect, row.PerfectRate, 1, 8);
        AssertModifierValues(row.Evades, row.EvadeRate, 3, 11);
        AssertModifierValues(viewModel.IncomingDamage.PerfectParryCount, viewModel.IncomingDamage.PerfectParryRate, 1, 8);
        AssertModifierValues(viewModel.IncomingDamage.PerfectBlockCount, viewModel.IncomingDamage.PerfectBlockRate, 1, 8);
        AssertModifierValues(viewModel.IncomingDamage.RegenerationCount, viewModel.IncomingDamage.RegenerationRate, 1, 8);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 3, 11);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Synthetic_Invincible_In_Summary_Without_Showing_Fake_Dodge_Row()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            scene.Sink,
            playerId,
            bossId,
            SyntheticCombatSkillCodes.UnresolvedInvincible,
            0,
            2_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            modifiers: DamageModifiers.Invincible,
            hitContribution: 0,
            attemptContribution: 1);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal("Strike", SkillName(row));
        Assert.Equal(2, viewModel.OutgoingDamage.Attempts);
        Assert.Equal(1, viewModel.OutgoingDamage.Hits);
        Assert.Equal(0, viewModel.OutgoingDamage.Evades);
        Assert.Equal(1, viewModel.OutgoingDamage.Invincible);
        AssertModifierValues(viewModel.OutgoingDamage.Evades, viewModel.OutgoingDamage.EvadeRate, 0, 2);
        AssertModifierValues(viewModel.OutgoingDamage.Invincible, viewModel.OutgoingDamage.InvincibleRate, 1, 2);
    }

    [Fact]
    public void SelectBattleCombatant_Counts_UnresolvedAttacker_Invincible_In_Incoming_Summary_Without_UnknownScope()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int bossId = 9001;

        scene.AppendNickname(playerId, "Perigee");
        AppendPacket(scene.Sink, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, bossId, playerId, 99000010, 25, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            scene.Sink,
            0,
            playerId,
            SyntheticCombatSkillCodes.UnresolvedInvincible,
            0,
            3_000,
            CombatEventKind.Damage,
            CombatValueKind.Damage,
            modifiers: DamageModifiers.Invincible,
            hitContribution: 0,
            attemptContribution: 1);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        var row = Assert.Single(viewModel.IncomingDamage.Rows);

        Assert.Equal("Boss Slam", SkillName(row));
        Assert.Equal(2, viewModel.IncomingDamage.Attempts);
        Assert.Equal(1, viewModel.IncomingDamage.Hits);
        Assert.Equal(1, viewModel.IncomingDamage.Invincible);
        Assert.DoesNotContain(viewModel.IncomingDamage.ScopeOptions, option => option.CombatantId == 0);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 1, 2);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412103519_Stream_Log()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = ReplayWithScene(FixtureHelper.GetPath("logs/aion2flow.stream.20260412103519.log"));
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        var record = CreateSceneArchiveRecord(replay);

        Assert.NotNull(record);
        Assert.Contains(3737, record!.Snapshot.Combatants.Keys);

        SelectArchivedSceneCombatant(viewModel, record, 3737);

        Assert.Equal(18, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 18, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412110721_Stream_Log()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = ReplayWithScene(FixtureHelper.GetPath("logs/aion2flow.stream.20260412110721.log"));
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var record = CreateSceneArchiveRecord(replay);

        Assert.NotNull(record);
        Assert.Contains(primary.CombatantId, record!.Snapshot.Combatants.Keys);

        SelectArchivedSceneCombatant(viewModel, record, primary.CombatantId);

        Assert.Equal(10, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 10, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    [Fact]
    public void SelectBattleCombatant_20260422222104_Does_Not_Show_Ally_Targets_In_Outgoing_Damage()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var logPath = FixtureHelper.GetPath("logs/aion2flow.stream.20260422222104.log");
        var replay = ReplayWithScene(logPath);
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 6485;
        var allyIds = new HashSet<int> { 3738, 4985, 7490 };
        var record = CreateSceneArchiveRecord(replay);

        Assert.NotNull(record);
        Assert.Contains(playerId, record!.Snapshot.Combatants.Keys);

        SelectArchivedSceneCombatant(viewModel, record, playerId);

        var damageCounterparts = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts
            .Select(static counterpart =>
                $"id={counterpart.CombatantId}|damage={counterpart.DamageAmount}|share={counterpart.DamageShare:F4}|selected={counterpart.IsSelected}")
            .ToArray();
        var damageRows = viewModel.OutgoingDamage.Rows
            .Select(static row =>
                $"skill={row.SkillCode}|total={row.TotalAmount}|hits={row.Hits}|attempts={row.Attempts}|evades={row.Evades}|invincible={row.Invincible}")
            .ToArray();

        var sourceIds = SceneReplayTestView.SummonOwnerByInstance(replay)
            .Where(static pair => pair.Value == playerId)
            .Select(static pair => pair.Key)
            .Append(playerId)
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();

        var relevantPackets = new List<string>();
        foreach (var sourceId in sourceIds)
        {
            if (!SceneReplayTestView.BySource(replay).TryGetValue(sourceId, out var packets))
            {
                continue;
            }

            foreach (var packet in packets)
            {
                if (!allyIds.Contains(packet.TargetId))
                {
                    continue;
                }

                relevantPackets.Add(
                    $"ts={packet.Timestamp}|rawSource={packet.SourceId}|resolvedSource={SceneReplayTestView.ResolveCombatantId(replay, packet.SourceId)}|target={packet.TargetId}|skillRaw={packet.OriginalSkillCode}|skill={packet.SkillCode}|damage={packet.Damage}|hit={packet.HitContribution}|attempt={packet.AttemptContribution}|event={packet.EventKind}|value={packet.ValueKind}|mods={packet.Modifiers}|effect={DescribeScenePacketEffect(packet)}|detailDamage={ContributesDamageForDetail(packet)}");
            }
        }

        relevantPackets.Sort(StringComparer.Ordinal);

        var unexpectedCounterparts = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts
            .Where(counterpart => allyIds.Contains(counterpart.CombatantId))
            .Select(static counterpart => counterpart.CombatantId)
            .ToArray();

        var diagnostic = string.Join(
            Environment.NewLine,
            [
                $"log={logPath}",
                $"sources=[{string.Join(",", sourceIds)}]",
                $"damageCounterparts={string.Join(" || ", damageCounterparts)}",
                $"damageRows={string.Join(" || ", damageRows)}",
                $"relevantPackets={string.Join(" || ", relevantPackets)}"
            ]);

        Assert.True(unexpectedCounterparts.Length == 0, diagnostic);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412103519_Live_Stream_Path()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        foreach (var entry in ReadStreamLogEntries("aion2flow.stream.20260412103519.log"))
        {
            if (!entry.IsInbound)
            {
                continue;
            }

            processor.AppendAndProcess(entry.Payload, entry.Connection, entry.TimestampMilliseconds);
        }

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        scene.Owner.Refresh();
        var snapshot = scene.Owner.CreateSnapshot();
        var battlePackets = scene.Owner.Combat.Events
            .Where(static e => e.TargetId == 3737 && e.ObservedAtMilliseconds >= 0)
            .ToArray();
        var battleInvincibles = battlePackets
            .Where(static e => (e.Observation.Modifiers & DamageModifiers.Invincible) != 0)
            .Select(static e => $"ts={e.ObservedAtMilliseconds}|source={e.SourceId}|marker={e.Observation.Marker}|attempt={e.Observation.AttemptCount}|effect={DescribePacketEffect(e)}")
            .ToArray();
        var manualInvincibleCount = battlePackets.Where(static e => e.ContributesDamage).Sum(static e => e.InvincibleCount);

        Assert.Contains(3737, snapshot.Combatants.Keys);
        Assert.True(battleInvincibles.Length == 7, string.Join(Environment.NewLine, battleInvincibles));
        Assert.True(manualInvincibleCount == 7, string.Join(Environment.NewLine, battleInvincibles));

        SelectSceneCombatant(viewModel, scene.Owner, snapshot, 3737);

        Assert.Equal(18, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 18, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(12000010, "Second Wind", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null),
            new Skill(13000010, "Support Heal", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(14000010, "Barrier Ward", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ];
    }

    private static IEnumerable<StreamLogEntry> ReadStreamLogEntries(string fileName)
    {
        foreach (var line in File.ReadLines(FixtureHelper.GetPath($"logs/{fileName}")))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 6)
            {
                continue;
            }

            var timestamp = DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUnixTimeMilliseconds();
            var isInbound = parts[1].Equals("dir=inbound", StringComparison.OrdinalIgnoreCase);

            if (!TryParseConnection(parts[2], out var connection))
            {
                continue;
            }

            var dataPart = parts.FirstOrDefault(part => part.StartsWith("data=", StringComparison.OrdinalIgnoreCase));
            if (dataPart is null)
            {
                continue;
            }

            yield return new StreamLogEntry(timestamp, isInbound, connection, Convert.FromHexString(dataPart[5..]));
        }
    }

    private static bool TryParseConnection(string value, out TcpConnection connection)
    {
        connection = default;

        var arrowIndex = value.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex <= 0)
        {
            return false;
        }

        if (!TryParseEndpoint(value[..arrowIndex], out var sourceAddress, out var sourcePort))
        {
            return false;
        }

        if (!TryParseEndpoint(value[(arrowIndex + 2)..], out var destinationAddress, out var destinationPort))
        {
            return false;
        }

        connection = new TcpConnection(sourceAddress, destinationAddress, sourcePort, destinationPort);
        return true;
    }

    private static bool TryParseEndpoint(string value, out uint address, out ushort port)
    {
        address = 0;
        port = 0;

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        return uint.TryParse(value[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out address)
            && ushort.TryParse(value[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
    }

    private static bool ContributesDamageForDetail(ParsedCombatPacket packet)
        => CombatContributionClassifier.Evaluate(packet).CountsAsDamage;

    private static bool ContributesDamageForDetail(SceneReplayPacket packet)
        => packet.ContributesDamage;

    private readonly record struct StreamLogEntry(long TimestampMilliseconds, bool IsInbound, TcpConnection Connection, byte[] Payload);

    private static string DescribePacketEffect(ParsedCombatPacket packet)
    {
        if (packet.IsPeriodicEffect)
        {
            return $"{packet.PeriodicRelation}:{packet.PeriodicMode}";
        }

        return packet.EffectTag == PacketEffectTag.None
            ? "none"
            : packet.EffectTag.ToString();
    }

    private static string DescribePacketEffect(CombatEventRecord e)
    {
        var observation = e.Observation;
        if (observation.PeriodicRelation != PeriodicEffectRelation.None || observation.PeriodicMode != 0)
            return $"{observation.PeriodicRelation}:{observation.PeriodicMode}";

        return observation.EffectTag == PacketEffectTag.None
            ? "none"
            : observation.EffectTag.ToString();
    }

    private static string DescribeScenePacketEffect(SceneReplayPacket packet)
    {
        if (packet.PeriodicRelation != PeriodicEffectRelation.None || packet.PeriodicMode != 0)
            return $"{packet.PeriodicRelation}:{packet.PeriodicMode}";

        return packet.EffectTag == PacketEffectTag.None
            ? "none"
            : packet.EffectTag.ToString();
    }

    private static void AppendPacket(
        IRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp,
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        PeriodicEffectRelation periodicRelation,
        int periodicMode,
        int type = 0,
        DamageModifiers modifiers = DamageModifiers.None,
        int marker = 0,
        int hitContribution = 1,
        int attemptContribution = 1,
        int multiHitCount = 0)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            Timestamp = timestamp,
            Marker = marker,
            Type = type,
            HitContribution = hitContribution,
            AttemptContribution = attemptContribution,
            MultiHitCount = multiHitCount,
            Modifiers = modifiers,
            EventKind = eventKind,
            ValueKind = valueKind
        };

        packet.SetPeriodicEffect(periodicRelation, periodicMode);
        sink.AppendCombatPacket(packet);
    }

    private static void AppendPacket(
        IRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp,
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        int type = 0,
        DamageModifiers modifiers = DamageModifiers.None,
        int marker = 0,
        int hitContribution = 1,
        int attemptContribution = 1,
        int multiHitCount = 0)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            Timestamp = timestamp,
            Marker = marker,
            Type = type,
            HitContribution = hitContribution,
            AttemptContribution = attemptContribution,
            MultiHitCount = multiHitCount,
            Modifiers = modifiers,
            EventKind = eventKind,
            ValueKind = valueKind
        };

        sink.AppendCombatPacket(packet);
    }

    private static void AppendScenePacket(
        JournalingRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp,
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        long batchOrdinal)
    {
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            Timestamp = timestamp,
            BatchOrdinal = batchOrdinal,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = eventKind,
            ValueKind = valueKind
        });
    }

    private static void AssertModifierValues(int actualCount, double actualRate, int expectedCount, int denominator)
    {
        Assert.Equal(expectedCount, actualCount);
        var expectedRate = denominator > 0 ? expectedCount / (double)denominator : 0d;
        Assert.Equal(expectedRate, actualRate, 10);
    }

    private static string SkillName(SkillDetailRowViewModel row)
        => CombatEventClassifier.DisplaySkillNameFor(row.SkillCode);

    private static void SelectSceneCombatant(CombatantDetailsFlyoutViewModel viewModel, SceneTestHarness scene, int combatantId, bool forceRefresh = false)
    {
        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, snapshot, combatantId, forceRefresh);
    }

    private static void SelectSceneCombatant(CombatantDetailsFlyoutViewModel viewModel, SceneTestHarness scene, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false)
    {
        var detail = scene.CreateDetailDelta(snapshot, combatantId, forceRefresh);
        viewModel.SelectSceneEncounterCombatant(snapshot.EncounterId, combatantId, snapshot, detail, forceRefresh);
    }

    private static void SelectSceneCombatant(CombatantDetailsFlyoutViewModel viewModel, SceneReadModelOwner scene, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false)
    {
        var detail = scene.CreateDetailDelta(snapshot, combatantId, forceRefresh);
        viewModel.SelectSceneEncounterCombatant(snapshot.EncounterId, combatantId, snapshot, detail, forceRefresh);
    }

    private static void SelectArchivedSceneCombatant(CombatantDetailsFlyoutViewModel viewModel, ArchivedEncounterRecord record, int combatantId, bool forceRefresh = false)
    {
        var detail = record.ScenePayload.CreateDetailDelta(combatantId);
        viewModel.SelectSceneEncounterCombatant(record.EncounterId, combatantId, record.Snapshot, detail, forceRefresh);
    }

    private static ArchivedEncounterRecord? CreateSceneArchiveRecord(PacketLogReplayResult replay)
    {
        var service = new EncounterArchiveService();
        var payload = replay.SceneOwner.CreateArchivePayload(replay.SceneOwner.CreateSnapshot());
        return service.Archive(payload, "replay", isAutomatic: false);
    }

    private static PacketLogReplayResult ReplayWithScene(string path)
    {
        return PacketLogReplayService.Replay(path);
    }

    [Fact]
    public void ScopeOptions_Resolve_Npc_Name_From_Catalog_When_NpcCode_Set()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);

        using var scene = new SceneTestHarness();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendNpcCode(npcInstanceId, npcCode);
        scene.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        AppendPacket(scene.Sink, playerId, npcInstanceId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(scene.Sink, playerId, npcInstanceId, 11000010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = scene.CreateSnapshot();
        SelectSceneCombatant(viewModel, scene, playerId);

        Assert.Equal(playerId, viewModel.SelectedCombatantId);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);

        var counterpart = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(counterpart);
        Assert.True(catalog.ContainsKey(npcCode));
        Assert.Equal(npcInstanceId, counterpart!.CombatantId);
    }

    [Fact]
    public void ScopeOptions_Resolve_Npc_Name_From_Archived_ScenePayload()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), catalog);

        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(localization, new UiFrameBatchService());
        var archive = new EncounterArchiveService();
        var scene = new SceneLiveReadModel();
        var sink = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        sink.AppendNickname(playerId, "Perigee");
        sink.AppendNpcCode(npcInstanceId, npcCode);
        sink.AppendNpcKind(npcInstanceId, NpcKind.Monster);
        sink.AppendNpcName(npcCode, "訓練用稻草人");
        AppendScenePacket(sink, playerId, npcInstanceId, 11000010, 600, 10_000, CombatEventKind.Damage, CombatValueKind.Damage, 1);
        AppendScenePacket(sink, playerId, npcInstanceId, 11000010, 400, 15_000, CombatEventKind.Damage, CombatValueKind.Damage, 2);
        sink.CompleteBatch(1);
        sink.CompleteBatch(2);

        var payload = scene.Owner.CreateArchivePayload(scene.Owner.CreateSnapshot());
        var record = archive.Archive(payload, "manual", isAutomatic: false);
        Assert.NotNull(record);

        scene.Reset();
        SelectArchivedSceneCombatant(viewModel, record!, playerId);

        var counterpart = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(counterpart);
        Assert.True(catalog.ContainsKey(npcCode));
        Assert.Equal(npcInstanceId, counterpart!.CombatantId);
    }

    private static void SelectOnlyCounterpart(DetailCounterpartFilterViewModel filter, int combatantId)
    {
        foreach (var counterpart in filter.Counterparts)
        {
            counterpart.IsSelected = counterpart.CombatantId == combatantId;
        }
    }

    private static void SelectCounterparts(DetailCounterpartFilterViewModel filter, params int[] combatantIds)
    {
        var selectedIds = combatantIds.ToHashSet();
        foreach (var counterpart in filter.Counterparts)
        {
            counterpart.IsSelected = selectedIds.Contains(counterpart.CombatantId);
        }
    }

    private static void AssertSelectedCounterpartIds(DetailCounterpartFilterViewModel filter, params int[] expectedCombatantIds)
    {
        var selectedIds = filter.Counterparts
            .Where(static counterpart => counterpart.IsSelected)
            .Select(static counterpart => counterpart.CombatantId)
            .OrderBy(static id => id)
            .ToArray();

        Array.Sort(expectedCombatantIds);
        Assert.Equal(expectedCombatantIds, selectedIds);
    }
}
