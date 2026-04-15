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
using System.Globalization;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class CombatantSkillDetailsFlyoutViewModelTests
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int healerId = 1002;
        const int bossId = 9001;
        const int addId = 9002;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNickname(healerId, "Helper");

        AppendPacket(store, playerId, bossId, 11000010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 250, "self-heal", 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, bossId, playerId, 99000010, 180, "boss-hit", 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, healerId, playerId, 13000010, 90, "ally-heal", 4_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, bossId, 11000010, 300, "direct-hit", 5_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, addId, 11000010, 200, "direct-hit", 5_500, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(3, viewModel.OutgoingDamage.Hits);
        Assert.Equal(250, viewModel.OutgoingHealing.Total);
        Assert.Equal(180, viewModel.IncomingDamage.Total);
        Assert.Equal(340, viewModel.IncomingHealing.Total);
        Assert.Equal(3, viewModel.OutgoingDamage.ScopeOptions.Count);

        viewModel.OutgoingDamage.SelectedScope = viewModel.OutgoingDamage.ScopeOptions.First(x => x.CombatantId == bossId);

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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 600, "direct-hit", 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 11000010, 400, "direct-hit", 15_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        var record = archive.Archive(snapshot, store, "manual", isAutomatic: false);

        Assert.NotNull(record);

        engine.Reset();
        viewModel.SelectBattleCombatant(record!.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Equal(1000, viewModel.OutgoingDamage.Total);
        Assert.Equal(2, viewModel.OutgoingDamage.Hits);
        Assert.Equal(2, viewModel.OutgoingDamage.ScopeOptions.Count);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 150, "passive-heal", 1_500, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, playerId, 13000010, 250, "active-heal", 2_500, CombatEventKind.Healing, CombatValueKind.Healing);

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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int summonId = 5001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        store.AppendSummon(playerId, summonId);
        AppendPacket(store, summonId, bossId, 11000010, 700, "direct-hit", 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, summonId, bossId, 11000010, 300, "direct-hit", 11_000, CombatEventKind.Damage, CombatValueKind.Damage);

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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int allyId = 1002;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNickname(allyId, "Helper");

        AppendPacket(store, playerId, bossId, 11000010, 450, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, playerId, 12000010, 250, "self-heal", 2_000, CombatEventKind.Healing, CombatValueKind.Healing);
        AppendPacket(store, playerId, playerId, 14000010, 300, "self-shield", 3_000, CombatEventKind.Healing, CombatValueKind.Shield);
        AppendPacket(store, playerId, allyId, 14000010, 200, "ally-shield", 4_000, CombatEventKind.Healing, CombatValueKind.Shield);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(250, viewModel.OutgoingHealing.Total);
        Assert.Equal(500, viewModel.OutgoingShield.Total);
        Assert.Equal(250, viewModel.IncomingHealing.Total);
        Assert.Equal(300, viewModel.IncomingShield.Total);
        Assert.Equal(3, viewModel.OutgoingHealing.ScopeOptions.Count);
        Assert.Equal(3, viewModel.OutgoingShield.ScopeOptions.Count);

        viewModel.OutgoingHealing.SelectedScope = viewModel.OutgoingHealing.ScopeOptions.First(x => x.CombatantId == allyId);

        Assert.Equal(allyId, viewModel.OutgoingShield.SelectedScope?.CombatantId);
        Assert.Equal(0, viewModel.OutgoingHealing.Total);
        Assert.Equal(200, viewModel.OutgoingShield.Total);
        Assert.Single(viewModel.OutgoingShield.Rows);
        Assert.Equal("Barrier Ward", viewModel.OutgoingShield.Rows[0].SkillName);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;
        const int addId = 9002;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, addId, 11000010, 200, "direct-hit", 2_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);
        viewModel.OutgoingDamage.SelectedScope = viewModel.OutgoingDamage.ScopeOptions.First(x => x.CombatantId == bossId);

        AppendPacket(store, playerId, bossId, 11000010, 300, "direct-hit", 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(bossId, viewModel.OutgoingDamage.SelectedScope?.CombatantId);
        Assert.Equal(800, viewModel.OutgoingDamage.Total);
        Assert.Single(viewModel.OutgoingDamage.Rows);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3, modifiers: DamageModifiers.Back | DamageModifiers.Smite);
        AppendPacket(store, playerId, bossId, 11000010, 400, "direct-hit", 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry | DamageModifiers.Perfect);
        AppendPacket(store, playerId, bossId, 11000010, 300, "direct-hit", 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.OutgoingDamage.Rows);

        Assert.Equal(3, row.Hits);
        Assert.Equal(FormatModifierSummary(1, 3), row.CriticalSummary);
        Assert.Equal(FormatModifierSummary(1, 3), row.PerfectSummary);
        Assert.Equal(FormatModifierSummary(1, 3), row.SmiteSummary);
        Assert.Equal(FormatModifierSummary(1, 3), row.ParrySummary);
        Assert.Equal(FormatModifierSummary(1, 3), row.EnduranceSummary);
        Assert.Equal(FormatModifierSummary(1, 3), row.BackSummary);
        Assert.Equal(FormatModifierSummary(0, 3), row.BlockSummary);
        Assert.Equal(FormatModifierSummary(0, 3), row.EvadeSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 13060250, 35515, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 2, marker: 1);
        AppendPacket(
            store,
            playerId,
            bossId,
            13060250,
            152936,
            "direct-hit",
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
        Assert.Equal(FormatModifierSummary(1, 2), row.CriticalSummary);
        Assert.Equal(FormatModifierSummary(1, 2), row.SmiteSummary);
        Assert.Equal(FormatModifierSummary(1, 2), row.MultiHitSummary);
        Assert.Equal(FormatModifierSummary(1, 2), row.BackSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(
            store,
            playerId,
            bossId,
            17010230,
            19958,
            "direct-hit",
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
            "direct-hit",
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
            "direct-hit",
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
            "direct-hit",
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
        Assert.Equal(FormatModifierSummary(2, 2), rows[0].MultiHitSummary);
        Assert.Equal("主神恩寵", rows[1].SkillName);
        Assert.Equal(FormatModifierSummary(0, 2), rows[1].MultiHitSummary);
        Assert.Equal(FormatModifierSummary(2, 4), viewModel.OutgoingDamage.MultiHitSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, "periodic-target-mode-9", 1_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, "periodic-target-mode-9", 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, "periodic-target-mode-9", 3_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 11000010, int.MaxValue, "periodic-target-mode-9", 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);

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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");

        AppendPacket(store, playerId, bossId, 17010010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage, type: 3);
        AppendPacket(store, playerId, bossId, 17010010, 100, "periodic-target-mode-9", 1_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 17010010, 100, "periodic-target-mode-9", 2_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);

        AppendPacket(store, playerId, bossId, 17020010, 450, "direct-hit", 3_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 17020010, 90, "periodic-target-mode-9", 3_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 17020010, 90, "periodic-target-mode-9", 4_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);

        AppendPacket(store, playerId, bossId, 17030010, 300, "direct-hit", 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Back);
        AppendPacket(store, playerId, bossId, 17030010, 250, "direct-hit", 5_500, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, bossId, 17030010, 80, "periodic-target-mode-9", 6_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 17030010, 80, "periodic-target-mode-9", 6_500, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
        AppendPacket(store, playerId, bossId, 17030010, 80, "periodic-target-mode-9", 7_000, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal(4, viewModel.OutgoingDamage.Hits);
        Assert.Equal(7, viewModel.OutgoingDamage.PeriodicHits);
        Assert.Equal(FormatModifierSummary(1, 4), viewModel.OutgoingDamage.CriticalSummary);
        Assert.Equal(FormatModifierSummary(1, 4), viewModel.OutgoingDamage.BackSummary);

        Assert.Collection(
            viewModel.OutgoingDamage.Rows.OrderBy(static row => row.SkillName, StringComparer.Ordinal),
            row =>
            {
                Assert.Equal("弱化之印", row.SkillName);
                Assert.Equal(2, row.Hits);
                Assert.Equal(3, row.PeriodicHits);
                Assert.Equal(FormatModifierSummary(0, 2), row.CriticalSummary);
                Assert.Equal(FormatModifierSummary(1, 2), row.BackSummary);
            },
            row =>
            {
                Assert.Equal("痛苦連鎖", row.SkillName);
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                Assert.Equal(FormatModifierSummary(0, 1), row.CriticalSummary);
            },
            row =>
            {
                Assert.Equal("破滅之語", row.SkillName);
                Assert.Equal(1, row.Hits);
                Assert.Equal(2, row.PeriodicHits);
                Assert.Equal(FormatModifierSummary(1, 1), row.CriticalSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, "direct-hit", 500, CombatEventKind.Damage, CombatValueKind.Damage);

        AppendPacket(store, bossId, playerId, 1100020, 1, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance | DamageModifiers.Regeneration);
        AppendPacket(store, bossId, playerId, 1100020, 1, "direct-hit", 2_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(store, bossId, playerId, 1100020, 0, "compact-evade", 3_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 0, "compact-evade", 4_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 11, "direct-hit", 5_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Parry);
        AppendPacket(store, bossId, playerId, 1100020, 1, "direct-hit", 6_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Endurance);
        AppendPacket(store, bossId, playerId, 1100020, 0, "compact-evade", 7_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Evade, hitContribution: 0, attemptContribution: 1);
        AppendPacket(store, bossId, playerId, 1100020, 11, "direct-hit", 8_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block);
        AppendPacket(store, bossId, playerId, 1100020, 1, "direct-hit", 9_000, CombatEventKind.Damage, CombatValueKind.Damage, modifiers: DamageModifiers.Block | DamageModifiers.Perfect);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        var row = Assert.Single(viewModel.IncomingDamage.Rows);

        Assert.Equal(26, viewModel.IncomingDamage.Total);
        Assert.Equal(9, viewModel.IncomingDamage.Attempts);
        Assert.Equal(6, viewModel.IncomingDamage.Hits);
        Assert.Equal(3, viewModel.IncomingDamage.Evades);
        Assert.Equal(9, row.Attempts);
        Assert.Equal(6, row.Hits);
        Assert.Equal(FormatModifierSummary(1, 6), row.ParrySummary);
        Assert.Equal(FormatModifierSummary(3, 6), row.EnduranceSummary);
        Assert.Equal(FormatModifierSummary(1, 6), row.RegenerationSummary);
        Assert.Equal(FormatModifierSummary(2, 6), row.BlockSummary);
        Assert.Equal(FormatModifierSummary(1, 6), row.PerfectSummary);
        Assert.Equal(FormatModifierSummary(3, 9), row.EvadeSummary);
        Assert.Equal(FormatModifierSummary(1, 6), viewModel.IncomingDamage.RegenerationSummary);
        Assert.Equal(FormatModifierSummary(3, 9), viewModel.IncomingDamage.EvadeSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            store,
            playerId,
            bossId,
            SyntheticCombatSkillCodes.UnresolvedInvincible,
            0,
            "dodge-invincible",
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
        Assert.Equal(FormatModifierSummary(0, 2), viewModel.OutgoingDamage.EvadeSummary);
        Assert.Equal(FormatModifierSummary(1, 2), viewModel.OutgoingDamage.InvincibleSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int bossId = 9001;

        store.AppendNickname(playerId, "Perigee");
        AppendPacket(store, playerId, bossId, 11000010, 100, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, bossId, playerId, 99000010, 25, "boss-hit", 2_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(
            store,
            0,
            playerId,
            SyntheticCombatSkillCodes.UnresolvedInvincible,
            0,
            "dodge-invincible",
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
        Assert.Equal(FormatModifierSummary(1, 2), viewModel.IncomingDamage.InvincibleSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, liveStore, archive, localization);

        var record = archive.Archive(replay.Snapshot, replay.Store, "replay", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Contains(3737, record!.Snapshot.Combatants.Keys);

        viewModel.SelectBattleCombatant(record.BattleId, 3737);

        Assert.Equal(18, viewModel.IncomingDamage.Evades);
        Assert.Equal(7, viewModel.IncomingDamage.Invincible);
        Assert.Equal(FormatModifierSummary(18, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.EvadeSummary);
        Assert.Equal(FormatModifierSummary(7, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.InvincibleSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, liveStore, archive, localization);
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
        Assert.Equal(FormatModifierSummary(10, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.EvadeSummary);
        Assert.Equal(FormatModifierSummary(7, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.InvincibleSummary);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        var snapshot = engine.CreateBattleSnapshot();
        var battlePackets = CombatMetricsEngine.EnumerateBattlePackets(store, snapshot.BattleStartTime, snapshot.BattleEndTime)
            .Where(static context => context.TargetId == 3737)
            .ToArray();
        var battleInvincibles = battlePackets
            .Where(static context => context.TargetId == 3737 && (context.Packet.Modifiers & DamageModifiers.Invincible) != 0)
            .Select(static context => $"ts={context.Packet.Timestamp}|source={context.SourceId}|marker={context.Packet.Marker}|attempt={context.Packet.AttemptContribution}|family={context.Packet.EffectFamily}")
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
        Assert.Equal(FormatModifierSummary(18, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.EvadeSummary);
        Assert.Equal(FormatModifierSummary(7, viewModel.IncomingDamage.Attempts), viewModel.IncomingDamage.InvincibleSummary);
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

    private static void AppendPacket(
        CombatMetricsStore store,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        string effectFamily,
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
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage,
            EffectFamily = effectFamily,
            Timestamp = timestamp,
            Marker = marker,
            Type = type,
            HitContribution = hitContribution,
            AttemptContribution = attemptContribution,
            MultiHitCount = multiHitCount,
            Modifiers = modifiers,
            EventKind = eventKind,
            ValueKind = valueKind
        });
    }

    private static string FormatModifierSummary(int count, int hits)
    {
        var rate = hits > 0 ? count / (double)hits : 0d;
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1:P1})", count, rate);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNpcCode(npcInstanceId, npcCode);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);

        AppendPacket(store, playerId, npcInstanceId, 11000010, 500, "direct-hit", 1_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, npcInstanceId, 11000010, 300, "direct-hit", 5_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        viewModel.SelectBattleCombatant(snapshot.BattleId, playerId);

        Assert.Equal("Perigee", viewModel.CombatantName);
        Assert.Equal(2, viewModel.OutgoingDamage.ScopeOptions.Count);

        var npcScope = viewModel.OutgoingDamage.ScopeOptions.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(npcScope);
        Assert.True(catalog.TryGetValue(npcCode, out var entry));
        Assert.Equal(entry.Name, npcScope!.DisplayName);
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
        var viewModel = new CombatantSkillDetailsFlyoutViewModel(engine, store, archive, localization);

        const int playerId = 1001;
        const int npcInstanceId = 29994;
        const int npcCode = 2400032;

        store.AppendNickname(playerId, "Perigee");
        store.AppendNpcCode(npcInstanceId, npcCode);
        store.AppendNpcKind(npcInstanceId, NpcKind.Monster);
        store.AppendNpcName(npcCode, "訓練用稻草人");

        AppendPacket(store, playerId, npcInstanceId, 11000010, 600, "direct-hit", 10_000, CombatEventKind.Damage, CombatValueKind.Damage);
        AppendPacket(store, playerId, npcInstanceId, 11000010, 400, "direct-hit", 15_000, CombatEventKind.Damage, CombatValueKind.Damage);

        var snapshot = engine.CreateBattleSnapshot();
        var record = archive.Archive(snapshot, store, "manual", isAutomatic: false);
        Assert.NotNull(record);

        engine.Reset();
        viewModel.SelectBattleCombatant(record!.BattleId, playerId);

        var npcScope = viewModel.OutgoingDamage.ScopeOptions.FirstOrDefault(x => x.CombatantId == npcInstanceId);
        Assert.NotNull(npcScope);
        Assert.True(catalog.TryGetValue(npcCode, out var entry));
        Assert.Equal(entry.Name, npcScope!.DisplayName);
    }
}
