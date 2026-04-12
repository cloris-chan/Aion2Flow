using System.Globalization;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class CombatantSkillDetailsFlyoutViewModel : ObservableObject
{
    private readonly CombatMetricsEngine _engine;
    private readonly CombatMetricsStore _liveStore;
    private readonly BattleArchiveService _battleArchiveService;
    private readonly LocalizationService _localization;

    private readonly List<ResolvedDetailPacket> _battlePackets = [];
    private DamageMeterSnapshot _currentSnapshot = new();
    private CombatMetricsStore? _currentStore;
    private Guid _battleContextId;
    private int? _combatantId;
    private bool _suppressScopeRefresh;

#if DEBUG
    private readonly bool _isDebugBuild = true;
#else
    private readonly bool _isDebugBuild;
#endif

    private sealed record ResolvedDetailPacket(ParsedCombatPacket Packet, int SourceId, int TargetId);

    private enum DetailSectionKind
    {
        OutgoingDamage,
        OutgoingHealing,
        OutgoingShield,
        IncomingDamage,
        IncomingHealing,
        IncomingShield
    }

    public CombatantSkillDetailsFlyoutViewModel(
        CombatMetricsEngine engine,
        CombatMetricsStore liveStore,
        BattleArchiveService battleArchiveService,
        LocalizationService localization)
    {
        _engine = engine;
        _liveStore = liveStore;
        _battleArchiveService = battleArchiveService;
        _localization = localization;

        OutgoingDamage.SelectedScopeChanged += HandleScopeChanged;
        OutgoingHealing.SelectedScopeChanged += HandleScopeChanged;
        IncomingDamage.SelectedScopeChanged += HandleScopeChanged;
        IncomingHealing.SelectedScopeChanged += HandleScopeChanged;
    }

    public LocalizationService Localization => _localization;

    public bool IsDebugBuild => _isDebugBuild;

    public SkillDetailSectionViewModel OutgoingDamage { get; } = new();

    public SkillDetailSectionViewModel OutgoingHealing { get; } = new();

    public SkillDetailSectionViewModel OutgoingShield { get; } = new();

    public SkillDetailSectionViewModel IncomingDamage { get; } = new();

    public SkillDetailSectionViewModel IncomingHealing { get; } = new();

    public SkillDetailSectionViewModel IncomingShield { get; } = new();

    [ObservableProperty]
    public partial string CombatantName { get; set; } = string.Empty;

    public void SelectBattleCombatant(Guid battleContextId, int? combatantId)
    {
        _battleContextId = battleContextId;
        _combatantId = combatantId;
        RefreshContext();
    }

    public void Clear()
    {
        _battleContextId = Guid.Empty;
        _combatantId = null;
        _currentSnapshot = new DamageMeterSnapshot();
        _currentStore = null;
        _battlePackets.Clear();
        CombatantName = string.Empty;
        OutgoingDamage.Clear();
        OutgoingHealing.Clear();
        OutgoingShield.Clear();
        IncomingDamage.Clear();
        IncomingHealing.Clear();
        IncomingShield.Clear();
    }

    private void HandleScopeChanged(object? sender, EventArgs e)
    {
        if (_suppressScopeRefresh)
        {
            return;
        }

        _suppressScopeRefresh = true;
        try
        {
            if (ReferenceEquals(sender, OutgoingHealing) || ReferenceEquals(sender, OutgoingShield))
            {
                var combatantId = (ReferenceEquals(sender, OutgoingHealing) ? OutgoingHealing.SelectedScope : OutgoingShield.SelectedScope)?.CombatantId;
                SyncSectionScope(OutgoingHealing, combatantId);
                SyncSectionScope(OutgoingShield, combatantId);
            }
            else if (ReferenceEquals(sender, IncomingHealing) || ReferenceEquals(sender, IncomingShield))
            {
                var combatantId = (ReferenceEquals(sender, IncomingHealing) ? IncomingHealing.SelectedScope : IncomingShield.SelectedScope)?.CombatantId;
                SyncSectionScope(IncomingHealing, combatantId);
                SyncSectionScope(IncomingShield, combatantId);
            }
        }
        finally
        {
            _suppressScopeRefresh = false;
        }

        RefreshSections();
    }

    private void RefreshContext()
    {
        if (_combatantId is null || _battleContextId == Guid.Empty)
        {
            _currentSnapshot = new DamageMeterSnapshot();
            _currentStore = null;
            _battlePackets.Clear();
            CombatantName = string.Empty;
            ClearSectionsOnly();
            return;
        }

        if (!TryResolveContext(out var snapshot, out var store))
        {
            _currentSnapshot = new DamageMeterSnapshot();
            _currentStore = null;
            _battlePackets.Clear();
            CombatantName = string.Empty;
            ClearSectionsOnly();
            return;
        }

        if (!snapshot.Combatants.ContainsKey(_combatantId.Value))
        {
            _currentSnapshot = new DamageMeterSnapshot();
            _currentStore = null;
            _battlePackets.Clear();
            CombatantName = string.Empty;
            ClearSectionsOnly();
            return;
        }

        _currentSnapshot = snapshot;
        _currentStore = store;
        _battlePackets.Clear();
        _battlePackets.AddRange(CollectBattlePackets(snapshot, store));
        CombatantName = CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, _combatantId.Value);

        RebuildScopeOptions();
        RefreshSections();
    }

    private bool TryResolveContext(out DamageMeterSnapshot snapshot, out CombatMetricsStore store)
    {
        if (_battleArchiveService.TryGetBattle(_battleContextId, out var record) && record is not null)
        {
            snapshot = record.Snapshot;
            store = record.Store;
            return true;
        }

        if (_battleContextId == _engine.CurrentBattleId)
        {
            snapshot = _engine.CreateBattleSnapshot();
            store = _liveStore;
            return true;
        }

        snapshot = new DamageMeterSnapshot();
        store = new CombatMetricsStore();
        return false;
    }

    private static IEnumerable<ResolvedDetailPacket> CollectBattlePackets(
        DamageMeterSnapshot snapshot,
        CombatMetricsStore store)
    {
        if (snapshot.BattleTime <= 0 || snapshot.BattleStartTime <= 0 || snapshot.BattleEndTime < snapshot.BattleStartTime)
        {
            yield break;
        }

        foreach (var battlePacket in CombatMetricsEngine.EnumerateBattlePackets(store, snapshot.BattleStartTime, snapshot.BattleEndTime))
        {
            yield return new ResolvedDetailPacket(
                battlePacket.Packet,
                battlePacket.SourceId,
                battlePacket.TargetId);
        }
    }

    private void RebuildScopeOptions()
    {
        if (_combatantId is null || _currentStore is null)
        {
            return;
        }

        _suppressScopeRefresh = true;
        try
        {
            RebuildScopeOptionsForSection(OutgoingDamage, DetailSectionKind.OutgoingDamage);
            RebuildSharedScopeOptions(OutgoingHealing, OutgoingShield, DetailSectionKind.OutgoingHealing, DetailSectionKind.OutgoingShield);
            RebuildScopeOptionsForSection(IncomingDamage, DetailSectionKind.IncomingDamage);
            RebuildSharedScopeOptions(IncomingHealing, IncomingShield, DetailSectionKind.IncomingHealing, DetailSectionKind.IncomingShield);
        }
        finally
        {
            _suppressScopeRefresh = false;
        }
    }

    private void RebuildScopeOptionsForSection(SkillDetailSectionViewModel section, DetailSectionKind sectionKind)
    {
        var scopes = BuildScopeOptions(sectionKind);
        var selectedCombatantId = section.SelectedScope?.CombatantId;
        section.ReplaceScopeOptions(scopes);
        section.SelectedScope = scopes.FirstOrDefault(x => x.CombatantId == selectedCombatantId) ?? scopes.FirstOrDefault();
    }

    private void RebuildSharedScopeOptions(
        SkillDetailSectionViewModel primarySection,
        SkillDetailSectionViewModel secondarySection,
        params DetailSectionKind[] sectionKinds)
    {
        var scopes = BuildScopeOptions(sectionKinds);
        var selectedCombatantId = primarySection.SelectedScope?.CombatantId ?? secondarySection.SelectedScope?.CombatantId;

        primarySection.ReplaceScopeOptions(scopes);
        secondarySection.ReplaceScopeOptions(scopes.Select(static x => new SkillDetailScopeOption(x.CombatantId, x.DisplayName)).ToArray());

        var primarySelectedScope = primarySection.ScopeOptions.FirstOrDefault(x => x.CombatantId == selectedCombatantId) ?? primarySection.ScopeOptions.FirstOrDefault();
        var secondarySelectedScope = secondarySection.ScopeOptions.FirstOrDefault(x => x.CombatantId == selectedCombatantId) ?? secondarySection.ScopeOptions.FirstOrDefault();
        primarySection.SelectedScope = primarySelectedScope;
        secondarySection.SelectedScope = secondarySelectedScope;
    }

    private List<SkillDetailScopeOption> BuildScopeOptions(params DetailSectionKind[] sectionKinds)
    {
        var options = new List<SkillDetailScopeOption>
        {
            new(null, Localization["Scope.AllBattle"])
        };

        if (_combatantId is null || _currentStore is null)
        {
            return options;
        }

        var combatantDisplayNames = new Dictionary<int, string>();
        foreach (var detailPacket in _battlePackets)
        {
            var matchesAnySection = false;
            foreach (var sectionKind in sectionKinds)
            {
                if (!MatchesSection(detailPacket, sectionKind, _combatantId.Value) ||
                    !ContributesToSection(detailPacket.Packet, sectionKind))
                {
                    continue;
                }

                matchesAnySection = true;
                break;
            }

            if (!matchesAnySection)
            {
                continue;
            }

            var combatantId = GetCounterpartCombatantId(detailPacket, sectionKinds[0]);
            if (combatantId <= 0)
            {
                continue;
            }

            combatantDisplayNames.TryAdd(combatantId, CombatMetricsEngine.ResolveCombatantDisplayName(_currentStore, _currentSnapshot, combatantId));
        }

        if (combatantDisplayNames.Count == 0)
        {
            return options;
        }

        var sortedCombatantIds = new List<int>(combatantDisplayNames.Keys);
        sortedCombatantIds.Sort((left, right) => StringComparer.CurrentCulture.Compare(combatantDisplayNames[left], combatantDisplayNames[right]));

        foreach (var combatantId in sortedCombatantIds)
        {
            options.Add(new SkillDetailScopeOption(combatantId, combatantDisplayNames[combatantId]));
        }

        return options;
    }

    private void RefreshSections()
    {
        RefreshSection(OutgoingDamage, DetailSectionKind.OutgoingDamage);
        RefreshSection(OutgoingHealing, DetailSectionKind.OutgoingHealing);
        RefreshSection(OutgoingShield, DetailSectionKind.OutgoingShield);
        RefreshSection(IncomingDamage, DetailSectionKind.IncomingDamage);
        RefreshSection(IncomingHealing, DetailSectionKind.IncomingHealing);
        RefreshSection(IncomingShield, DetailSectionKind.IncomingShield);
    }

    private void RefreshSection(SkillDetailSectionViewModel section, DetailSectionKind sectionKind)
    {
        if (_combatantId is null)
        {
            section.Clear();
            return;
        }

        var selectedScopeCombatantId = section.SelectedScope?.CombatantId;
        var metrics = new Dictionary<int, SkillMetrics>();

        foreach (var detailPacket in _battlePackets)
        {
            if (!MatchesSection(detailPacket, sectionKind, _combatantId.Value))
            {
                continue;
            }

            if (selectedScopeCombatantId.HasValue && GetCounterpartCombatantId(detailPacket, sectionKind) != selectedScopeCombatantId.Value)
            {
                continue;
            }

            if (!ContributesToSection(detailPacket.Packet, sectionKind))
            {
                continue;
            }

            if (!metrics.TryGetValue(detailPacket.Packet.SkillCode, out var skill))
            {
                skill = new SkillMetrics(detailPacket.Packet);
                metrics[detailPacket.Packet.SkillCode] = skill;
            }

            skill.ProcessEvent(detailPacket.Packet);
        }

        var rows = sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage
            ? BuildDamageRows(metrics.Values)
            : sectionKind is DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield
                ? BuildShieldRows(metrics.Values)
                : BuildHealingRows(metrics.Values);

        ApplySectionRows(section, metrics.Values, rows, sectionKind);
    }

    private void ApplySectionRows(
        SkillDetailSectionViewModel section,
        IReadOnlyCollection<SkillMetrics> skills,
        List<SkillDetailRowViewModel> rows,
        DetailSectionKind sectionKind)
    {
        section.ReplaceRows(rows);
        section.Shield = rows.Sum(static x => x.ShieldAmount);
        section.SkillCount = rows.Count;
        section.HasSkills = rows.Count > 0;

        if (sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage)
        {
            ApplyDamageSection(section, skills);
            return;
        }

        section.Total = rows.Sum(static x => x.TotalAmount);
        section.Hits = rows.Sum(static x => x.Hits);
        section.Attempts = rows.Sum(static x => x.Attempts);
        section.PeriodicHits = rows.Sum(static x => x.PeriodicHits);
        section.Evades = rows.Sum(static x => x.Evades);
        section.Invincible = rows.Sum(static x => x.Invincible);
        section.Criticals = rows.Sum(static x => x.Criticals);

        var battleSeconds = _currentSnapshot.BattleTime > 0
            ? _currentSnapshot.BattleTime / 1000d
            : 0d;
        section.PerSecond = battleSeconds > 0 ? section.Total / battleSeconds : 0d;

        section.CriticalRate = 0d;
        section.SmiteRate = 0d;
        section.MultiHitRate = 0d;
        section.ParryRate = 0d;
        section.PerfectRate = 0d;
        section.EnduranceRate = 0d;
        section.BackRate = 0d;
        section.RegenerationRate = 0d;
        section.BlockRate = 0d;
        section.EvadeRate = 0d;
        section.InvincibleRate = 0d;
        section.SetDamageModifierSummaries(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private void ApplyDamageSection(SkillDetailSectionViewModel section, IEnumerable<SkillMetrics> skills)
    {
        var total = 0L;
        var totalHits = 0;
        var totalAttempts = 0;
        var totalPeriodicHits = 0;
        var critical = 0;
        var perfect = 0;
        var smite = 0;
        var multiHit = 0;
        var parry = 0;
        var block = 0;
        var endurance = 0;
        var regeneration = 0;
        var back = 0;
        var evades = 0;
        var invincible = 0;

        foreach (var skill in skills)
        {
            total += skill.DamageAmount + skill.PeriodicDamageAmount;
            totalHits += skill.Times;
            totalAttempts += skill.AttemptTimes;
            totalPeriodicHits += skill.PeriodicDamageTimes;
            evades += skill.EvadeTimes;
            invincible += skill.InvincibleTimes;
            critical += skill.CriticalTimes;
            perfect += skill.PerfectTimes;
            smite += skill.SmiteTimes;
            multiHit += skill.MultiHitTimes;
            parry += skill.ParryTimes;
            block += skill.BlockTimes;
            endurance += skill.EnduranceTimes;
            regeneration += skill.RegenerationTimes;
            back += skill.BackTimes;
        }

        section.Total = total;
        section.Hits = totalHits;
        section.Attempts = totalAttempts;
        section.PeriodicHits = totalPeriodicHits;
        section.Evades = evades;
        section.Invincible = invincible;
        section.Criticals = critical;

        var battleSeconds = _currentSnapshot.BattleTime > 0
            ? _currentSnapshot.BattleTime / 1000d
            : 0d;
        section.PerSecond = battleSeconds > 0 ? section.Total / battleSeconds : 0d;

        section.CriticalRate = totalHits > 0 ? (double)critical / totalHits : 0d;
        section.PerfectRate = totalHits > 0 ? (double)perfect / totalHits : 0d;
        section.SmiteRate = totalHits > 0 ? (double)smite / totalHits : 0d;
        section.MultiHitRate = totalHits > 0 ? (double)multiHit / totalHits : 0d;
        section.ParryRate = totalHits > 0 ? (double)parry / totalHits : 0d;
        section.BlockRate = totalHits > 0 ? (double)block / totalHits : 0d;
        section.EnduranceRate = totalHits > 0 ? (double)endurance / totalHits : 0d;
        section.RegenerationRate = totalHits > 0 ? (double)regeneration / totalHits : 0d;
        section.BackRate = totalHits > 0 ? (double)back / totalHits : 0d;
        section.EvadeRate = totalAttempts > 0 ? (double)evades / totalAttempts : 0d;
        section.InvincibleRate = totalAttempts > 0 ? (double)invincible / totalAttempts : 0d;
        section.SetDamageModifierSummaries(critical, perfect, smite, multiHit, parry, block, endurance, regeneration, back, evades, invincible);
    }

    private static bool MatchesSection(ResolvedDetailPacket packet, DetailSectionKind sectionKind, int combatantId)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.TargetId == combatantId,
            _ => false
        };
    }

    private static int GetCounterpartCombatantId(ResolvedDetailPacket packet, DetailSectionKind sectionKind)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.TargetId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.SourceId,
            _ => 0
        };
    }

    private static bool ContributesToSection(ParsedCombatPacket packet, DetailSectionKind sectionKind)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => ContributesDamage(packet),
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => ContributesHealing(packet),
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => ContributesShield(packet),
            _ => false
        };
    }

    private static bool ContributesDamage(ParsedCombatPacket packet)
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

    private static bool ContributesHealing(ParsedCombatPacket packet)
    {
        return packet.ValueKind switch
        {
            CombatValueKind.Healing => packet.Damage > 0,
            CombatValueKind.PeriodicHealing => packet.Damage > 0,
            CombatValueKind.DrainHealing => packet.Damage > 0,
            CombatValueKind.Shield => false,
            _ => packet.EventKind == CombatEventKind.Healing && packet.Damage > 0
        };
    }

    private static bool ContributesShield(ParsedCombatPacket packet)
        => packet.ValueKind == CombatValueKind.Shield && packet.Damage > 0;

    private static List<SkillDetailRowViewModel> BuildDamageRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = skills
            .Select(skill =>
            {
                if (IsHiddenDamageOutcomeSkill(skill.SkillCode))
                {
                    return null;
                }

                var totalAmount = skill.DamageAmount + skill.PeriodicDamageAmount;
                var directHits = skill.Times;
                var attempts = skill.AttemptTimes;
                var periodicHits = skill.PeriodicDamageTimes;
                var evades = skill.EvadeTimes;
                var invincible = skill.InvincibleTimes;
                if (totalAmount <= 0 && directHits <= 0 && periodicHits <= 0 && attempts <= 0 && evades <= 0 && invincible <= 0)
                {
                    return null;
                }

                return new SkillDetailRowViewModel(
                    skill.SkillCode,
                    ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                    totalAmount,
                    skill.DamageAmount,
                    skill.PeriodicDamageAmount,
                    0,
                    0,
                    directHits,
                    attempts,
                    periodicHits,
                    evades,
                    invincible,
                    skill.CriticalTimes,
                    skill.BackTimes,
                    skill.ParryTimes,
                    skill.PerfectTimes,
                    skill.SmiteTimes,
                    skill.MultiHitTimes,
                    skill.EnduranceTimes,
                    skill.RegenerationTimes,
                    skill.BlockTimes,
                    0d);
            })
            .Where(static row => row is not null)
            .Cast<SkillDetailRowViewModel>()
            .OrderByDescending(static row => row.TotalAmount)
            .ThenByDescending(static row => row.Hits)
            .ThenBy(static row => row.SkillName, StringComparer.CurrentCulture)
            .ToList();

        var sectionTotal = rows.Sum(static row => row.TotalAmount);
        if (sectionTotal <= 0)
        {
            return rows;
        }

        return rows.Select(row => row with { SharePercent = row.TotalAmount / (double)sectionTotal }).ToList();
    }

    private static bool IsHiddenDamageOutcomeSkill(int skillCode)
        => skillCode == SyntheticCombatSkillCodes.UnresolvedInvincible;

    private static List<SkillDetailRowViewModel> BuildHealingRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = skills
            .Select(skill =>
            {
                var directHealingAmount = Math.Max(0L, skill.HealingAmount - skill.PeriodicHealingAmount - skill.DrainHealingAmount);
                var directHealingHits = Math.Max(0, skill.HealingTimes - skill.PeriodicHealingTimes - skill.DrainHealingTimes);
                var totalAmount = directHealingAmount + skill.PeriodicHealingAmount + skill.DrainHealingAmount;
                var totalHits = directHealingHits + skill.PeriodicHealingTimes + skill.DrainHealingTimes;
                if (totalAmount <= 0 && totalHits <= 0)
                {
                    return null;
                }

                return new SkillDetailRowViewModel(
                    skill.SkillCode,
                    ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                    totalAmount,
                    directHealingAmount,
                    skill.PeriodicHealingAmount,
                    skill.DrainHealingAmount,
                    0,
                    totalHits,
                    totalHits,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0d);
            })
            .Where(static row => row is not null)
            .Cast<SkillDetailRowViewModel>()
            .OrderByDescending(static row => row.TotalAmount)
            .ThenByDescending(static row => row.Hits)
            .ThenBy(static row => row.SkillName, StringComparer.CurrentCulture)
            .ToList();

        var sectionTotal = rows.Sum(static row => row.TotalAmount);
        if (sectionTotal <= 0)
        {
            return rows;
        }

        return rows.Select(row => row with { SharePercent = row.TotalAmount / (double)sectionTotal }).ToList();
    }

    private static List<SkillDetailRowViewModel> BuildShieldRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = skills
            .Select(skill =>
            {
                if (skill.ShieldAmount <= 0 && skill.ShieldTimes <= 0)
                {
                    return null;
                }

                return new SkillDetailRowViewModel(
                    skill.SkillCode,
                    ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                    skill.ShieldAmount,
                    0,
                    0,
                    0,
                    skill.ShieldAmount,
                    skill.ShieldTimes,
                    skill.ShieldTimes,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0d);
            })
            .Where(static row => row is not null)
            .Cast<SkillDetailRowViewModel>()
            .OrderByDescending(static row => row.TotalAmount)
            .ThenByDescending(static row => row.Hits)
            .ThenBy(static row => row.SkillName, StringComparer.CurrentCulture)
            .ToList();

        var sectionTotal = rows.Sum(static row => row.TotalAmount);
        if (sectionTotal <= 0)
        {
            return rows;
        }

        return rows.Select(row => row with { SharePercent = row.TotalAmount / (double)sectionTotal }).ToList();
    }

    private static string ResolveSkillDisplayName(int skillCode, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(fallbackName) && fallbackName != skillCode.ToString(CultureInfo.InvariantCulture))
        {
            return fallbackName;
        }

        return CombatEventClassifier.DisplaySkillNameFor(skillCode);
    }

    private void ClearSectionsOnly()
    {
        OutgoingDamage.Clear();
        OutgoingHealing.Clear();
        OutgoingShield.Clear();
        IncomingDamage.Clear();
        IncomingHealing.Clear();
        IncomingShield.Clear();
    }

    private static void SyncSectionScope(SkillDetailSectionViewModel section, int? combatantId)
    {
        var selectedScope = section.ScopeOptions.FirstOrDefault(x => x.CombatantId == combatantId) ?? section.ScopeOptions.FirstOrDefault();
        if (Equals(section.SelectedScope, selectedScope))
        {
            return;
        }

        section.SelectedScope = selectedScope;
    }
}
