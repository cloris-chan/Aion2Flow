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

    private readonly struct ResolvedDetailPacket(ParsedCombatPacket Packet, int SourceId, int TargetId)
    {
        public readonly ParsedCombatPacket Packet = Packet;
        public readonly int SourceId = SourceId;
        public readonly int TargetId = TargetId;
    }

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
        secondarySection.ReplaceScopeOptions(scopes);

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
        var packetsSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_battlePackets);
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            var matchesAnySection = false;
            foreach (var sectionKind in sectionKinds)
            {
                if (!MatchesSection(in detailPacket, sectionKind, _combatantId.Value) ||
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

            var combatantId = GetCounterpartCombatantId(in detailPacket, sectionKinds[0]);
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

        var packetsSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_battlePackets);
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            if (!MatchesSection(in detailPacket, sectionKind, _combatantId.Value))
            {
                continue;
            }

            if (selectedScopeCombatantId.HasValue && GetCounterpartCombatantId(in detailPacket, sectionKind) != selectedScopeCombatantId.Value)
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
        List<SkillDetailRowData> rows,
        DetailSectionKind sectionKind)
    {
        section.ReplaceRows(rows);
        section.SkillCount = rows.Count;
        section.HasSkills = rows.Count > 0;

        if (sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage)
        {
            ApplyDamageSection(section, skills);
            return;
        }

        long totalAmount = 0, directAmount = 0, periodicAmount = 0, drainAmount = 0, shieldAmount = 0;
        int hits = 0, attempts = 0, periodicHits = 0, evades = 0, invincible = 0, criticals = 0;

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows);
        foreach (ref var row in span)
        {
            totalAmount += row.TotalAmount;
            directAmount += row.DirectAmount;
            periodicAmount += row.PeriodicAmount;
            drainAmount += row.DrainAmount;
            shieldAmount += row.ShieldAmount;
            hits += row.Hits;
            attempts += row.Attempts;
            periodicHits += row.PeriodicHits;
            evades += row.Evades;
            invincible += row.Invincible;
            criticals += row.Criticals;
        }

        section.Total = totalAmount;
        section.DirectTotal = directAmount;
        section.PeriodicTotal = periodicAmount;
        section.DrainTotal = drainAmount;
        section.Shield = shieldAmount;
        section.Hits = hits;
        section.Attempts = attempts;
        section.PeriodicHits = periodicHits;
        section.Evades = evades;
        section.Invincible = invincible;
        section.Criticals = criticals;
        section.PerfectCount = 0;
        section.SmiteCount = 0;
        section.MultiHitCount = 0;
        section.BackCount = 0;
        section.ParryCount = 0;
        section.BlockCount = 0;
        section.EnduranceCount = 0;
        section.RegenerationCount = 0;

        var battleSeconds = _currentSnapshot.BattleTime > 0
            ? _currentSnapshot.BattleTime / 1000d
            : 0d;
        section.PerSecond = battleSeconds > 0 ? totalAmount / battleSeconds : 0d;

        section.HitRate = 0d;
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
    }

    private void ApplyDamageSection(SkillDetailSectionViewModel section, IEnumerable<SkillMetrics> skills)
    {
        long total = 0, directTotal = 0, periodicTotal = 0;
        int totalHits = 0, totalAttempts = 0, totalPeriodicHits = 0;
        int critical = 0, perfect = 0, smite = 0, multiHit = 0;
        int parry = 0, block = 0, endurance = 0, regeneration = 0, back = 0;
        int evades = 0, invincible = 0;

        foreach (var skill in skills)
        {
            directTotal += skill.DamageAmount;
            periodicTotal += skill.PeriodicDamageAmount;
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
        section.DirectTotal = directTotal;
        section.PeriodicTotal = periodicTotal;
        section.DrainTotal = 0;
        section.Hits = totalHits;
        section.Attempts = totalAttempts;
        section.PeriodicHits = totalPeriodicHits;
        section.Evades = evades;
        section.Invincible = invincible;
        section.Criticals = critical;
        section.PerfectCount = perfect;
        section.SmiteCount = smite;
        section.MultiHitCount = multiHit;
        section.BackCount = back;
        section.ParryCount = parry;
        section.BlockCount = block;
        section.EnduranceCount = endurance;
        section.RegenerationCount = regeneration;

        var battleSeconds = _currentSnapshot.BattleTime > 0
            ? _currentSnapshot.BattleTime / 1000d
            : 0d;
        section.PerSecond = battleSeconds > 0 ? section.Total / battleSeconds : 0d;

        section.HitRate = totalAttempts > 0 ? totalHits / (double)totalAttempts : 0d;
        section.CriticalRate = totalHits > 0 ? critical / (double)totalHits : 0d;
        section.PerfectRate = totalHits > 0 ? perfect / (double)totalHits : 0d;
        section.SmiteRate = totalHits > 0 ? smite / (double)totalHits : 0d;
        section.MultiHitRate = totalHits > 0 ? multiHit / (double)totalHits : 0d;
        section.ParryRate = totalHits > 0 ? parry / (double)totalHits : 0d;
        section.BlockRate = totalHits > 0 ? block / (double)totalHits : 0d;
        section.EnduranceRate = totalHits > 0 ? endurance / (double)totalHits : 0d;
        section.RegenerationRate = totalHits > 0 ? regeneration / (double)totalHits : 0d;
        section.BackRate = totalHits > 0 ? back / (double)totalHits : 0d;
        section.EvadeRate = totalAttempts > 0 ? evades / (double)totalAttempts : 0d;
        section.InvincibleRate = totalAttempts > 0 ? invincible / (double)totalAttempts : 0d;
    }

    private static bool MatchesSection(in ResolvedDetailPacket packet, DetailSectionKind sectionKind, int combatantId)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.TargetId == combatantId,
            _ => false
        };
    }

    private static int GetCounterpartCombatantId(in ResolvedDetailPacket packet, DetailSectionKind sectionKind)
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

    private static List<SkillDetailRowData> BuildDamageRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var skill in skills)
        {
            if (IsHiddenDamageOutcomeSkill(skill.SkillCode))
            {
                continue;
            }

            var totalAmount = skill.DamageAmount + skill.PeriodicDamageAmount;
            var directHits = skill.Times;
            var attempts = skill.AttemptTimes;
            var periodicHits = skill.PeriodicDamageTimes;
            var evades = skill.EvadeTimes;
            var invincible = skill.InvincibleTimes;
            if (totalAmount <= 0 && directHits <= 0 && periodicHits <= 0 && attempts <= 0 && evades <= 0 && invincible <= 0)
            {
                continue;
            }

            rows.Add(new SkillDetailRowData
            {
                SkillCode = skill.SkillCode,
                SkillName = ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                TotalAmount = totalAmount,
                DirectAmount = skill.DamageAmount,
                PeriodicAmount = skill.PeriodicDamageAmount,
                Hits = directHits,
                Attempts = attempts,
                PeriodicHits = periodicHits,
                Evades = evades,
                Invincible = invincible,
                Criticals = skill.CriticalTimes,
                Back = skill.BackTimes,
                Parry = skill.ParryTimes,
                Perfect = skill.PerfectTimes,
                Smite = skill.SmiteTimes,
                MultiHit = skill.MultiHitTimes,
                Endurance = skill.EnduranceTimes,
                Regeneration = skill.RegenerationTimes,
                Block = skill.BlockTimes,
            });
        }

        rows.Sort(static (a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return StringComparer.CurrentCulture.Compare(a.SkillName, b.SkillName);
        });

        var sectionTotal = 0L;
        foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
    }

    private static bool IsHiddenDamageOutcomeSkill(int skillCode)
        => skillCode == SyntheticCombatSkillCodes.UnresolvedInvincible;

    private static List<SkillDetailRowData> BuildHealingRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var skill in skills)
        {
            var directHealingAmount = Math.Max(0L, skill.HealingAmount - skill.PeriodicHealingAmount - skill.DrainHealingAmount);
            var directHealingHits = Math.Max(0, skill.HealingTimes - skill.PeriodicHealingTimes - skill.DrainHealingTimes);
            var totalAmount = directHealingAmount + skill.PeriodicHealingAmount + skill.DrainHealingAmount;
            var totalHits = directHealingHits + skill.PeriodicHealingTimes + skill.DrainHealingTimes;
            if (totalAmount <= 0 && totalHits <= 0)
            {
                continue;
            }

            rows.Add(new SkillDetailRowData
            {
                SkillCode = skill.SkillCode,
                SkillName = ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                TotalAmount = totalAmount,
                DirectAmount = directHealingAmount,
                PeriodicAmount = skill.PeriodicHealingAmount,
                DrainAmount = skill.DrainHealingAmount,
                Hits = totalHits,
                Attempts = totalHits,
                PeriodicHits = skill.PeriodicHealingTimes,
            });
        }

        rows.Sort(static (a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return StringComparer.CurrentCulture.Compare(a.SkillName, b.SkillName);
        });

        var sectionTotal = 0L;
        foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
    }

    private static List<SkillDetailRowData> BuildShieldRows(IEnumerable<SkillMetrics> skills)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var skill in skills)
        {
            if (skill.ShieldAmount <= 0 && skill.ShieldTimes <= 0)
            {
                continue;
            }

            rows.Add(new SkillDetailRowData
            {
                SkillCode = skill.SkillCode,
                SkillName = ResolveSkillDisplayName(skill.SkillCode, skill.SkillName),
                TotalAmount = skill.ShieldAmount,
                ShieldAmount = skill.ShieldAmount,
                Hits = skill.ShieldTimes,
                Attempts = skill.ShieldTimes,
            });
        }

        rows.Sort(static (a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return StringComparer.CurrentCulture.Compare(a.SkillName, b.SkillName);
        });

        var sectionTotal = 0L;
        foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
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
