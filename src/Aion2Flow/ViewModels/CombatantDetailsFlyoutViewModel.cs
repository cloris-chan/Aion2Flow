using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class CombatantDetailsFlyoutViewModel : ObservableObject, ICombatDetailEventWriter, IDisposable
{
    private readonly List<CombatDetailEvent> _detailEvents = [];
    private readonly DetailCounterpartOptionBuilder _counterpartOptionBuilder = new();
    private readonly HashSet<int> _outgoingDamageSelectionIds = [];
    private readonly HashSet<int> _outgoingSupportSelectionIds = [];
    private readonly HashSet<int> _incomingDamageSelectionIds = [];
    private readonly HashSet<int> _incomingSupportSelectionIds = [];
    private readonly SkillDetailSectionAggregation _outgoingDamageSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _outgoingHealingSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _outgoingShieldSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingDamageSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingHealingSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingShieldSectionAggregation = new();
    private readonly List<SkillDetailRowData> _sectionRows = [];
    private readonly Dictionary<SkillBaseKey, int> _sectionRowIndexes = [];
    private readonly LocalizationService _localization;
    private SceneCombatSnapshot _currentSnapshot = new();
    private Guid _encounterContextId;
    private int? _combatantId;
    private long _detailRevision = -1;
    private bool _disposed;

    public CombatantDetailsFlyoutViewModel(LocalizationService localization, UiFrameBatchService frameBatchService)
    {
        _localization = localization;
        OutgoingDetail = new CombatDirectionDetailViewModel(localization, frameBatchService, "Direction_Targets");
        IncomingDetail = new CombatDirectionDetailViewModel(localization, frameBatchService, "Direction_Sources");
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

    public void SynchronizeSkillSelection(bool isOutgoing, CombatContributionCategory category, SkillBaseKey baseKey)
    {
        OutgoingDamage.SelectRowByKey(isOutgoing && category == CombatContributionCategory.Damage ? baseKey : null);
        OutgoingHealing.SelectRowByKey(isOutgoing && category == CombatContributionCategory.Healing ? baseKey : null);
        OutgoingShield.SelectRowByKey(isOutgoing && category == CombatContributionCategory.Shield ? baseKey : null);
        IncomingDamage.SelectRowByKey(!isOutgoing && category == CombatContributionCategory.Damage ? baseKey : null);
        IncomingHealing.SelectRowByKey(!isOutgoing && category == CombatContributionCategory.Healing ? baseKey : null);
        IncomingShield.SelectRowByKey(!isOutgoing && category == CombatContributionCategory.Shield ? baseKey : null);
    }

    public void ClearSkillSelection()
    {
        OutgoingDamage.SelectRowByKey(null);
        OutgoingHealing.SelectRowByKey(null);
        OutgoingShield.SelectRowByKey(null);
        IncomingDamage.SelectRowByKey(null);
        IncomingHealing.SelectRowByKey(null);
        IncomingShield.SelectRowByKey(null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeCounterpartFilter(OutgoingDetail.DamageCounterpartFilter);
        DisposeCounterpartFilter(OutgoingDetail.SupportCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.DamageCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.SupportCounterpartFilter);
    }

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
        RefreshSceneContext(encounterContextId, combatantId, snapshot, detail, forceRefresh);
    }

    public void SelectLiveSceneEncounterCombatant(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailUpdateResult update, bool forceRefresh = false)
    {
        RefreshLiveSceneContext(encounterContextId, combatantId, snapshot, update, forceRefresh);
    }

    public void SelectPlaybackSceneEncounterCombatant(Guid encounterContextId, int combatantId, SceneCombatSnapshot snapshot, CombatDetailUpdateResult update, IReadOnlyList<CombatDetailEvent> events)
    {
        if (update.IsFullSnapshot)
            _detailEvents.Clear();

        for (var i = 0; i < events.Count; i++)
            _detailEvents.Add(events[i]);

        RefreshLiveSceneContext(encounterContextId, combatantId, snapshot, update, forceRefresh: false);
    }

    public void Deactivate()
    {
        SelectedCombatantId = 0;
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
        OutgoingDetail.Clear();
        IncomingDetail.Clear();
    }

    void ICombatDetailEventWriter.Clear() => _detailEvents.Clear();

    void ICombatDetailEventWriter.Add(in CombatDetailEvent detailEvent) => _detailEvents.Add(detailEvent);

    private void DisposeCounterpartFilter(DetailCounterpartFilterViewModel filter)
    {
        filter.SelectionChanged -= HandleCounterpartSelectionChanged;
        filter.Dispose();
    }

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
        if (combatantId is null ||
            encounterContextId == Guid.Empty ||
            !snapshot.Combatants.ContainsKey(combatantId.Value) && detail.Combatant is null && detail.Events.Count == 0)
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

    private void RefreshLiveSceneContext(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailUpdateResult update, bool forceRefresh)
    {
        if (combatantId is null ||
            encounterContextId == Guid.Empty ||
            !snapshot.Combatants.ContainsKey(combatantId.Value) && update.Combatant is null && _detailEvents.Count == 0)
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

        var canReuseExistingSections = !forceRefresh &&
            _encounterContextId == encounterContextId &&
            _combatantId == combatantId &&
            _detailRevision == update.Revision &&
            !update.IsFullSnapshot &&
            !update.HasChanges;

        _encounterContextId = encounterContextId;
        _combatantId = combatantId;
        _currentSnapshot = snapshot;
        SelectedCombatantId = combatantId.Value;

        if (canReuseExistingSections)
        {
            RefreshSectionRatesOnly();
            return;
        }

        _detailRevision = update.Revision;
        if (update.IsFullSnapshot || update.AddedEventCount > 0 || forceRefresh)
        {
            RebuildCounterpartSelections();
            RefreshAllSections();
            return;
        }

        RefreshSectionRatesOnly();
    }

    private void RebuildCounterpartSelections()
    {
        if (_combatantId is null)
        {
            return;
        }

        _counterpartOptionBuilder.Accumulate(CollectionsMarshal.AsSpan(_detailEvents), _combatantId.Value);
        OutgoingDetail.DamageCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingDamageOptions(DisplayContext));
        OutgoingDetail.SupportCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingSupportOptions(DisplayContext));
        IncomingDetail.DamageCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingDamageOptions(DisplayContext));
        IncomingDetail.SupportCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingSupportOptions(DisplayContext));
    }

    private void RefreshAllSections()
    {
        if (_combatantId is null)
        {
            ClearSectionsOnly();
            return;
        }

        CopyDirectionSelections(OutgoingDetail, _outgoingDamageSelectionIds, _outgoingSupportSelectionIds, out var outgoingDamageCounterpartCount, out var outgoingSupportCounterpartCount);
        CopyDirectionSelections(IncomingDetail, _incomingDamageSelectionIds, _incomingSupportSelectionIds, out var incomingDamageCounterpartCount, out var incomingSupportCounterpartCount);

        ResetDirectionAggregations(
            _outgoingDamageSectionAggregation,
            _outgoingHealingSectionAggregation,
            _outgoingShieldSectionAggregation,
            _outgoingDamageSelectionIds,
            _outgoingSupportSelectionIds,
            outgoingDamageCounterpartCount,
            outgoingSupportCounterpartCount);
        ResetDirectionAggregations(
            _incomingDamageSectionAggregation,
            _incomingHealingSectionAggregation,
            _incomingShieldSectionAggregation,
            _incomingDamageSelectionIds,
            _incomingSupportSelectionIds,
            incomingDamageCounterpartCount,
            incomingSupportCounterpartCount);

        var packetsSpan = CollectionsMarshal.AsSpan(_detailEvents);
        var combatantId = _combatantId.Value;
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            AccumulateSection(_outgoingDamageSectionAggregation, in detailPacket, DetailSectionKind.OutgoingDamage, combatantId, _outgoingDamageSelectionIds);
            AccumulateSection(_outgoingHealingSectionAggregation, in detailPacket, DetailSectionKind.OutgoingHealing, combatantId, _outgoingSupportSelectionIds);
            AccumulateSection(_outgoingShieldSectionAggregation, in detailPacket, DetailSectionKind.OutgoingShield, combatantId, _outgoingSupportSelectionIds);
            AccumulateSection(_incomingDamageSectionAggregation, in detailPacket, DetailSectionKind.IncomingDamage, combatantId, _incomingDamageSelectionIds);
            AccumulateSection(_incomingHealingSectionAggregation, in detailPacket, DetailSectionKind.IncomingHealing, combatantId, _incomingSupportSelectionIds);
            AccumulateSection(_incomingShieldSectionAggregation, in detailPacket, DetailSectionKind.IncomingShield, combatantId, _incomingSupportSelectionIds);
        }

        ApplyAggregatedSection(OutgoingDetail.DamageSection, DetailSectionKind.OutgoingDamage, _outgoingDamageSectionAggregation);
        ApplyAggregatedSection(OutgoingDetail.HealingSection, DetailSectionKind.OutgoingHealing, _outgoingHealingSectionAggregation);
        ApplyAggregatedSection(OutgoingDetail.ShieldSection, DetailSectionKind.OutgoingShield, _outgoingShieldSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.DamageSection, DetailSectionKind.IncomingDamage, _incomingDamageSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.HealingSection, DetailSectionKind.IncomingHealing, _incomingHealingSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.ShieldSection, DetailSectionKind.IncomingShield, _incomingShieldSectionAggregation);
    }

    private void RefreshDirection(
        CombatDirectionDetailViewModel directionDetail,
        DetailSectionKind damageSectionKind,
        DetailSectionKind healingSectionKind,
        DetailSectionKind shieldSectionKind)
    {
        var isOutgoing = ReferenceEquals(directionDetail, OutgoingDetail);
        var damageAggregation = isOutgoing ? _outgoingDamageSectionAggregation : _incomingDamageSectionAggregation;
        var healingAggregation = isOutgoing ? _outgoingHealingSectionAggregation : _incomingHealingSectionAggregation;
        var shieldAggregation = isOutgoing ? _outgoingShieldSectionAggregation : _incomingShieldSectionAggregation;
        var selectedDamageCounterpartIds = isOutgoing ? _outgoingDamageSelectionIds : _incomingDamageSelectionIds;
        var selectedSupportCounterpartIds = isOutgoing ? _outgoingSupportSelectionIds : _incomingSupportSelectionIds;
        CopyDirectionSelections(directionDetail, selectedDamageCounterpartIds, selectedSupportCounterpartIds, out var selectableDamageCounterpartCount, out var selectableSupportCounterpartCount);

        if (_combatantId is null)
        {
            directionDetail.DamageSection.Clear();
            directionDetail.HealingSection.Clear();
            directionDetail.ShieldSection.Clear();
            return;
        }

        ResetDirectionAggregations(
            damageAggregation,
            healingAggregation,
            shieldAggregation,
            selectedDamageCounterpartIds,
            selectedSupportCounterpartIds,
            selectableDamageCounterpartCount,
            selectableSupportCounterpartCount);

        var packetsSpan = CollectionsMarshal.AsSpan(_detailEvents);
        var combatantId = _combatantId.Value;
        foreach (ref readonly var detailPacket in packetsSpan)
        {
            AccumulateSection(damageAggregation, in detailPacket, damageSectionKind, combatantId, selectedDamageCounterpartIds);
            AccumulateSection(healingAggregation, in detailPacket, healingSectionKind, combatantId, selectedSupportCounterpartIds);
            AccumulateSection(shieldAggregation, in detailPacket, shieldSectionKind, combatantId, selectedSupportCounterpartIds);
        }

        ApplyAggregatedSection(directionDetail.DamageSection, damageSectionKind, damageAggregation);
        ApplyAggregatedSection(directionDetail.HealingSection, healingSectionKind, healingAggregation);
        ApplyAggregatedSection(directionDetail.ShieldSection, shieldSectionKind, shieldAggregation);
    }

    private static void CopyDirectionSelections(
        CombatDirectionDetailViewModel directionDetail,
        HashSet<int> selectedDamageCounterpartIds,
        HashSet<int> selectedSupportCounterpartIds,
        out int selectableDamageCounterpartCount,
        out int selectableSupportCounterpartCount)
    {
        directionDetail.DamageCounterpartFilter.CopySelectedCounterpartIds(selectedDamageCounterpartIds);
        selectableDamageCounterpartCount = directionDetail.DamageCounterpartFilter.Counterparts.Count;
        directionDetail.SupportCounterpartFilter.CopySelectedCounterpartIds(selectedSupportCounterpartIds);
        selectableSupportCounterpartCount = directionDetail.SupportCounterpartFilter.Counterparts.Count;
    }

    private static void ResetDirectionAggregations(
        SkillDetailSectionAggregation damageAggregation,
        SkillDetailSectionAggregation healingAggregation,
        SkillDetailSectionAggregation shieldAggregation,
        HashSet<int> selectedDamageCounterpartIds,
        HashSet<int> selectedSupportCounterpartIds,
        int selectableDamageCounterpartCount,
        int selectableSupportCounterpartCount)
    {
        damageAggregation.Reset(selectableDamageCounterpartCount > 0 && selectedDamageCounterpartIds.Count != selectableDamageCounterpartCount);
        healingAggregation.Reset(selectableSupportCounterpartCount > 0 && selectedSupportCounterpartIds.Count != selectableSupportCounterpartCount);
        shieldAggregation.Reset(healingAggregation.HasSubsetFilter);
    }

    private static void AccumulateSection(
        SkillDetailSectionAggregation aggregation,
        in CombatDetailEvent detailPacket,
        DetailSectionKind sectionKind,
        int combatantId,
        HashSet<int> selectedCounterpartIds)
    {
        if (!SkillDetailSectionRules.Matches(in detailPacket, sectionKind, combatantId))
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailPacket, sectionKind);
        if (counterpartCombatantId > 0)
        {
            if (!selectedCounterpartIds.Contains(counterpartCombatantId))
                return;
        }
        else if (aggregation.HasSubsetFilter)
        {
            return;
        }

        if (!SkillDetailSectionRules.Contributes(in detailPacket, sectionKind))
            return;

        var observedAt = detailPacket.ObservedAt;
        if (observedAt > 0)
        {
            aggregation.FirstObserved = Math.Min(aggregation.FirstObserved, observedAt);
            aggregation.LastObserved = Math.Max(aggregation.LastObserved, observedAt);
        }

        var eventKey = detailPacket.EventKey;
        ref var skillMetrics = ref CollectionsMarshal.GetValueRefOrAddDefault(aggregation.SkillMetrics, eventKey, out var exists);
        if (!exists)
        {
            var observation = detailPacket.Observation;
            skillMetrics = new SkillMetrics(eventKey, in observation);
        }

        var skillObservation = detailPacket.Observation;
        var contribution = detailPacket.Contribution;
        skillMetrics.ProcessContribution(in skillObservation, in contribution);
        ref var eventCount = ref CollectionsMarshal.GetValueRefOrAddDefault(aggregation.EventCounts, eventKey, out _);
        eventCount++;
    }

    private void ApplyAggregatedSection(SkillDetailSectionViewModel section, DetailSectionKind sectionKind, SkillDetailSectionAggregation aggregation)
    {
        var metrics = aggregation.SkillMetrics;

        _sectionRows.Clear();
        _sectionRowIndexes.Clear();
        if (sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage)
            SkillDetailRowBuilder.BuildDamageRows(metrics, aggregation.EventCounts, DisplayContext, _localization, _sectionRows, _sectionRowIndexes);
        else if (sectionKind is DetailSectionKind.OutgoingShield or DetailSectionKind.IncomingShield)
            SkillDetailRowBuilder.BuildShieldRows(metrics, aggregation.EventCounts, DisplayContext, _localization, _sectionRows, _sectionRowIndexes);
        else
            SkillDetailRowBuilder.BuildHealingRows(metrics, aggregation.EventCounts, DisplayContext, _localization, _sectionRows, _sectionRowIndexes);

        var durationSeconds = aggregation.HasSubsetFilter
            ? ResolveObservedDurationSeconds(aggregation.FirstObserved, aggregation.LastObserved)
            : ResolveSceneDurationSeconds();
        SkillDetailSectionSummaryApplier.Apply(section, metrics, _sectionRows, sectionKind, durationSeconds, !aggregation.HasSubsetFilter);
    }

    private double ResolveSceneDurationSeconds()
        => _currentSnapshot.EncounterTime > 0 ? Math.Max(1d, _currentSnapshot.EncounterTime / 1000d) : 0d;

    private static double ResolveObservedDurationSeconds(long firstObserved, long lastObserved)
        => firstObserved != long.MaxValue && lastObserved != long.MinValue ? Math.Max(1d, Math.Max(0, lastObserved - firstObserved) / 1000d) : 0d;

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
}
