using System.Diagnostics;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public readonly record struct CombatantDetailRefreshBaselineCounters(
    TimeSpan Elapsed,
    long AllocatedBytes,
    int DetailEventCount,
    int DetailRowCount,
    int CounterpartCount)
{
    public static CombatantDetailRefreshBaselineCounters Empty { get; } = new(TimeSpan.Zero, 0, 0, 0, 0);
}

public sealed partial class CombatantDetailsFlyoutViewModel : ObservableObject
{
    private struct CounterpartAggregateMetrics
    {
        public long DamageAmount;
        public long HealingAmount;
        public long ShieldAmount;
    }

    private readonly List<CombatDetailEvent> _detailEvents = [];
    private SceneCombatSnapshot _currentSnapshot = new();
    private Guid _encounterContextId;
    private int? _combatantId;
    private long _detailRevision = -1;

    private enum DetailSectionKind
    {
        OutgoingDamage,
        OutgoingHealing,
        OutgoingShield,
        IncomingDamage,
        IncomingHealing,
        IncomingShield
    }

    public CombatantDetailsFlyoutViewModel(LocalizationService localization)
    {
        OutgoingDetail = new CombatDirectionDetailViewModel(localization, "Direction_Targets");
        IncomingDetail = new CombatDirectionDetailViewModel(localization, "Direction_Sources");
        OutgoingDetail.DamageCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        OutgoingDetail.SupportCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.DamageCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.SupportCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
    }

    public CombatDirectionDetailViewModel OutgoingDetail { get; }

    public CombatDirectionDetailViewModel IncomingDetail { get; }

    public SkillDetailSectionViewModel OutgoingDamage => OutgoingDetail.DamageSection;

    public SkillDetailSectionViewModel OutgoingHealing => OutgoingDetail.HealingSection;

    public SkillDetailSectionViewModel OutgoingShield => OutgoingDetail.ShieldSection;

    public SkillDetailSectionViewModel IncomingDamage => IncomingDetail.DamageSection;

    public SkillDetailSectionViewModel IncomingHealing => IncomingDetail.HealingSection;

    public SkillDetailSectionViewModel IncomingShield => IncomingDetail.ShieldSection;

    public CombatantDetailRefreshBaselineCounters LastRefreshBaselineCounters { get; private set; }

    [ObservableProperty]
    public partial SceneDisplayContext? DisplayContext { get; set; }

    [ObservableProperty]
    public partial int SelectedCombatantId { get; set; }

    [ObservableProperty]
    public partial int SelectedDirectionIndex { get; set; }

    public bool IsOutgoingSelected
    {
        get => SelectedDirectionIndex == 0;
        set
        {
            if (value)
            {
                SelectedDirectionIndex = 0;
            }
        }
    }

    public bool IsIncomingSelected
    {
        get => SelectedDirectionIndex == 1;
        set
        {
            if (value)
            {
                SelectedDirectionIndex = 1;
            }
        }
    }

