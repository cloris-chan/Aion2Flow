using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class CombatantDetailsFlyoutViewModel : ObservableObject, ICombatDetailEventWriter, IDisposable
{
    private readonly List<CombatMetricDetailEvent> _metricDetailEvents = [];
    private readonly List<CombatMechanicDetailEvent> _mechanicDetailEvents = [];
    private readonly List<CombatResourceDetailEvent> _resourceDetailEvents = [];
    private readonly DetailCounterpartOptionBuilder _counterpartOptionBuilder = new();
    private readonly HashSet<int> _outgoingDamageSelectionIds = [];
    private readonly HashSet<int> _outgoingHealingSelectionIds = [];
    private readonly HashSet<int> _outgoingShieldSelectionIds = [];
    private readonly HashSet<int> _outgoingResourceSelectionIds = [];
    private readonly HashSet<int> _incomingDamageSelectionIds = [];
    private readonly HashSet<int> _incomingHealingSelectionIds = [];
    private readonly HashSet<int> _incomingShieldSelectionIds = [];
    private readonly HashSet<int> _incomingResourceSelectionIds = [];
    private readonly SkillDetailSectionAggregation _outgoingDamageSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _outgoingHealingSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _outgoingShieldSectionAggregation = new();
    private readonly ResourceDetailSectionAggregation _outgoingResourceSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingDamageSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingHealingSectionAggregation = new();
    private readonly SkillDetailSectionAggregation _incomingShieldSectionAggregation = new();
    private readonly ResourceDetailSectionAggregation _incomingResourceSectionAggregation = new();
    private readonly List<SkillDetailRowData> _sectionRows = [];
    private readonly Dictionary<SkillBaseKey, int> _sectionRowIndexes = [];
    private readonly List<ResourceDetailRowData> _resourceSectionRows = [];
    private readonly Dictionary<SkillBaseKey, int> _resourceSectionRowIndexes = [];
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
        OutgoingDetail.HealingCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        OutgoingDetail.ShieldCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        OutgoingDetail.ResourceCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.DamageCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.HealingCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.ShieldCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
        IncomingDetail.ResourceCounterpartFilter.SelectionChanged += HandleCounterpartSelectionChanged;
    }

    public CombatDirectionDetailViewModel OutgoingDetail { get; }

    public CombatDirectionDetailViewModel IncomingDetail { get; }

    public SkillDetailSectionViewModel OutgoingDamage => OutgoingDetail.DamageSection;

    public SkillDetailSectionViewModel OutgoingHealing => OutgoingDetail.HealingSection;

    public SkillDetailSectionViewModel OutgoingShield => OutgoingDetail.ShieldSection;

    public SkillDetailSectionViewModel IncomingDamage => IncomingDetail.DamageSection;

    public SkillDetailSectionViewModel IncomingHealing => IncomingDetail.HealingSection;

    public SkillDetailSectionViewModel IncomingShield => IncomingDetail.ShieldSection;

    public ResourceDetailSectionViewModel OutgoingResource => OutgoingDetail.ResourceSection;

    public ResourceDetailSectionViewModel IncomingResource => IncomingDetail.ResourceSection;

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
        DisposeCounterpartFilter(OutgoingDetail.HealingCounterpartFilter);
        DisposeCounterpartFilter(OutgoingDetail.ShieldCounterpartFilter);
        DisposeCounterpartFilter(OutgoingDetail.ResourceCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.DamageCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.HealingCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.ShieldCounterpartFilter);
        DisposeCounterpartFilter(IncomingDetail.ResourceCounterpartFilter);
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

    public void SelectPlaybackSceneEncounterCombatant(Guid encounterContextId, int combatantId, SceneCombatSnapshot snapshot, CombatDetailUpdateResult update, in CombatDetailEventSet events)
    {
        if (update.IsFullSnapshot)
            ClearDetailEvents();

        _metricDetailEvents.AddRange(events.MetricEvents);
        _mechanicDetailEvents.AddRange(events.MechanicEvents);
        _resourceDetailEvents.AddRange(events.ResourceEvents);

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
        ClearDetailEvents();
        _detailRevision = -1;
        SelectedCombatantId = 0;
        SelectedDirectionIndex = 0;
        OutgoingDetail.Clear();
        IncomingDetail.Clear();
    }

    void ICombatDetailEventWriter.Clear() => ClearDetailEvents();

    void ICombatDetailEventWriter.AddMetric(in CombatMetricDetailEvent detailEvent) => _metricDetailEvents.Add(detailEvent);

    void ICombatDetailEventWriter.AddMechanic(in CombatMechanicDetailEvent detailEvent) => _mechanicDetailEvents.Add(detailEvent);

    void ICombatDetailEventWriter.AddResource(in CombatResourceDetailEvent detailEvent) => _resourceDetailEvents.Add(detailEvent);

    private bool HasDetailEvents =>
        _metricDetailEvents.Count > 0 ||
        _mechanicDetailEvents.Count > 0 ||
        _resourceDetailEvents.Count > 0;

    private void ClearDetailEvents()
    {
        _metricDetailEvents.Clear();
        _mechanicDetailEvents.Clear();
        _resourceDetailEvents.Clear();
    }

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
            ReferenceEquals(sender, OutgoingDetail.HealingCounterpartFilter) ||
            ReferenceEquals(sender, OutgoingDetail.ShieldCounterpartFilter) ||
            ReferenceEquals(sender, OutgoingDetail.ResourceCounterpartFilter))
        {
            RefreshDirection(
                OutgoingDetail,
                DetailSectionKind.OutgoingDamage,
                DetailSectionKind.OutgoingHealing,
                DetailSectionKind.OutgoingShield,
                DetailSectionKind.OutgoingResource);
        }
        else if (ReferenceEquals(sender, IncomingDetail.DamageCounterpartFilter) ||
                 ReferenceEquals(sender, IncomingDetail.HealingCounterpartFilter) ||
                 ReferenceEquals(sender, IncomingDetail.ShieldCounterpartFilter) ||
                 ReferenceEquals(sender, IncomingDetail.ResourceCounterpartFilter))
        {
            RefreshDirection(
                IncomingDetail,
                DetailSectionKind.IncomingDamage,
                DetailSectionKind.IncomingHealing,
                DetailSectionKind.IncomingShield,
                DetailSectionKind.IncomingResource);
        }
    }

    private void RefreshSceneContext(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailDelta detail, bool forceRefresh)
    {
        if (combatantId is null ||
            encounterContextId == Guid.Empty ||
            !snapshot.Combatants.ContainsKey(combatantId.Value) && detail.Combatant is null &&
            detail.MetricEvents.Count == 0 && detail.MechanicEvents.Count == 0 && detail.ResourceEvents.Count == 0)
        {
            _encounterContextId = encounterContextId;
            _combatantId = combatantId;
            _currentSnapshot = new SceneCombatSnapshot();
            ClearDetailEvents();
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
        ClearDetailEvents();
        _metricDetailEvents.AddRange(detail.MetricEvents);
        _mechanicDetailEvents.AddRange(detail.MechanicEvents);
        _resourceDetailEvents.AddRange(detail.ResourceEvents);

        RebuildCounterpartSelections();
        RefreshAllSections();
    }

    private void RefreshLiveSceneContext(Guid encounterContextId, int? combatantId, SceneCombatSnapshot snapshot, CombatDetailUpdateResult update, bool forceRefresh)
    {
        if (combatantId is null ||
            encounterContextId == Guid.Empty ||
            !snapshot.Combatants.ContainsKey(combatantId.Value) && update.Combatant is null && !HasDetailEvents)
        {
            _encounterContextId = encounterContextId;
            _combatantId = combatantId;
            _currentSnapshot = new SceneCombatSnapshot();
            ClearDetailEvents();
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
        if (update.IsFullSnapshot ||
            update.AddedMetricEventCount > 0 ||
            update.AddedMechanicEventCount > 0 ||
            update.AddedResourceEventCount > 0 ||
            forceRefresh)
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

        _counterpartOptionBuilder.Accumulate(
            CollectionsMarshal.AsSpan(_metricDetailEvents),
            CollectionsMarshal.AsSpan(_mechanicDetailEvents),
            CollectionsMarshal.AsSpan(_resourceDetailEvents),
            _combatantId.Value);
        OutgoingDetail.DamageCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingDamageOptions(DisplayContext));
        OutgoingDetail.HealingCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingHealingOptions(DisplayContext));
        OutgoingDetail.ShieldCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingShieldOptions(DisplayContext));
        OutgoingDetail.ResourceCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildOutgoingResourceOptions(DisplayContext));
        IncomingDetail.DamageCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingDamageOptions(DisplayContext));
        IncomingDetail.HealingCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingHealingOptions(DisplayContext));
        IncomingDetail.ShieldCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingShieldOptions(DisplayContext));
        IncomingDetail.ResourceCounterpartFilter.ReplaceCounterparts(_counterpartOptionBuilder.BuildIncomingResourceOptions(DisplayContext));
    }

    private void RefreshAllSections()
    {
        if (_combatantId is null)
        {
            ClearSectionsOnly();
            return;
        }

        CopyDirectionSelections(
            OutgoingDetail,
            _outgoingDamageSelectionIds,
            _outgoingHealingSelectionIds,
            _outgoingShieldSelectionIds,
            _outgoingResourceSelectionIds,
            out var outgoingDamageCounterpartCount,
            out var outgoingHealingCounterpartCount,
            out var outgoingShieldCounterpartCount,
            out var outgoingResourceCounterpartCount);
        CopyDirectionSelections(
            IncomingDetail,
            _incomingDamageSelectionIds,
            _incomingHealingSelectionIds,
            _incomingShieldSelectionIds,
            _incomingResourceSelectionIds,
            out var incomingDamageCounterpartCount,
            out var incomingHealingCounterpartCount,
            out var incomingShieldCounterpartCount,
            out var incomingResourceCounterpartCount);

        ResetDirectionAggregations(
            _outgoingDamageSectionAggregation,
            _outgoingHealingSectionAggregation,
            _outgoingShieldSectionAggregation,
            _outgoingResourceSectionAggregation,
            _outgoingDamageSelectionIds,
            _outgoingHealingSelectionIds,
            _outgoingShieldSelectionIds,
            _outgoingResourceSelectionIds,
            outgoingDamageCounterpartCount,
            outgoingHealingCounterpartCount,
            outgoingShieldCounterpartCount,
            outgoingResourceCounterpartCount);
        ResetDirectionAggregations(
            _incomingDamageSectionAggregation,
            _incomingHealingSectionAggregation,
            _incomingShieldSectionAggregation,
            _incomingResourceSectionAggregation,
            _incomingDamageSelectionIds,
            _incomingHealingSelectionIds,
            _incomingShieldSelectionIds,
            _incomingResourceSelectionIds,
            incomingDamageCounterpartCount,
            incomingHealingCounterpartCount,
            incomingShieldCounterpartCount,
            incomingResourceCounterpartCount);

        var metricEvents = CollectionsMarshal.AsSpan(_metricDetailEvents);
        var combatantId = _combatantId.Value;
        foreach (ref readonly var detailEvent in metricEvents)
        {
            AccumulateMetricSection(_outgoingDamageSectionAggregation, in detailEvent, DetailSectionKind.OutgoingDamage, combatantId, _outgoingDamageSelectionIds);
            AccumulateMetricSection(_outgoingHealingSectionAggregation, in detailEvent, DetailSectionKind.OutgoingHealing, combatantId, _outgoingHealingSelectionIds);
            AccumulateMetricSection(_outgoingShieldSectionAggregation, in detailEvent, DetailSectionKind.OutgoingShield, combatantId, _outgoingShieldSelectionIds);
            AccumulateMetricSection(_incomingDamageSectionAggregation, in detailEvent, DetailSectionKind.IncomingDamage, combatantId, _incomingDamageSelectionIds);
            AccumulateMetricSection(_incomingHealingSectionAggregation, in detailEvent, DetailSectionKind.IncomingHealing, combatantId, _incomingHealingSelectionIds);
            AccumulateMetricSection(_incomingShieldSectionAggregation, in detailEvent, DetailSectionKind.IncomingShield, combatantId, _incomingShieldSelectionIds);
        }

        var mechanicEvents = CollectionsMarshal.AsSpan(_mechanicDetailEvents);
        foreach (ref readonly var detailEvent in mechanicEvents)
        {
            AccumulateMechanicSection(_outgoingDamageSectionAggregation, in detailEvent, DetailSectionKind.OutgoingDamage, combatantId, _outgoingDamageSelectionIds);
            AccumulateMechanicSection(_incomingDamageSectionAggregation, in detailEvent, DetailSectionKind.IncomingDamage, combatantId, _incomingDamageSelectionIds);
        }

        var resourceEvents = CollectionsMarshal.AsSpan(_resourceDetailEvents);
        foreach (ref readonly var detailEvent in resourceEvents)
        {
            AccumulateResourceSection(_outgoingResourceSectionAggregation, in detailEvent, DetailSectionKind.OutgoingResource, combatantId, _outgoingResourceSelectionIds);
            AccumulateResourceSection(_incomingResourceSectionAggregation, in detailEvent, DetailSectionKind.IncomingResource, combatantId, _incomingResourceSelectionIds);
        }

        ApplyAggregatedSection(OutgoingDetail.DamageSection, DetailSectionKind.OutgoingDamage, _outgoingDamageSectionAggregation);
        ApplyAggregatedSection(OutgoingDetail.HealingSection, DetailSectionKind.OutgoingHealing, _outgoingHealingSectionAggregation);
        ApplyAggregatedSection(OutgoingDetail.ShieldSection, DetailSectionKind.OutgoingShield, _outgoingShieldSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.DamageSection, DetailSectionKind.IncomingDamage, _incomingDamageSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.HealingSection, DetailSectionKind.IncomingHealing, _incomingHealingSectionAggregation);
        ApplyAggregatedSection(IncomingDetail.ShieldSection, DetailSectionKind.IncomingShield, _incomingShieldSectionAggregation);
        ApplyResourceSection(OutgoingDetail.ResourceSection, _outgoingResourceSectionAggregation);
        ApplyResourceSection(IncomingDetail.ResourceSection, _incomingResourceSectionAggregation);
    }

    private void RefreshDirection(
        CombatDirectionDetailViewModel directionDetail,
        DetailSectionKind damageSectionKind,
        DetailSectionKind healingSectionKind,
        DetailSectionKind shieldSectionKind,
        DetailSectionKind resourceSectionKind)
    {
        var isOutgoing = ReferenceEquals(directionDetail, OutgoingDetail);
        var damageAggregation = isOutgoing ? _outgoingDamageSectionAggregation : _incomingDamageSectionAggregation;
        var healingAggregation = isOutgoing ? _outgoingHealingSectionAggregation : _incomingHealingSectionAggregation;
        var shieldAggregation = isOutgoing ? _outgoingShieldSectionAggregation : _incomingShieldSectionAggregation;
        var resourceAggregation = isOutgoing ? _outgoingResourceSectionAggregation : _incomingResourceSectionAggregation;
        var selectedDamageCounterpartIds = isOutgoing ? _outgoingDamageSelectionIds : _incomingDamageSelectionIds;
        var selectedHealingCounterpartIds = isOutgoing ? _outgoingHealingSelectionIds : _incomingHealingSelectionIds;
        var selectedShieldCounterpartIds = isOutgoing ? _outgoingShieldSelectionIds : _incomingShieldSelectionIds;
        var selectedResourceCounterpartIds = isOutgoing ? _outgoingResourceSelectionIds : _incomingResourceSelectionIds;
        CopyDirectionSelections(
            directionDetail,
            selectedDamageCounterpartIds,
            selectedHealingCounterpartIds,
            selectedShieldCounterpartIds,
            selectedResourceCounterpartIds,
            out var selectableDamageCounterpartCount,
            out var selectableHealingCounterpartCount,
            out var selectableShieldCounterpartCount,
            out var selectableResourceCounterpartCount);

        if (_combatantId is null)
        {
            directionDetail.DamageSection.Clear();
            directionDetail.HealingSection.Clear();
            directionDetail.ShieldSection.Clear();
            directionDetail.ResourceSection.Clear();
            return;
        }

        ResetDirectionAggregations(
            damageAggregation,
            healingAggregation,
            shieldAggregation,
            resourceAggregation,
            selectedDamageCounterpartIds,
            selectedHealingCounterpartIds,
            selectedShieldCounterpartIds,
            selectedResourceCounterpartIds,
            selectableDamageCounterpartCount,
            selectableHealingCounterpartCount,
            selectableShieldCounterpartCount,
            selectableResourceCounterpartCount);

        var metricEvents = CollectionsMarshal.AsSpan(_metricDetailEvents);
        var combatantId = _combatantId.Value;
        foreach (ref readonly var detailEvent in metricEvents)
        {
            AccumulateMetricSection(damageAggregation, in detailEvent, damageSectionKind, combatantId, selectedDamageCounterpartIds);
            AccumulateMetricSection(healingAggregation, in detailEvent, healingSectionKind, combatantId, selectedHealingCounterpartIds);
            AccumulateMetricSection(shieldAggregation, in detailEvent, shieldSectionKind, combatantId, selectedShieldCounterpartIds);
        }

        var mechanicEvents = CollectionsMarshal.AsSpan(_mechanicDetailEvents);
        foreach (ref readonly var detailEvent in mechanicEvents)
        {
            AccumulateMechanicSection(damageAggregation, in detailEvent, damageSectionKind, combatantId, selectedDamageCounterpartIds);
        }

        var resourceEvents = CollectionsMarshal.AsSpan(_resourceDetailEvents);
        foreach (ref readonly var detailEvent in resourceEvents)
            AccumulateResourceSection(resourceAggregation, in detailEvent, resourceSectionKind, combatantId, selectedResourceCounterpartIds);

        ApplyAggregatedSection(directionDetail.DamageSection, damageSectionKind, damageAggregation);
        ApplyAggregatedSection(directionDetail.HealingSection, healingSectionKind, healingAggregation);
        ApplyAggregatedSection(directionDetail.ShieldSection, shieldSectionKind, shieldAggregation);
        ApplyResourceSection(directionDetail.ResourceSection, resourceAggregation);
    }

    private static void CopyDirectionSelections(
        CombatDirectionDetailViewModel directionDetail,
        HashSet<int> selectedDamageCounterpartIds,
        HashSet<int> selectedHealingCounterpartIds,
        HashSet<int> selectedShieldCounterpartIds,
        HashSet<int> selectedResourceCounterpartIds,
        out int selectableDamageCounterpartCount,
        out int selectableHealingCounterpartCount,
        out int selectableShieldCounterpartCount,
        out int selectableResourceCounterpartCount)
    {
        directionDetail.DamageCounterpartFilter.CopySelectedCounterpartIds(selectedDamageCounterpartIds);
        selectableDamageCounterpartCount = directionDetail.DamageCounterpartFilter.Counterparts.Count;
        directionDetail.HealingCounterpartFilter.CopySelectedCounterpartIds(selectedHealingCounterpartIds);
        selectableHealingCounterpartCount = directionDetail.HealingCounterpartFilter.Counterparts.Count;
        directionDetail.ShieldCounterpartFilter.CopySelectedCounterpartIds(selectedShieldCounterpartIds);
        selectableShieldCounterpartCount = directionDetail.ShieldCounterpartFilter.Counterparts.Count;
        directionDetail.ResourceCounterpartFilter.CopySelectedCounterpartIds(selectedResourceCounterpartIds);
        selectableResourceCounterpartCount = directionDetail.ResourceCounterpartFilter.Counterparts.Count;
    }

    private static void ResetDirectionAggregations(
        SkillDetailSectionAggregation damageAggregation,
        SkillDetailSectionAggregation healingAggregation,
        SkillDetailSectionAggregation shieldAggregation,
        ResourceDetailSectionAggregation resourceAggregation,
        HashSet<int> selectedDamageCounterpartIds,
        HashSet<int> selectedHealingCounterpartIds,
        HashSet<int> selectedShieldCounterpartIds,
        HashSet<int> selectedResourceCounterpartIds,
        int selectableDamageCounterpartCount,
        int selectableHealingCounterpartCount,
        int selectableShieldCounterpartCount,
        int selectableResourceCounterpartCount)
    {
        damageAggregation.Reset(selectableDamageCounterpartCount > 0 && selectedDamageCounterpartIds.Count != selectableDamageCounterpartCount);
        healingAggregation.Reset(selectableHealingCounterpartCount > 0 && selectedHealingCounterpartIds.Count != selectableHealingCounterpartCount);
        shieldAggregation.Reset(selectableShieldCounterpartCount > 0 && selectedShieldCounterpartIds.Count != selectableShieldCounterpartCount);
        resourceAggregation.Reset(selectableResourceCounterpartCount > 0 && selectedResourceCounterpartIds.Count != selectableResourceCounterpartCount);
    }

    private static void AccumulateMetricSection(
        SkillDetailSectionAggregation aggregation,
        in CombatMetricDetailEvent detailEvent,
        DetailSectionKind sectionKind,
        int combatantId,
        HashSet<int> selectedCounterpartIds)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId))
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId > 0)
        {
            if (!selectedCounterpartIds.Contains(counterpartCombatantId))
                return;
        }
        else if (aggregation.HasSubsetFilter)
        {
            return;
        }

        if (!SkillDetailSectionRules.Contributes(in detailEvent, sectionKind))
            return;

        var observedAt = detailEvent.ObservedAt;
        if (observedAt > 0)
        {
            aggregation.FirstObserved = Math.Min(aggregation.FirstObserved, observedAt);
            aggregation.LastObserved = Math.Max(aggregation.LastObserved, observedAt);
        }

        var eventKey = detailEvent.EventKey;
        ref var skillMetrics = ref CollectionsMarshal.GetValueRefOrAddDefault(aggregation.SkillMetrics, eventKey, out var exists);
        if (!exists)
        {
            skillMetrics = new SkillMetrics(eventKey);
        }

        var contribution = detailEvent.Contribution;
        skillMetrics.ProcessContribution(in contribution);
        aggregation.CountOccurrence(detailEvent.Fact);
    }

    private static void AccumulateMechanicSection(
        SkillDetailSectionAggregation aggregation,
        in CombatMechanicDetailEvent detailEvent,
        DetailSectionKind sectionKind,
        int combatantId,
        HashSet<int> selectedCounterpartIds)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId))
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId > 0)
        {
            if (!selectedCounterpartIds.Contains(counterpartCombatantId))
                return;
        }
        else if (aggregation.HasSubsetFilter)
        {
            return;
        }

        var observedAt = detailEvent.ObservedAt;
        if (observedAt > 0)
        {
            aggregation.FirstObserved = Math.Min(aggregation.FirstObserved, observedAt);
            aggregation.LastObserved = Math.Max(aggregation.LastObserved, observedAt);
        }

        var eventKey = detailEvent.EventKey;
        ref var skillMetrics = ref CollectionsMarshal.GetValueRefOrAddDefault(aggregation.SkillMetrics, eventKey, out var exists);
        if (!exists)
            skillMetrics = new SkillMetrics(eventKey);

        var mechanic = detailEvent.Mechanic;
        skillMetrics.ProcessMechanic(in mechanic);
        aggregation.CountOccurrence(detailEvent.Fact);
    }

    private static void AccumulateResourceSection(
        ResourceDetailSectionAggregation aggregation,
        in CombatResourceDetailEvent detailEvent,
        DetailSectionKind sectionKind,
        int combatantId,
        HashSet<int> selectedCounterpartIds)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId))
            return;

        if (detailEvent.Resource.Resource != CombatResourceKind.Mana)
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId > 0)
        {
            if (!selectedCounterpartIds.Contains(counterpartCombatantId))
                return;
        }
        else if (aggregation.HasSubsetFilter)
        {
            return;
        }

        var eventKey = detailEvent.EventKey;
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(aggregation.Skills, eventKey, out _);

        var resource = detailEvent.Resource;
        metrics.Process(in resource);
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

    private void ApplyResourceSection(ResourceDetailSectionViewModel section, ResourceDetailSectionAggregation aggregation)
    {
        ResourceDetailRowBuilder.Build(
            aggregation.Skills,
            DisplayContext,
            _localization,
            _resourceSectionRows,
            _resourceSectionRowIndexes);
        ResourceDetailRowBuilder.ApplySummary(section, _resourceSectionRows);
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
        OutgoingResource.Clear();
        IncomingResource.Clear();
    }
}
