using System.Globalization;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Tests.Protocol;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class CombatantDetailsFlyoutViewModelTests
{
    [Fact]
    public void SelectBattleCombatant_Builds_Live_Battle_Sections_And_Filters_By_Target()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;
        const int addId = 9002;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNickname(healerId, "Helper");

        AppendPacket(store, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 250, 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, bossId, playerId, 99000010, 180, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, healerId, playerId, 13000010, 90, 4_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, bossId, 11000010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, addId, 11000010, 200, 5_500, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
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
        Assert.Equal("Strike", viewModel.OutgoingDamage.Rows[0].SkillName);
        Assert.Equal(800, viewModel.OutgoingDamage.Rows[0].TotalAmount);
    }

    [Fact]
    public void SelectBattleCombatant_Uses_Archived_BattleId_Context()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 600, 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 11000010, 400, 15_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        var record = archive.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(record);

        engine.Reset();
        viewModel.SelectBattleCombatant(record!.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Selected_Combatant_Healing_Details_Outside_Damage_Window()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 150, 1_500, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, playerId, 13000010, 250, 2_500, CombatEventKind.Healing, CombatValueKind.Healing);

        var snapshot = engine.CreateBattleSnapshot();

        Assert.Equal(400, snapshot.Combatants[playerId].HealingAmount);

        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(400, viewModel.OutgoingHealing.Total);
        Assert.Equal(400, viewModel.IncomingHealing.Total);
        Assert.Equal(2, viewModel.OutgoingHealing.Rows.Count);
        Assert.Contains(viewModel.OutgoingHealing.Rows, static row => row.SkillName == "Second Wind");
        Assert.Contains(viewModel.OutgoingHealing.Rows, static row => row.SkillName == "Support Heal");
    }

    [Fact]
    public void SelectBattleCombatant_Uses_Archived_Store_For_Summon_Attribution()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int summonId = 5001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        store.AppendSummon(playerId, summonId);
        AppendPacket(store, summonId, bossId, 11000010, 700, 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, summonId, bossId, 11000010, 300, 11_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        var record = archive.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(record);

        engine.Reset();
        viewModel.SelectBattleCombatant(record!.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
    }

    [Fact]
    public void SelectBattleCombatant_Splits_Healing_And_Shield_Sections_And_Shares_Recovery_Scope()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int allyId = 1002;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNickname(allyId, "Helper");

        AppendPacket(store, playerId, bossId, 11000010, 450, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 250, 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, playerId, 14000010, 300, 3_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(store, playerId, allyId, 14000010, 200, 4_000, CombatEventKind.Healing, CombatValueKind.Shield);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

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
        Assert.Equal("Barrier Ward", viewModel.OutgoingShield.Rows[0].SkillName);
    }

    [Fact]
    public void SelectBattleCombatant_Does_Not_Treat_Hostile_Shield_Absorption_As_Support_Source()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNickname(healerId, "Helper");

        AppendPacket(store, playerId, bossId, 11000010, 450, 500, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, healerId, playerId, 13000010, 90, 1_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, bossId, playerId, 14000010, 300, 2_000, CombatEventKind.Support, CombatValueKind.Shield);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(90, viewModel.IncomingHealing.Total);
        Assert.Equal(300, viewModel.IncomingShield.Total);
        Assert.Contains(viewModel.IncomingDetail.SupportCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == healerId);
        Assert.DoesNotContain(viewModel.IncomingDetail.SupportCounterpartFilter.Counterparts, static counterpart => counterpart.CombatantId == bossId);
    }

    [Fact]
    public void SelectBattleCombatant_Preserves_Live_Scope_Filter_Across_Refreshes()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, addId, 11000010, 200, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);
        SelectOnlyCounterpart(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);

        AppendPacket(store, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        AssertSelectedCounterpartIds(viewModel.OutgoingDetail.DamageCounterpartFilter, bossId);
        Assert.Equal(800, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDamage.Rows);
    }

    [Fact]
    public void SelectBattleCombatant_Preserves_Counterpart_ViewModel_Identity_Across_Relevant_Refreshes()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, addId, 11000010, 200, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var originalBossCounterpart = Assert.Single(
            viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts,
            static counterpart => counterpart.CombatantId == bossId);

        AppendPacket(store, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);

        snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

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
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3, modifiers: DamageModifiers.Back | DamageModifiers.Smite);
        AppendPacket(store, playerId, bossId, 11000010, 400, 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry | DamageModifiers.Perfect);
        AppendPacket(store, playerId, bossId, 11000010, 300, 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal(3, row.Hits);
        AssertModifierValues(row.Criticals, row.CriticalRate, 1, 3);
        AssertModifierValues(row.Perfect, row.PerfectRate, 1, 3);
        AssertModifierValues(row.Smite, row.SmiteRate, 1, 3);
        AssertModifierValues(row.Parry, row.ParryRate, 1, 3);
        AssertModifierValues(row.Endurance, row.EnduranceRate, 1, 3);
        AssertModifierValues(row.Back, row.BackRate, 1, 3);
        AssertModifierValues(row.Block, row.BlockRate, 0, 3);
        AssertModifierValues(row.Evades, row.EvadeRate, 0, 3);
    }

    [Fact]
    public void SelectBattleCombatant_Tracks_MultiHit_Modifiers_Without_Inflating_Direct_Hits()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(13060250, "突襲", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 13060250, 35515, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 2, marker: 1);
        AppendPacket(
            store,
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

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

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
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(17010230, "大地報應", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null),
            new Skill(17730000, "主神恩寵", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(
            store,
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
            store,
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
            store,
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
            store,
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

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var rows = viewModel.OutgoingDamage.Rows.OrderBy(row => row.SkillCode).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("大地報應", rows[0].SkillName);
        AssertModifierValues(rows[0].MultiHit, rows[0].MultiHitRate, 2, 2);
        Assert.Equal("主神恩寵", rows[1].SkillName);
        AssertModifierValues(rows[1].MultiHit, rows[1].MultiHitRate, 0, 2);
        AssertModifierValues(viewModel.OutgoingDamage.MultiHitCount, viewModel.OutgoingDamage.MultiHitRate, 2, 4);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Very_Large_Periodic_Damage_Totals_Consistent()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, 1_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, 3_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var expectedDamage = 4L * int.MaxValue;

        Assert.Equal(expectedDamage, (long)snapshot.Combatants[playerId].DamageAmount);
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
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(17010010, "破滅之語", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(17020010, "痛苦連鎖", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.PeriodicDamage, null),
            new Skill(17030010, "弱化之印", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.PeriodicDamage, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 17010010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3);
        AppendPacket(store, playerId, bossId, 17010010, 100, 1_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 17010010, 100, 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        AppendPacket(store, playerId, bossId, 17020010, 450, 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 17020010, 90, 3_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 17020010, 90, 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        AppendPacket(store, playerId, bossId, 17030010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Back);
        AppendPacket(store, playerId, bossId, 17030010, 250, 5_500, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 17030010, 80, 6_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 17030010, 80, 6_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);
        AppendPacket(store, playerId, bossId, 17030010, 80, 7_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage, PeriodicEffectRelation.Target, 9);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(4, viewModel.OutgoingDamage.Hits);
        Assert.Equal(7, viewModel.OutgoingDamage.PeriodicHits);
        AssertModifierValues(viewModel.OutgoingDamage.Criticals, viewModel.OutgoingDamage.CriticalRate, 1, 4);
        AssertModifierValues(viewModel.OutgoingDamage.BackCount, viewModel.OutgoingDamage.BackRate, 1, 4);

        Assert.Collection(
            viewModel.OutgoingDamage.Rows.OrderBy(static row => row.SkillName, StringComparer.Ordinal),
            row =>
            {
                Assert.Equal("弱化之印", row.SkillName);
                Assert.Equal(2, row.Hits);
                Assert.Equal(3, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 0, 2);
                AssertModifierValues(row.Back, row.BackRate, 1, 2);
            },
            row =>
            {
                Assert.Equal("痛苦連鎖", row.SkillName);
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 0, 1);
            },
            row =>
            {
                Assert.Equal("破滅之語", row.SkillName);
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                AssertModifierValues(row.Criticals, row.CriticalRate, 1, 1);
            });
    }

    [Fact]
    public void SelectBattleCombatant_Tracks_Evade_And_Block_Defense_Outcomes()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(1100020, "Croka Light Beam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, 500, CombatEventKind.Damage, CombatValueKind.Damage);

        AppendPacket(store, bossId, playerId, 1100020, 1, 1_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance | DamageModifiers.Regeneration);
        AppendPacket(store, bossId, playerId, 1100020, 1, 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(store, bossId, playerId, 1100020, 0, 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 0, 4_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 11, 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry);
        AppendPacket(store, bossId, playerId, 1100020, 1, 6_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(store, bossId, playerId, 1100020, 0, 7_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 11, 8_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block);
        AppendPacket(store, bossId, playerId, 1100020, 1, 9_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block | DamageModifiers.Perfect);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.IncomingDamage.Rows);

        Assert.Equal(26, viewModel.IncomingDamage.Total);
        Assert.Equal(9, viewModel.IncomingDamage.Attempts);
        Assert.Equal(6, viewModel.IncomingDamage.Hits);
        Assert.Equal(3, viewModel.IncomingDamage.Evades);
        Assert.Equal(9, row.Attempts);
        Assert.Equal(6, row.Hits);
        AssertModifierValues(row.Parry, row.ParryRate, 1, 6);
        AssertModifierValues(row.Endurance, row.EnduranceRate, 3, 6);
        AssertModifierValues(row.Regeneration, row.RegenerationRate, 1, 6);
        AssertModifierValues(row.Block, row.BlockRate, 2, 6);
        AssertModifierValues(row.Perfect, row.PerfectRate, 1, 6);
        AssertModifierValues(row.Evades, row.EvadeRate, 3, 9);
        AssertModifierValues(viewModel.IncomingDamage.RegenerationCount, viewModel.IncomingDamage.RegenerationRate, 1, 6);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 3, 9);
    }

    [Fact]
    public void SelectBattleCombatant_Keeps_Synthetic_Invincible_In_Summary_Without_Showing_Fake_Dodge_Row()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            store,
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

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal("Strike", row.SkillName);
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
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, bossId, playerId, 99000010, 25, 2_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            store,
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

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.IncomingDamage.Rows);

        Assert.Equal("Boss Slam", row.SkillName);
        Assert.Equal(2, viewModel.IncomingDamage.Attempts);
        Assert.Equal(1, viewModel.IncomingDamage.Hits);
        Assert.Equal(1, viewModel.IncomingDamage.Invincible);
        Assert.DoesNotContain(viewModel.IncomingDamage.ScopeOptions, option => option.CombatantId == 0);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 1, 2);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412103519_Stream_Log()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260412103519.log"));
        var liveStore = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(liveStore);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, liveStore, archive, localization);

        var record = archive.Archive(replay.Snapshot, replay.Store, "replay", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Contains(3737, record!.Snapshot.Combatants.Keys);

        viewModel.SelectBattleCombatant(record.BattleId, 3737);

        Assert.Equal(18, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 18, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412110721_Stream_Log()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260412110721.log"));
        var liveStore = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(liveStore);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, liveStore, archive, localization);
        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var record = archive.Archive(replay.Snapshot, replay.Store, "replay", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Contains(primary.CombatantId, record!.Snapshot.Combatants.Keys);

        viewModel.SelectBattleCombatant(record.BattleId, primary.CombatantId);

        Assert.Equal(10, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 10, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    [Fact]
    public void SelectBattleCombatant_Reconstructs_MultiSource_Invincibles_From_20260412103519_Live_Stream_Path()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        using var processor = new PacketStreamProcessor(store);

        foreach (var entry in ReadStreamLogEntries("aion2flow.stream.20260412103519.log"))
        {
            if (!entry.IsInbound)
            {
                continue;
            }

            processor.AppendAndProcess(entry.Payload, entry.Connection, entry.TimestampMilliseconds);
        }

        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        var snapshot = engine.CreateBattleSnapshot();
        var battlePackets = CombatMetricsEngine.EnumerateBattlePackets(store, snapshot.BattleStartTime, snapshot.BattleEndTime)
            .Where(static context => context.TargetId == 3737)
            .ToArray();
        var battleInvincibles = battlePackets
            .Where(static context => context.TargetId == 3737 && (context.Packet.Modifiers & DamageModifiers.Invincible) != 0)
            .Select(static context => $"ts={context.Packet.Timestamp}|source={context.SourceId}|marker={context.Packet.Marker}|attempt={context.Packet.AttemptContribution}|effect={DescribePacketEffect(context.Packet)}")
            .ToArray();
        var manualIncomingDamageMetrics = new Dictionary<int, SkillMetrics>();

        foreach (var battlePacket in battlePackets)
        {
            if (!ContributesDamageForDetail(battlePacket.Packet))
            {
                continue;
            }

            if (!manualIncomingDamageMetrics.TryGetValue(battlePacket.Packet.SkillCode, out var skill))
            {
                skill = new SkillMetrics(battlePacket.Packet);
                manualIncomingDamageMetrics[battlePacket.Packet.SkillCode] = skill;
            }

            skill.ProcessEvent(battlePacket.Packet);
        }

        var manualInvincibleCount = manualIncomingDamageMetrics.Values.Sum(static skill => skill.InvincibleTimes);

        Assert.Contains(3737, snapshot.Combatants.Keys);
        Assert.True(battleInvincibles.Length == 7, string.Join(Environment.NewLine, battleInvincibles));
        Assert.True(manualInvincibleCount == 7, string.Join(Environment.NewLine, battleInvincibles));

        viewModel.SelectBattleCombatant(snapshot.BattleId, 3737);

        Assert.Equal(18, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        AssertModifierValues(viewModel.IncomingDamage.Evades, viewModel.IncomingDamage.EvadeRate, 18, viewModel.IncomingDamage.Attempts);
        AssertModifierValues(viewModel.IncomingDamage.Invincible, viewModel.IncomingDamage.InvincibleRate, 7, viewModel.IncomingDamage.Attempts);
    }

    private static SkillCollection BuildSkillMap()
    {
        return
        [
            new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(12000010, "Second Wind", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", SkillKind.Healing, SkillSemantics.Healing, null),
            new Skill(13000010, "Support Heal", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Healing, SkillSemantics.Healing, null),
            new Skill(14000010, "Barrier Ward", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.ShieldOrBarrier, SkillSemantics.ShieldOrBarrier, null),
            new Skill(99000010, "Boss Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null)
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
    {
        if (packet.EventKind == CombatEventKind.Damage &&
            packet.ValueKind is CombatValueKind.Damage or CombatValueKind.PeriodicDamage or CombatValueKind.DrainDamage or CombatValueKind.Unknown &&
            (packet.AttemptContribution > 0 || (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0))
        {
            return true;
        }

        return packet.ValueKind switch
        {
            CombatValueKind.Damage => packet.Damage > 0,
            CombatValueKind.PeriodicDamage => packet.Damage > 0,
            CombatValueKind.DrainDamage => packet.Damage > 0,
            CombatValueKind.Unknown => packet.EventKind == CombatEventKind.Damage && packet.Damage > 0,
            _ => false
        };
    }

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

    private static void AppendPacket(
        CombatMetricsStore store,
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
        store.AppendCombatPacket(packet);
    }

    private static void AppendPacket(
        CombatMetricsStore store,
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

        store.AppendCombatPacket(packet);
    }

    private static void AssertModifierValues(int actualCount, double actualRate, int expectedCount, int denominator)
    {
        Assert.Equal(expectedCount, actualCount);
        var expectedRate = denominator > 0 ? expectedCount / (double)denominator : 0d;
        Assert.Equal(expectedRate, actualRate, 10);
    }

    [Fact]
    public void ScopeOptions_Resolve_Npc_Name_From_Catalog_When_NpcCode_Set()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), catalog);

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNpcCode(npcInstanceId, npcCode);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        AppendPacket(store, playerId, npcInstanceId, 11000010, 500, 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, npcInstanceId, 11000010, 300, 5_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Single(viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts);

        var counterpart = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(counterpart);
        Assert.True(catalog.TryGetValue(npcCode, out var entry));
        Assert.Equal(entry.Name, counterpart!.DisplayName);
    }

    [Fact]
    public void ScopeOptions_Resolve_Npc_Name_From_Archived_Store()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), catalog);

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var archive = new BattleArchiveService();
        var language = new LanguageService();
        using var localization = new LocalizationService(language);
        var viewModel = new CombatantDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNpcCode(npcInstanceId, npcCode);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);
        store.AppendNpcName(npcCode, "訓練用稻草人");

        AppendPacket(store, playerId, npcInstanceId, 11000010, 600, 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, npcInstanceId, 11000010, 400, 15_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        var record = archive.Archive(snapshot, store, "manual", isAutomatic: false);
        Assert.NotNull(record);

        engine.Reset();
        viewModel.SelectBattleCombatant(record!.BattleId, playerId);

        var counterpart = viewModel.OutgoingDetail.DamageCounterpartFilter.Counterparts.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(counterpart);
        Assert.True(catalog.TryGetValue(npcCode, out var entry));
        Assert.Equal(entry.Name, counterpart!.DisplayName);
    }

    private static void SelectOnlyCounterpart(DetailCounterpartFilterViewModel filter, int combatantId)
    {
        foreach (var counterpart in filter.Counterparts)
        {
            counterpart.IsSelected = counterpart.CombatantId == combatantId;
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