    partial void OnSelectedDirectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOutgoingSelected));
        OnPropertyChanged(nameof(IsIncomingSelected));
    }

    public void SelectSceneEncounterCombatant(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailDelta detail, bool forceRefresh = false)
    {
        var baselineStart = CaptureBaselineStart();
        RefreshSceneContext(encounterContextId, combatantId, snapshot, detail, forceRefresh);
        LastRefreshBaselineCounters = CaptureRefreshBaselineCounter(baselineStart);
    }

    public void Clear()
    {
        _encounterContextId = Guid.Empty;
        _combatantId = null;
        _currentSnapshot = new SceneCombatSnapshot();
        _detailEvents.Clear();
        _detailRevision = -1;
        SelectedCombatantId = 0;
        SelectedDirectionIndex = 0;
        LastRefreshBaselineCounters = CombatantDetailRefreshBaselineCounters.Empty;
        OutgoingDetail.Clear();
        IncomingDetail.Clear();
    }

    private static DetailBaselineStart CaptureBaselineStart()
        => new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    private CombatantDetailRefreshBaselineCounters CaptureRefreshBaselineCounter(DetailBaselineStart start)
    {
        var elapsed = Stopwatch.GetElapsedTime(start.Timestamp);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - start.AllocatedBytes;
        return new CombatantDetailRefreshBaselineCounters(
            elapsed,
            Math.Max(0, allocatedBytes),
            _detailEvents.Count,
            CountDetailRows(),
            CountCounterpartOptions());
    }

    private int CountDetailRows()
        => OutgoingDamage.Rows.Count +
           OutgoingHealing.Rows.Count +
           OutgoingShield.Rows.Count +
           IncomingDamage.Rows.Count +
           IncomingHealing.Rows.Count +
           IncomingShield.Rows.Count;

    private int CountCounterpartOptions()
        => OutgoingDetail.DamageCounterpartFilter.Counterparts.Count +
           OutgoingDetail.SupportCounterpartFilter.Counterparts.Count +
           IncomingDetail.DamageCounterpartFilter.Counterparts.Count +
           IncomingDetail.SupportCounterpartFilter.Counterparts.Count;

    private void HandleCounterpartSelectionChanged(object? sender, EventArgs e)
    {
        if (_combatantId is null)
        {
            return;
        }

        if (ReferenceEquals(sender, OutgoingDetail.DamageCounterpartFilter) ||
            ReferenceEquals(sender, OutgoingDetail.SupportCounterpartFilter))
        {
            RefreshDirection(
                OutgoingDetail,
                DetailSectionKind.OutgoingDamage,
                DetailSectionKind.OutgoingHealing,
                DetailSectionKind.OutgoingShield);
        }
        else if (ReferenceEquals(sender, IncomingDetail.DamageCounterpartFilter) ||
                 ReferenceEquals(sender, IncomingDetail.SupportCounterpartFilter))
        {
            RefreshDirection(
                IncomingDetail,
                DetailSectionKind.IncomingDamage,
                DetailSectionKind.IncomingHealing,
                DetailSectionKind.IncomingShield);
        }
    }

    private void RefreshSceneContext(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailDelta detail, bool forceRefresh)
    {
        if (combatantId is null || encounterContextId == Guid.Empty || !snapshot.Combatants.ContainsKey(combatantId.Value))
        {
            _encounterContextId = encounterContextId;
            _combatantId = combatantId;
            _currentSnapshot = new SceneCombatSnapshot();
            _detailEvents.Clear();
            _detailRevision = -1;
            SelectedCombatantId = 0;
            ClearSectionsOnly();
            return;
        }

        var nextDetailRevision = detail.Revision;
        var canReuseExistingSections = !forceRefresh &&
            _encounterContextId == encounterContextId &&
            _combatantId == combatantId &&
            _detailRevision == nextDetailRevision;

        _encounterContextId = encounterContextId;
        _combatantId = combatantId;
        _currentSnapshot = snapshot;
        SelectedCombatantId = combatantId.Value;

        if (canReuseExistingSections)
        {
            RefreshSectionRatesOnly();
            return;
        }

        _detailRevision = nextDetailRevision;
        _detailEvents.Clear();
        _detailEvents.AddRange(detail.Events);

        RebuildCounterpartSelections();
        RefreshAllSections();
    }

    private void RebuildCounterpartSelections()
    {
        if (_combatantId is null)
        {
            return;
        }

        OutgoingDetail.DamageCounterpartFilter.ReplaceCounterparts(BuildCounterpartOptions(
            DetailSectionKind.OutgoingDamage));
        OutgoingDetail.SupportCounterpartFilter.ReplaceCounterparts(BuildCounterpartOptions(
            DetailSectionKind.OutgoingHealing,
            DetailSectionKind.OutgoingShield));
        IncomingDetail.DamageCounterpartFilter.ReplaceCounterparts(BuildCounterpartOptions(
            DetailSectionKind.IncomingDamage));
        IncomingDetail.SupportCounterpartFilter.ReplaceCounterparts(BuildCounterpartOptions(
            DetailSectionKind.IncomingHealing,
            DetailSectionKind.IncomingShield));
    }

    private List<DetailCounterpartOption> BuildCounterpartOptions(params DetailSectionKind[] sectionKinds)
    {
        if (_combatantId is null)
        {
            return [];
        }

        var counterpartMetrics = new Dictionary<int, CounterpartAggregateMetrics>();
        var packetsSpan = CollectionsMarshal.AsSpan(_detailEvents);
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            foreach (var sectionKind in sectionKinds)
            {
                if (!MatchesSection(in detailPacket, sectionKind, _combatantId.Value) ||
                    !ContributesToSection(in detailPacket, sectionKind))
                {
                    continue;
                }

                var combatantId = GetCounterpartCombatantId(in detailPacket, sectionKind);
                if (combatantId <= 0)
                {
                    break;
                }

                counterpartMetrics.TryGetValue(combatantId, out var metrics);

                var amount = GetSectionContributionAmount(in detailPacket, sectionKind);
                switch (sectionKind)
                {
                    case DetailSectionKind.OutgoingDamage:
                    case DetailSectionKind.IncomingDamage:
                        metrics.DamageAmount += amount;
                        break;
                    case DetailSectionKind.OutgoingHealing:
                    case DetailSectionKind.IncomingHealing:
                        metrics.HealingAmount += amount;
                        break;
                    case DetailSectionKind.OutgoingShield:
                    case DetailSectionKind.IncomingShield:
                        metrics.ShieldAmount += amount;
                        break;
                }

                counterpartMetrics[combatantId] = metrics;
                break;
            }
        }

        long totalDamage = 0, totalHealing = 0, totalShield = 0;
        foreach (var metrics in counterpartMetrics.Values)
        {
            totalDamage += metrics.DamageAmount;
            totalHealing += metrics.HealingAmount;
            totalShield += metrics.ShieldAmount;
        }

        var sortedCombatantIds = new List<int>(counterpartMetrics.Keys);
        sortedCombatantIds.Sort((left, right) =>
        {
            var leftMetrics = counterpartMetrics[left];
            var rightMetrics = counterpartMetrics[right];
            var cmp = (rightMetrics.DamageAmount + rightMetrics.HealingAmount + rightMetrics.ShieldAmount)
                .CompareTo(leftMetrics.DamageAmount + leftMetrics.HealingAmount + leftMetrics.ShieldAmount);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = rightMetrics.DamageAmount.CompareTo(leftMetrics.DamageAmount);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = rightMetrics.HealingAmount.CompareTo(leftMetrics.HealingAmount);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = rightMetrics.ShieldAmount.CompareTo(leftMetrics.ShieldAmount);
            if (cmp != 0)
            {
                return cmp;
            }

            var leftName = DisplayContext?.GetEntitySortKey(left) ?? left.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var rightName = DisplayContext?.GetEntitySortKey(right) ?? right.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StringComparer.CurrentCulture.Compare(leftName, rightName);
        });

        var options = new List<DetailCounterpartOption>(sortedCombatantIds.Count);
        foreach (var combatantId in sortedCombatantIds)
        {
            var metrics = counterpartMetrics[combatantId];
            options.Add(new DetailCounterpartOption(
                combatantId,
                metrics.DamageAmount,
                totalDamage > 0 ? metrics.DamageAmount / (double)totalDamage : 0d,
                metrics.HealingAmount,
                totalHealing > 0 ? metrics.HealingAmount / (double)totalHealing : 0d,
                metrics.ShieldAmount,
                totalShield > 0 ? metrics.ShieldAmount / (double)totalShield : 0d));
        }

        return options;
    }

    private void RefreshAllSections()
    {
        RefreshDirection(
            OutgoingDetail,
            DetailSectionKind.OutgoingDamage,
            DetailSectionKind.OutgoingHealing,
            DetailSectionKind.OutgoingShield);
        RefreshDirection(
            IncomingDetail,
            DetailSectionKind.IncomingDamage,
            DetailSectionKind.IncomingHealing,
            DetailSectionKind.IncomingShield);
    }

    private void RefreshDirection(
        CombatDirectionDetailViewModel directionDetail,
        DetailSectionKind damageSectionKind,
        DetailSectionKind healingSectionKind,
        DetailSectionKind shieldSectionKind)
    {
        var selectedDamageCounterpartIds = directionDetail.DamageCounterpartFilter.GetSelectedCounterpartIds();
        var selectableDamageCounterpartCount = directionDetail.DamageCounterpartFilter.Counterparts.Count;
        var selectedSupportCounterpartIds = directionDetail.SupportCounterpartFilter.GetSelectedCounterpartIds();
        var selectableSupportCounterpartCount = directionDetail.SupportCounterpartFilter.Counterparts.Count;

        RefreshSection(directionDetail.DamageSection, damageSectionKind, selectedDamageCounterpartIds, selectableDamageCounterpartCount);
        RefreshSection(directionDetail.HealingSection, healingSectionKind, selectedSupportCounterpartIds, selectableSupportCounterpartCount);
        RefreshSection(directionDetail.ShieldSection, shieldSectionKind, selectedSupportCounterpartIds, selectableSupportCounterpartCount);
    }

    private void RefreshSection(
        SkillDetailSectionViewModel section,
        DetailSectionKind sectionKind,
        HashSet<int> selectedCounterpartIds,
        int selectableCounterpartCount)
    {
        if (_combatantId is null)
        {
            section.Clear();
            return;
        }

        var metrics = new Dictionary<int, SkillMetrics>();
        var hasSubsetFilter = selectableCounterpartCount > 0 && selectedCounterpartIds.Count != selectableCounterpartCount;
        var firstObserved = long.MaxValue;
        var lastObserved = long.MinValue;

        var packetsSpan = CollectionsMarshal.AsSpan(_detailEvents);
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            if (!MatchesSection(in detailPacket, sectionKind, _combatantId.Value))
            {
                continue;
            }

            var counterpartCombatantId = GetCounterpartCombatantId(in detailPacket, sectionKind);
            if (counterpartCombatantId > 0)
            {
                if (!selectedCounterpartIds.Contains(counterpartCombatantId))
                {
                    continue;
                }
            }
            else if (hasSubsetFilter)
            {
                continue;
            }

            if (!ContributesToSection(in detailPacket, sectionKind))
            {
                continue;
            }

            var observedAt = ResolveObservedAt(in detailPacket);
            if (observedAt > 0)
            {
                firstObserved = Math.Min(firstObserved, observedAt);
                lastObserved = Math.Max(lastObserved, observedAt);
            }

            ref var skill = ref CollectionsMarshal.GetValueRefOrAddDefault(metrics, detailPacket.SkillCode, out var exists);
            if (!exists)
            {
                var observation = detailPacket.Observation;
                skill = new SkillMetrics(in observation);
            }

            var skillObservation = detailPacket.Observation;
            skill.ProcessObservation(in skillObservation);
        }

        var rows = sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage
            ? BuildDamageRows(metrics, DisplayContext)
            : sectionKind is DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield
                ? BuildShieldRows(metrics, DisplayContext)
                : BuildHealingRows(metrics, DisplayContext);

        var durationSeconds = hasSubsetFilter
            ? ResolveObservedDurationSeconds(firstObserved, lastObserved)
            : ResolveSceneDurationSeconds();
        ApplySectionRows(section, metrics, rows, sectionKind, durationSeconds, !hasSubsetFilter);
    }

    private static void ApplySectionRows(
        SkillDetailSectionViewModel section,
        Dictionary<int, SkillMetrics> skills,
        List<SkillDetailRowData> rows,
        DetailSectionKind sectionKind,
        double durationSeconds,
        bool usesSceneDuration)
    {
        section.ReplaceRows(rows);
        section.SkillCount = rows.Count;
        section.HasSkills = rows.Count > 0;
        section.DurationSeconds = durationSeconds;
        section.UsesSceneDuration = usesSceneDuration;

        if (sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage)
        {
            ApplyDamageSection(section, skills, durationSeconds);
            return;
        }

        long totalAmount = 0, directAmount = 0, periodicAmount = 0, drainAmount = 0, regenerationAmount = 0, shieldAmount = 0, shieldAbsorbedAmount = 0;
        int hits = 0, attempts = 0, periodicHits = 0, evades = 0, invincible = 0, criticals = 0;

        var span = CollectionsMarshal.AsSpan(rows);
        foreach (ref var row in span)
        {
            totalAmount += row.TotalAmount;
            directAmount += row.DirectAmount;
            periodicAmount += row.PeriodicAmount;
            drainAmount += row.DrainAmount;
            regenerationAmount += row.RegenerationAmount;
            shieldAmount += row.ShieldAmount;
            shieldAbsorbedAmount += row.ShieldAbsorbedAmount;
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
        section.RegenerationTotal = regenerationAmount;
        section.Shield = shieldAmount;
        section.ShieldAbsorbed = shieldAbsorbedAmount;
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

        section.PerSecond = durationSeconds > 0 ? totalAmount / durationSeconds : 0d;

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

    private static void ApplyDamageSection(SkillDetailSectionViewModel section, Dictionary<int, SkillMetrics> skills, double durationSeconds)
    {
        long total = 0, directTotal = 0, periodicTotal = 0;
        int totalHits = 0, totalAttempts = 0, totalPeriodicHits = 0;
        int critical = 0, perfect = 0, smite = 0, multiHit = 0;
        int parry = 0, block = 0, endurance = 0, regeneration = 0, back = 0;
        int evades = 0, invincible = 0;

        foreach (var (_, skill) in skills)
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

        section.PerSecond = durationSeconds > 0 ? section.Total / durationSeconds : 0d;

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

    private static bool MatchesSection(in CombatDetailEvent packet, DetailSectionKind sectionKind, int combatantId)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.SourceId == combatantId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.TargetId == combatantId,
            _ => false
        };
    }

    private static int GetCounterpartCombatantId(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        if (sectionKind == DetailSectionKind.IncomingShield &&
            packet.ValueKind == CombatValueKind.Shield &&
            packet.SourceId > 0 &&
            packet.TargetId > 0 &&
            packet.SourceId != packet.TargetId)
        {
            return 0;
        }

        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.OutgoingHealing or DetailSectionKind.OutgoingShield => packet.TargetId,
            DetailSectionKind.IncomingDamage or DetailSectionKind.IncomingHealing or DetailSectionKind.IncomingShield => packet.SourceId,
            _ => 0
        };
    }

    private static bool ContributesToSection(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        var observation = packet.Observation;
        var contribution = CombatContributionClassifier.Evaluate(in observation);
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => contribution.CountsAsDamage,
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => contribution.CountsAsHealing,
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => contribution.CountsAsShieldGrant || contribution.CountsAsShieldAbsorbed,
            _ => false
        };
    }

    private static long GetSectionContributionAmount(in CombatDetailEvent packet, DetailSectionKind sectionKind)
    {
        return sectionKind switch
        {
            DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage => Math.Max(0L, packet.Amount),
            DetailSectionKind.OutgoingHealing or DetailSectionKind.IncomingHealing => Math.Max(0L, packet.Amount),
            DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield => Math.Max(0L, packet.Amount),
            _ => 0L
        };
    }

    private double ResolveSceneDurationSeconds()
        => _currentSnapshot.EncounterTime > 0 ? Math.Max(1d, _currentSnapshot.EncounterTime / 1000d) : 0d;

    private static double ResolveObservedDurationSeconds(long firstObserved, long lastObserved)
        => firstObserved != long.MaxValue && lastObserved != long.MinValue ? Math.Max(1d, Math.Max(0, lastObserved - firstObserved) / 1000d) : 0d;

    private static long ResolveObservedAt(in CombatDetailEvent detailPacket)
        => detailPacket.ObservedAt;

    private static List<SkillDetailRowData> BuildDamageRows(Dictionary<int, SkillMetrics> skills, SceneDisplayContext? displayContext)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var (_, skill) in skills)
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

        rows.Sort((a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return CompareSkillNames(a.SkillCode, b.SkillCode, displayContext);
        });

        var sectionTotal = 0L;
        foreach (ref var row in CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
    }

    private static bool IsHiddenDamageOutcomeSkill(int skillCode)
        => skillCode == SyntheticCombatSkillCodes.UnresolvedInvincible;

    private static List<SkillDetailRowData> BuildHealingRows(Dictionary<int, SkillMetrics> skills, SceneDisplayContext? displayContext)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var (_, skill) in skills)
        {
            var directHealingAmount = Math.Max(0L, skill.HealingAmount - skill.PeriodicHealingAmount - skill.DrainHealingAmount - skill.RegenerationHealingAmount);
            var directHealingHits = Math.Max(0, skill.HealingTimes - skill.PeriodicHealingTimes - skill.DrainHealingTimes - skill.RegenerationHealingTimes);
            var totalAmount = directHealingAmount + skill.PeriodicHealingAmount + skill.DrainHealingAmount + skill.RegenerationHealingAmount;
            var totalHits = directHealingHits + skill.PeriodicHealingTimes + skill.DrainHealingTimes + skill.RegenerationHealingTimes;
            if (totalAmount <= 0 && totalHits <= 0)
            {
                continue;
            }

            rows.Add(new SkillDetailRowData
            {
                SkillCode = skill.SkillCode,
                TotalAmount = totalAmount,
                DirectAmount = directHealingAmount,
                PeriodicAmount = skill.PeriodicHealingAmount,
                DrainAmount = skill.DrainHealingAmount,
                RegenerationAmount = skill.RegenerationHealingAmount,
                Hits = totalHits,
                Attempts = totalHits,
                PeriodicHits = skill.PeriodicHealingTimes,
            });
        }

        rows.Sort((a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return CompareSkillNames(a.SkillCode, b.SkillCode, displayContext);
        });

        var sectionTotal = 0L;
        foreach (ref var row in CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
    }

    private static List<SkillDetailRowData> BuildShieldRows(Dictionary<int, SkillMetrics> skills, SceneDisplayContext? displayContext)
    {
        var rows = new List<SkillDetailRowData>();
        foreach (var (_, skill) in skills)
        {
            if (skill.ShieldAmount <= 0 && skill.ShieldTimes <= 0 &&
                skill.ShieldAbsorbedAmount <= 0 && skill.ShieldAbsorbedTimes <= 0)
            {
                continue;
            }

            rows.Add(new SkillDetailRowData
            {
                SkillCode = skill.SkillCode,
                TotalAmount = skill.ShieldAmount,
                ShieldAmount = skill.ShieldAmount,
                ShieldAbsorbedAmount = skill.ShieldAbsorbedAmount,
                Hits = skill.ShieldTimes,
                Attempts = skill.ShieldTimes,
            });
        }

        rows.Sort((a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return CompareSkillNames(a.SkillCode, b.SkillCode, displayContext);
        });

        var sectionTotal = 0L;
        foreach (ref var row in CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal > 0)
        {
            foreach (ref var row in CollectionsMarshal.AsSpan(rows))
            {
                row.SharePercent = row.TotalAmount / (double)sectionTotal;
            }
        }

        return rows;
    }

    private static int CompareSkillNames(int leftSkillCode, int rightSkillCode, SceneDisplayContext? displayContext)
    {
        var leftName = displayContext?.GetSkillSortKey(leftSkillCode) ?? leftSkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rightName = displayContext?.GetSkillSortKey(rightSkillCode) ?? rightSkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return StringComparer.CurrentCulture.Compare(leftName, rightName);
    }

    private void RefreshSectionRatesOnly()
    {
        RefreshSectionPerSecond(OutgoingDamage);
        RefreshSectionPerSecond(OutgoingHealing);
        RefreshSectionPerSecond(OutgoingShield);
        RefreshSectionPerSecond(IncomingDamage);
        RefreshSectionPerSecond(IncomingHealing);
        RefreshSectionPerSecond(IncomingShield);
    }

    private void RefreshSectionPerSecond(SkillDetailSectionViewModel section)
    {
        if (section.UsesSceneDuration)
        {
            section.DurationSeconds = ResolveSceneDurationSeconds();
        }

        section.PerSecond = section.DurationSeconds > 0 ? section.Total / section.DurationSeconds : 0d;
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


    private readonly record struct DetailBaselineStart(long Timestamp, long AllocatedBytes);
}
