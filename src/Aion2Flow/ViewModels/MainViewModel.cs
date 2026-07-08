using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class MainViewModel : FrameBatchedObservableObject, IAsyncDisposable
{
    private static readonly BossDamageContribution[] EmptyBossDamageContributions = [];

    private const string IndicatorIdleColor = "#6F7A8A";
    private const string IndicatorOkColor = "#6FD38A";
    private const string IndicatorWarnColor = "#F3C969";
    private const string IndicatorErrorColor = "#F07C82";
    private const string IndicatorInfoColor = "#8DD6FF";
    private const double BarColorAlpha = 0x70 / 255d;
    private const double BarColorMinSaturation = 0.52d;
    private const double BarColorSaturationRange = 0.16d;
    private const double BarColorMinLightness = 0.52d;
    private const double BarColorLightnessRange = 0.12d;
    private const int BarHueGap = 40;
    private const int BarHueRingSize = 360 / BarHueGap;

    private readonly WinDivertCaptureService _captureService;
    private readonly ProcessPortDiscoveryService _processPortDiscoveryService;
    private readonly LanguageService _languageService;
    private readonly GameResourceService _gameResourceService;
    private readonly EncounterArchiveService _encounterArchiveService;
    private readonly CombatantDetailsFlyoutViewModel _combatantDetails;
    private readonly UiFrameBatchService _frameBatchService;

    private SceneCombatSnapshot _latestLiveSnapshot = new();
    private SceneCombatSnapshot _displayedSnapshot = new();
    private SceneReadModelFrame _latestLiveFrame = new();
    private SceneCombatSnapshot? _displayContextSnapshot;
    private ArchivedEncounterRecord? _displayContextArchivedRecord;
    private readonly Dictionary<int, IBrush> _combatantBarBrushes = [];
    private readonly Dictionary<long, IBrush> _bossHpBarBrushes = [];
    private readonly List<ProgressSegment> _bossSegmentScratch = [];
    private readonly List<BossFocusDisplayGroup> _bossFocusDisplayGroups = [];
    private int _displayContextVersion;
    private int _displayContextBuiltVersion = -1;
    private long _displayContextMetadataRevision = -1;
    private bool _displayContextIsArchived;
    private Guid _barBrushEncounterId;
    private uint _barBrushSeed;
    private int _barBrushBaseHue;
    private int _nextBarHueIndex;
    private volatile bool _suppressRefresh;
    private bool _isDisposed;

    public LocalizationService Localization { get; }
    public CombatantDetailsFlyoutViewModel CombatantDetails => _combatantDetails;
    public SettingsFlyoutViewModel SettingsFlyout { get; }
    public CombatantColumnLayoutViewModel CombatantColumns { get; }
    public ObservableCollection<BossFocusViewModel> BossFocuses { get; } = [];
    public KeyedObservableCollection<int, CombatantRowViewModel> Combatants { get; } = new(x => x.Id);
    public ObservableCollection<EncounterHistoryItemViewModel> EncounterHistory { get; } = [];

    public SceneDisplayContext? DisplayContext
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public int RoundTripTimeMilliseconds
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double EncounterTimeSeconds
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public Guid NumericStableWidthScopeKey
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public uint LiveSceneMapId
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public string SceneName
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string DriverIndicatorColor
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = IndicatorIdleColor;

    public string GamePortIndicatorColor
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = IndicatorIdleColor;

    public string CaptureLockIndicatorColor
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = IndicatorIdleColor;

    public string LatencyIndicatorColor
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = IndicatorIdleColor;

    public string DriverIndicatorToolTip
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string GamePortIndicatorToolTip
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string CaptureLockIndicatorToolTip
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string LatencyToolTip
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    public partial CombatantRowViewModel? SelectedCombatant { get; set; }

    [ObservableProperty]
    public partial EncounterHistoryItemViewModel? SelectedEncounterHistory { get; set; }

    [ObservableProperty]
    public partial bool IsViewingArchivedEncounter { get; set; }

    [ObservableProperty]
    public partial bool HasArchivedEncounters { get; set; }

    public MainViewModel(
        WinDivertCaptureService captureService,
        ProcessPortDiscoveryService processPortDiscoveryService,
        LanguageService languageService,
        GameResourceService gameResourceService,
        EncounterArchiveService encounterArchiveService,
        CombatantDetailsFlyoutViewModel combatantDetails,
        LocalizationService localization,
        SettingsFlyoutViewModel settingsFlyout,
        UiFrameBatchService frameBatchService)
        : base(frameBatchService)
    {
        _captureService = captureService;
        _processPortDiscoveryService = processPortDiscoveryService;
        _languageService = languageService;
        _gameResourceService = gameResourceService;
        _encounterArchiveService = encounterArchiveService;
        _combatantDetails = combatantDetails;
        _frameBatchService = frameBatchService;
        Localization = localization;
        SettingsFlyout = settingsFlyout;
        if (_captureService.Scene.Kind != SettingsFlyout.SceneKind)
            _captureService.Scene.ChangeKind(SettingsFlyout.SceneKind, _captureService.Scene.SessionStarted, archiveCurrent: false);
        CombatantColumns = new CombatantColumnLayoutViewModel(frameBatchService)
        {
            UseCompactMainMetrics = SettingsFlyout.UseCompactMainMetrics
        };
        DisplayContext = CreateLiveDisplayContext(_displayedSnapshot);
        _combatantDetails.DisplayContext = DisplayContext;

        _captureService.StatusChanged += OnCaptureStatusChanged;
        _captureService.RttResolved += OnRttResolved;
        _languageService.LanguageChanged += OnLanguageChanged;
        _gameResourceService.ResourcesChanged += OnResourcesChanged;
        _encounterArchiveService.HistoryChanged += OnEncounterHistoryChanged;
        SettingsFlyout.PropertyChanged += OnSettingsFlyoutPropertyChanged;

        RebuildEncounterHistory();
        ApplyLocalizedUiText();
        RefreshCaptureIndicators();
    }

    public Task InitializeAsync() => StartCaptureAsync();

    private void OnRttResolved(double rtt)
    {
        Dispatcher.UIThread.Post(RefreshCaptureIndicators);
    }

    private void OnCaptureStatusChanged(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Status = message;
            RefreshCaptureIndicators();
        });
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _displayContextVersion++;
            RebuildEncounterHistory();
            ApplyLocalizedUiText();
            RefreshCaptureIndicators();
            RefreshDisplayedSnapshot(forceDetailRefresh: true);
        });
    }

    private void OnResourcesChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _displayContextVersion++;
            RebuildEncounterHistory();
            RefreshDisplayedSnapshot(forceDetailRefresh: true);
        });
    }

    private void OnEncounterHistoryChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RebuildEncounterHistory);
    }

    private void OnSettingsFlyoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsFlyoutViewModel.CombatantSortMetric))
            Dispatcher.UIThread.Post(() => RefreshDisplayedSnapshot());
        else if (e.PropertyName == nameof(SettingsFlyoutViewModel.SceneKind))
            Dispatcher.UIThread.Post(ChangeSceneKind);
        else if (e.PropertyName == nameof(SettingsFlyoutViewModel.UseCompactMainMetrics))
            Dispatcher.UIThread.Post(() => CombatantColumns.UseCompactMainMetrics = SettingsFlyout.UseCompactMainMetrics);
        else if (e.PropertyName == nameof(SettingsFlyoutViewModel.ShowFocusStatusBar) ||
                 e.PropertyName == nameof(SettingsFlyoutViewModel.CombatantStatisticsScope))
            Dispatcher.UIThread.Post(() => RefreshDisplayedSnapshot());
    }

    [RelayCommand]
    private void SelectCombatant(CombatantRowViewModel? combatant)
    {
        SelectedCombatant = combatant;
    }

    private async Task StartCaptureAsync()
    {
        if (IsCapturing) return;
        try
        {
            await _processPortDiscoveryService.StartAsync();
            await _captureService.StartAsync();
            IsCapturing = true;
            RefreshCaptureIndicators();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            RefreshCaptureIndicators();
        }
    }

    private async Task StopCaptureAsync()
    {
        if (!IsCapturing) return;
        await _captureService.StopAsync();
        await _processPortDiscoveryService.StopAsync();
        IsCapturing = false;
        RefreshCaptureIndicators();
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (_suppressRefresh)
        {
            return;
        }

        _suppressRefresh = true;
        try
        {
            DrainPendingBossArchives();
            ArchiveEncounter("manual-reset", isAutomatic: true);
            ResetLiveModels(RawPacketDump.RotateLogs);

            ResetLivePresentation();
            RefreshCaptureIndicators();
        }
        finally
        {
            _suppressRefresh = false;
        }
    }

    partial void OnSelectedCombatantChanged(CombatantRowViewModel? oldValue, CombatantRowViewModel? newValue)
    {
        RefreshCombatantDetails();
    }

    partial void OnSelectedEncounterHistoryChanged(EncounterHistoryItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        IsViewingArchivedEncounter = true;
        _displayedSnapshot = value.Record.Snapshot;
        ApplySnapshot(_displayedSnapshot);
    }

    [RelayCommand]
    private void ArchiveCurrentEncounter()
    {
        var record = ArchiveEncounter("manual", isAutomatic: false);
        if (record is null)
        {
            return;
        }

        RebuildEncounterHistory();
        SelectedEncounterHistory = EncounterHistory.FirstOrDefault(x => x.Record.Id == record.Id);
    }

    [RelayCommand]
    private void ReturnToLive()
    {
        IsViewingArchivedEncounter = false;
        SelectedEncounterHistory = null;
        ApplySnapshot(_latestLiveSnapshot);
    }

    private void RefreshCombatStats()
    {
        if (_suppressRefresh)
        {
            return;
        }

        var previousLiveSnapshot = _latestLiveSnapshot;
        DrainPendingBossArchives();
        var selectedCombatantId = IsViewingArchivedEncounter ? 0 : SelectedCombatant?.Id ?? 0;
        var nextLiveFrame = CreateLiveFrame(selectedCombatantId);
        DrainPendingBossArchives();
        if (TryAutoResetEncounter(previousLiveSnapshot, nextLiveFrame.Snapshot))
        {
            nextLiveFrame = CreateLiveFrame(selectedCombatantId);
        }

        _latestLiveFrame = nextLiveFrame;
        _latestLiveSnapshot = nextLiveFrame.Snapshot;
        RefreshCaptureIndicators();

        if (IsViewingArchivedEncounter)
        {
            return;
        }

        _displayedSnapshot = _latestLiveSnapshot;
        ApplySnapshot(_displayedSnapshot);
    }

    public void ProcessUiFrame()
    {
        if (!_isDisposed && IsCapturing)
        {
            RefreshCombatStats();
        }
    }

    internal void RefreshCombatStatsForTesting() => RefreshCombatStats();

    internal void ProcessUiFrameForTesting() => ProcessUiFrame();

    internal void ResetLiveModelsForTesting() => ResetLiveModels(static () => DateTimeOffset.Now);

    private SceneReadModelFrame CreateLiveFrame(int detailCombatantId = 0, bool forceDetailRefresh = false) =>
        detailCombatantId > 0
            ? _captureService.Scene.CreateFrame(detailCombatantId, _combatantDetails, forceDetailRefresh)
            : _captureService.Scene.CreateFrame();

    private void ApplySnapshot(SceneCombatSnapshot snapshot, bool forceDetailRefresh = false)
    {
        UpdateDisplayContext(snapshot);
        NumericStableWidthScopeKey = snapshot.EncounterId;
        var encounterSeconds = snapshot.EncounterTime / 1000.0;
        EncounterTimeSeconds = encounterSeconds;
        LiveSceneMapId = snapshot.MapId;
        SceneName = DisplayContext?.ResolveSceneName(snapshot.Kind, snapshot.MapId, snapshot.BossNpcCodes) ?? string.Empty;
        EnsureBarBrushScope(snapshot.EncounterId);

        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (snapshot.Combatants.TryGetValue(row.Id, out var data) &&
                ShouldDisplayCombatant(row.Id, data))
            {
                row.CharacterClass = data.CharacterClass;
                row.DamagePerSecond = data.DamagePerSecond;
                row.HealingPerSecond = data.HealingPerSecond;
                row.Damage = data.DamageAmount;
                row.Healing = data.HealingAmount;
            }
            else
            {
                Combatants.Remove(row);
            }
        }

        var combatants = snapshot.Combatants.AsSpan();
        foreach (ref readonly var entry in combatants)
        {
            var id = entry.Id;
            var data = entry.Metrics;
            if (Combatants.ContainsKey(id))
                continue;

            if (!ShouldDisplayCombatant(id, data))
                continue;

            Combatants.Add(new CombatantRowViewModel(
                _frameBatchService,
                CombatantColumns,
                id,
                data.CharacterClass,
                data.DamagePerSecond,
                data.HealingPerSecond,
                data.DamageAmount,
                data.HealingAmount));
        }

        if (SelectedCombatant is { } selectedCombatant && !Combatants.ContainsKey(selectedCombatant.Id))
            SelectedCombatant = null;

        RefreshCombatantBars(snapshot.EncounterId);
        Combatants.Sort(CompareCombatantRows);

        var liveFrame = IsViewingArchivedEncounter ? (SceneReadModelFrame?)null : _latestLiveFrame;
        RefreshBossFocus(liveFrame, snapshot);

        RefreshCombatantDetails(forceDetailRefresh);
    }

    private void RefreshDisplayedSnapshot(bool forceDetailRefresh = false)
    {
        if (IsViewingArchivedEncounter && SelectedEncounterHistory is not null)
        {
            _displayedSnapshot = SelectedEncounterHistory.Record.Snapshot;
            ApplySnapshot(_displayedSnapshot, forceDetailRefresh);
            return;
        }

        _displayedSnapshot = _latestLiveSnapshot;
        ApplySnapshot(_displayedSnapshot, forceDetailRefresh);
    }

    private void RefreshBossFocus(SceneReadModelFrame? liveFrame, SceneCombatSnapshot snapshot)
    {
        if (IsViewingArchivedEncounter)
        {
            _bossFocusDisplayGroups.Clear();
            BossFocuses.Clear();
            RefreshCombatantBossShares(EmptyBossDamageContributions);
            return;
        }

        var snapshots = liveFrame?.BossFocuses ?? snapshot.BossFocuses;
        var damageContributions = liveFrame?.BossDamageContributions ?? EmptyBossDamageContributions;
        BossFocusDisplayBuilder.Build(
            snapshots,
            damageContributions,
            DisplayContext,
            SettingsFlyout.CombatantStatisticsScope,
            Combatants.ContainsKey,
            _bossFocusDisplayGroups);
        if (SettingsFlyout.ShowFocusStatusBar)
        {
            SyncBossFocuses(snapshot.EncounterId, damageContributions);
        }
        else
        {
            BossFocuses.Clear();
        }
        RefreshCombatantBossShares(damageContributions);
    }

    private void SyncBossFocuses(Guid encounterId, IReadOnlyList<BossDamageContribution> damageContributions)
    {
        for (var i = BossFocuses.Count - 1; i >= 0; i--)
        {
            var existing = BossFocuses[i];
            var stillPresent = false;
            for (var j = 0; j < _bossFocusDisplayGroups.Count; j++)
            {
                if (_bossFocusDisplayGroups[j].DisplayKey == existing.DisplayKey)
                {
                    stillPresent = true;
                    break;
                }
            }
            if (!stillPresent)
            {
                BossFocuses.RemoveAt(i);
            }
        }

        for (var i = 0; i < _bossFocusDisplayGroups.Count; i++)
        {
            var group = _bossFocusDisplayGroups[i];
            BossFocusViewModel? row = null;
            for (var j = 0; j < BossFocuses.Count; j++)
            {
                if (BossFocuses[j].DisplayKey == group.DisplayKey)
                {
                    row = BossFocuses[j];
                    break;
                }
            }

            if (row is null)
            {
                row = new BossFocusViewModel(
                    _frameBatchService,
                    group.DisplayKey,
                    group.Representative.InstanceId,
                    group.NpcCode,
                    group.InstanceCount,
                    BossFocusDisplayBuilder.NormalizeHpForDisplay(group.Representative.Kind, group.NpcCode, group.Representative.Hp),
                    BossFocusDisplayBuilder.NormalizeHpForDisplay(group.Representative.Kind, group.NpcCode, group.Representative.MaxHp),
                    group.Representative.HasHp,
                    group.Representative.HasMaxHp);
                BossFocuses.Add(row);
            }
            else
            {
                row.Update(
                    group.Representative.InstanceId,
                    group.NpcCode,
                    group.InstanceCount,
                    BossFocusDisplayBuilder.NormalizeHpForDisplay(group.Representative.Kind, group.NpcCode, group.Representative.Hp),
                    BossFocusDisplayBuilder.NormalizeHpForDisplay(group.Representative.Kind, group.NpcCode, group.Representative.MaxHp),
                    group.Representative.HasHp,
                    group.Representative.HasMaxHp);
            }

            var hpBrush = ResolveBossHpBrush(encounterId, group.DisplayKey);
            row.UpdateSegments(CreateBossSegments(group.Representative, damageContributions, hpBrush, encounterId));
        }
    }

    private List<ProgressSegment> CreateBossSegments(SceneBossFocusSnapshot boss, IReadOnlyList<BossDamageContribution> damageContributions, IBrush hpBrush, Guid encounterId)
    {
        _bossSegmentScratch.Clear();
        if (!boss.HasHp || !boss.HasMaxHp)
            return _bossSegmentScratch;

        var maxHp = Math.Max(1, boss.MaxHp);
        var hpRatio = Math.Clamp(Math.Max(0, boss.Hp) / (double)maxHp, 0d, 1d);
        if (hpRatio > 0)
            _bossSegmentScratch.Add(new ProgressSegment(hpRatio, hpBrush));

        var lostRatio = 1d - hpRatio;
        if (lostRatio <= 0)
            return _bossSegmentScratch;

        var start = BossFocusDisplayBuilder.FindContributionStart(damageContributions, boss.InstanceId);
        if (start < 0)
            return _bossSegmentScratch;

        var end = start;
        while (end < damageContributions.Count && damageContributions[end].BossId == boss.InstanceId)
            end++;

        var totalDamage = 0L;
        for (var i = start; i < end; i++)
        {
            var contribution = damageContributions[i];
            if (contribution.DamageAmount > 0 && Combatants.ContainsKey(contribution.SourceCombatantId))
                totalDamage += contribution.DamageAmount;
        }

        if (totalDamage <= 0)
            return _bossSegmentScratch;

        for (var i = start; i < end; i++)
        {
            var contribution = damageContributions[i];
            if (contribution.DamageAmount <= 0 || !Combatants.ContainsKey(contribution.SourceCombatantId))
                continue;

            var ratio = lostRatio * contribution.DamageAmount / totalDamage;
            if (ratio > 0)
                _bossSegmentScratch.Add(new ProgressSegment(ratio, ResolveCombatantBarBrush(encounterId, contribution.SourceCombatantId)));
        }

        return _bossSegmentScratch;
    }

    private void RefreshCombatantBossShares(IReadOnlyList<BossDamageContribution> damageContributions)
    {
        var scope = BossFocusDisplayBuilder.CreateShareScope(_bossFocusDisplayGroups);
        var hasBossColumn = scope.EffectiveHp > 0;
        CombatantColumns.Update(hasBossColumn);
        if (!hasBossColumn)
        {
            for (var i = 0; i < Combatants.Count; i++)
                Combatants[i].UpdateBossShare(0, isVisible: false);
            return;
        }

        for (var i = 0; i < Combatants.Count; i++)
        {
            var row = Combatants[i];
            var damage = BossFocusDisplayBuilder.FindAggregateContributionAmount(
                _bossFocusDisplayGroups,
                damageContributions,
                row.Id);
            row.UpdateBossShare(damage > 0 ? damage / (double)scope.EffectiveHp : 0, isVisible: true);
        }
    }

    private void RefreshCombatantBars(Guid encounterId)
    {
        var maxMetric = 0d;
        for (var i = 0; i < Combatants.Count; i++)
            maxMetric = Math.Max(maxMetric, ResolveCombatantSortMetric(Combatants[i]));

        for (var i = 0; i < Combatants.Count; i++)
        {
            var row = Combatants[i];
            var ratio = maxMetric > 0 ? ResolveCombatantSortMetric(row) / maxMetric : 0d;
            row.UpdateBar(ratio, ResolveCombatantBarBrush(encounterId, row.Id));
        }
    }

    private int CompareCombatantRows(CombatantRowViewModel left, CombatantRowViewModel right)
    {
        var cmp = ResolveCombatantSortMetric(right).CompareTo(ResolveCombatantSortMetric(left));
        if (cmp != 0)
            return cmp;

        cmp = right.Damage.CompareTo(left.Damage);
        return cmp != 0 ? cmp : left.Id.CompareTo(right.Id);
    }

    private double ResolveCombatantSortMetric(CombatantRowViewModel row) =>
        SettingsFlyout.CombatantSortMetric == CombatantSortMetric.TotalDamage
            ? row.Damage
            : row.DamagePerSecond;

    private void EnsureBarBrushScope(Guid encounterId)
    {
        if (_barBrushEncounterId == encounterId)
            return;

        _barBrushEncounterId = encounterId;
        _barBrushSeed = CreateColorSeed(encounterId);
        _barBrushBaseHue = (int)(_barBrushSeed % 360);
        _nextBarHueIndex = 0;
        _combatantBarBrushes.Clear();
        _bossHpBarBrushes.Clear();
    }

    private IBrush ResolveCombatantBarBrush(Guid encounterId, int combatantId)
    {
        EnsureBarBrushScope(encounterId);
        if (_combatantBarBrushes.TryGetValue(combatantId, out var brush))
            return brush;

        brush = CreateGeneratedBrush(_nextBarHueIndex++);
        _combatantBarBrushes.Add(combatantId, brush);
        return brush;
    }

    private IBrush ResolveBossHpBrush(Guid encounterId, long bossId)
    {
        EnsureBarBrushScope(encounterId);
        if (_bossHpBarBrushes.TryGetValue(bossId, out var brush))
            return brush;

        brush = CreateGeneratedBrush(_nextBarHueIndex++);
        _bossHpBarBrushes.Add(bossId, brush);
        return brush;
    }

    private SolidColorBrush CreateGeneratedBrush(int index)
    {
        var hue = AllocateBarHue(index);
        var variant = MixHash(_barBrushSeed, unchecked((uint)index));
        var saturation = BarColorMinSaturation + BarColorSaturationRange * ((variant & 0xffff) / 65_535d);
        var lightness = BarColorMinLightness + BarColorLightnessRange * (((variant >> 16) & 0xffff) / 65_535d);
        return new SolidColorBrush(HslColor.ToRgb(hue, saturation, lightness, BarColorAlpha));
    }

    private int AllocateBarHue(int index)
    {
        var ring = index / BarHueRingSize;
        var slot = index % BarHueRingSize;
        var offset = GetHueRingOffset(ring);
        return NormalizeHue(_barBrushBaseHue + offset + slot * BarHueGap);
    }

    private static int GetHueRingOffset(int ring)
    {
        if (ring == 0)
            return 0;

        var level = 0;
        var value = ring;
        while (value > 1)
        {
            value >>= 1;
            level++;
        }

        var denominator = 1 << (level + 1);
        var odd = ((ring - (1 << level)) << 1) + 1;
        return BarHueGap * odd / denominator;
    }

    private static int NormalizeHue(int hue)
    {
        hue %= 360;
        return hue < 0 ? hue + 360 : hue;
    }

    private static uint CreateColorSeed(Guid encounterId)
    {
        Span<byte> bytes = stackalloc byte[16];
        encounterId.TryWriteBytes(bytes);
        var hash = 2_166_136_261u;
        for (var i = 0; i < bytes.Length; i++)
            hash = (hash ^ bytes[i]) * 16_777_619u;
        return hash;
    }

    private static uint MixHash(uint hash, uint value)
    {
        hash = (hash ^ (byte)value) * 16_777_619u;
        hash = (hash ^ (byte)(value >> 8)) * 16_777_619u;
        hash = (hash ^ (byte)(value >> 16)) * 16_777_619u;
        return (hash ^ (byte)(value >> 24)) * 16_777_619u;
    }

    private bool ShouldDisplayCombatant(int combatantId, SceneCombatantMetrics data)
    {
        if (!data.IsVisiblePlayerCombatant)
        {
            return false;
        }

        var scope = SettingsFlyout.CombatantStatisticsScope;
        if (scope == CombatantStatisticsScope.All)
        {
            return data.CharacterClass is not null;
        }

        if (DisplayContext?.TryResolvePcMetadata(combatantId, out var metadata) != true)
        {
            return false;
        }

        if (metadata.IsLocalPlayer)
        {
            return true;
        }

        return scope switch
        {
            CombatantStatisticsScope.Party => metadata.GroupRelation == PlayerGroupRelation.PartyMember,
            CombatantStatisticsScope.Force => metadata.GroupRelation is PlayerGroupRelation.PartyMember or PlayerGroupRelation.ForceMember,
            _ => false
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _captureService.StatusChanged -= OnCaptureStatusChanged;
        _captureService.RttResolved -= OnRttResolved;
        _languageService.LanguageChanged -= OnLanguageChanged;
        _gameResourceService.ResourcesChanged -= OnResourcesChanged;
        _encounterArchiveService.HistoryChanged -= OnEncounterHistoryChanged;
        SettingsFlyout.PropertyChanged -= OnSettingsFlyoutPropertyChanged;
        await StopCaptureAsync().ConfigureAwait(false);
        await _processPortDiscoveryService.DisposeAsync().ConfigureAwait(false);
    }

    private void ApplyLocalizedUiText()
    {
        Status = Localization["Status_Ready"];
        NumericStableWidthScopeKey = _displayedSnapshot.EncounterId;
        EncounterTimeSeconds = 0d;
        RoundTripTimeMilliseconds = 0;
        LiveSceneMapId = _displayedSnapshot.MapId;
        UpdateDisplayContext(_displayedSnapshot);
        SceneName = DisplayContext?.ResolveSceneName(_displayedSnapshot.Kind, _displayedSnapshot.MapId, _displayedSnapshot.BossNpcCodes) ?? string.Empty;
    }

    private void RebuildEncounterHistory()
    {
        var selectedId = SelectedEncounterHistory?.Record.Id;
        EncounterHistory.Clear();
        foreach (var record in _encounterArchiveService.History)
        {
            var displayContext = CreateArchivedDisplayContext(record);
            EncounterHistory.Add(new EncounterHistoryItemViewModel(
                record,
                displayContext,
                displayContext.ResolveSceneName(record.ScenePayload.Kind, record.Snapshot.MapId, record.ScenePayload.BossNpcCodes),
                record.ArchivedAt.ToString("HH:mm:ss")));
        }

        HasArchivedEncounters = EncounterHistory.Count > 0;
        SelectedEncounterHistory = EncounterHistory.FirstOrDefault(x => x.Record.Id == selectedId);
    }

    private void UpdateDisplayContext(SceneCombatSnapshot snapshot)
    {
        var isArchived = IsViewingArchivedEncounter && SelectedEncounterHistory is not null;
        var archivedRecord = isArchived ? SelectedEncounterHistory!.Record : null;
        var metadataRevision = isArchived ? -1 : _captureService.Scene.Owner.MetadataRegistry.Revision;
        if (ReferenceEquals(_displayContextSnapshot, snapshot) &&
            ReferenceEquals(_displayContextArchivedRecord, archivedRecord) &&
            _displayContextBuiltVersion == _displayContextVersion &&
            _displayContextMetadataRevision == metadataRevision &&
            _displayContextIsArchived == isArchived)
        {
            return;
        }

        _displayContextSnapshot = snapshot;
        _displayContextArchivedRecord = archivedRecord;
        _displayContextBuiltVersion = _displayContextVersion;
        _displayContextMetadataRevision = metadataRevision;
        _displayContextIsArchived = isArchived;
        DisplayContext = isArchived
            ? CreateArchivedDisplayContext(archivedRecord!)
            : CreateLiveDisplayContext(snapshot);
        _combatantDetails.DisplayContext = DisplayContext;
    }

    private SceneDisplayContext CreateLiveDisplayContext(SceneCombatSnapshot snapshot)
        => CreateDisplayContext(snapshot, SceneIdentityScope.Empty, _captureService.Scene.Owner.MetadataRegistry);

    private SceneDisplayContext CreateArchivedDisplayContext(ArchivedEncounterRecord record)
        => CreateDisplayContext(record.Snapshot, record.ScenePayload.IdentityScope, null);

    private SceneDisplayContext CreateDisplayContext(SceneCombatSnapshot snapshot, SceneIdentityScope scope, RuntimeMetadataRegistry? metadataRegistry)
        => new(scope, metadataRegistry, snapshot, _gameResourceService, Localization["Scene_Unknown"]);

    private ArchivedEncounterRecord? ArchiveEncounter(string trigger, bool isAutomatic, SceneCombatSnapshot? archivedSnapshot = null)
    {
        var owner = _captureService.Scene.Owner;
        if (archivedSnapshot is null)
        {
            var archive = _captureService.Scene.CreateArchiveCapture();
            return _encounterArchiveService.Archive(archive.Snapshot, archive.Payload, trigger, isAutomatic);
        }

        var payload = owner.CreateArchivePayload(archivedSnapshot);
        return _encounterArchiveService.Archive(archivedSnapshot, payload, trigger, isAutomatic);
    }

    private bool TryAutoResetEncounter(SceneCombatSnapshot previousLiveSnapshot, SceneCombatSnapshot latestLiveSnapshot)
    {
        if (_captureService.Scene.Kind == SceneKind.Boss)
            return false;

        if (TryResolveMapTransitionResetReason(previousLiveSnapshot, latestLiveSnapshot, out var mapTransitionReason))
        {
            ArchiveEncounter(mapTransitionReason, isAutomatic: true, previousLiveSnapshot);
            ResetLiveModels(RawPacketDump.RotateLogs);
            return true;
        }

        return false;
    }

    internal static bool TryResolveMapTransitionResetReason(
        SceneCombatSnapshot previousLiveSnapshot,
        SceneCombatSnapshot latestLiveSnapshot,
        out string reason)
    {
        reason = string.Empty;

        if (!HasArchivableEncounter(previousLiveSnapshot))
        {
            return false;
        }

        var previousMapId = previousLiveSnapshot.MapId;
        var latestMapId = latestLiveSnapshot.MapId;
        var previousInstanceId = previousLiveSnapshot.MapInstanceId;
        var latestInstanceId = latestLiveSnapshot.MapInstanceId;

        if (previousMapId != latestMapId)
        {
            reason = "map-transition";
            return true;
        }

        if (previousInstanceId != latestInstanceId)
        {
            reason = "map-instance-transition";
            return true;
        }

        return false;
    }

    private static bool HasArchivableEncounter(SceneCombatSnapshot snapshot)
        => snapshot.EncounterTime > 0 && snapshot.Combatants.Count > 0;

    private void ResetLiveModels(Func<DateTimeOffset> resolveSessionStarted)
    {
        _captureService.Scene.Reset(resolveSessionStarted);
    }

    private void ChangeSceneKind()
    {
        if (_isDisposed || _suppressRefresh || _captureService.Scene.Kind == SettingsFlyout.SceneKind)
            return;

        _suppressRefresh = true;
        try
        {
            DrainPendingBossArchives();
            var archive = _captureService.Scene.ChangeKind(SettingsFlyout.SceneKind, RawPacketDump.RotateLogs, archiveCurrent: true);
            if (archive is { } capture)
                ArchiveCapture(in capture, "scene-kind-change", isAutomatic: true);
            ResetLivePresentation();
        }
        finally
        {
            _suppressRefresh = false;
        }
    }

    private void DrainPendingBossArchives()
    {
        while (_captureService.Scene.TryDequeuePendingArchive(out var capture))
            ArchiveCapture(in capture, "boss-encounter-completed", isAutomatic: true);
    }

    private ArchivedEncounterRecord? ArchiveCapture(in SceneArchiveCapture capture, string trigger, bool isAutomatic) =>
        _encounterArchiveService.Archive(capture.Snapshot, capture.Payload, trigger, isAutomatic);

    private void ResetLivePresentation()
    {
        _latestLiveSnapshot = new SceneCombatSnapshot();
        _displayedSnapshot = new SceneCombatSnapshot();
        _latestLiveFrame = new SceneReadModelFrame();
        Combatants.Clear();
        CombatantDetails.Clear();
        BossFocuses.Clear();
        CombatantColumns.Update(hasBossColumn: false);
        SelectedCombatant = null;
        SelectedEncounterHistory = null;
        IsViewingArchivedEncounter = false;
        ApplyLocalizedUiText();
    }

    private void RefreshCombatantDetails(bool forceRefresh = false)
    {
        if (SelectedCombatant is null)
        {
            CombatantDetails.Deactivate();
            return;
        }

        var encounterContextId = IsViewingArchivedEncounter
            ? SelectedEncounterHistory?.Record.EncounterId ?? Guid.Empty
            : _displayedSnapshot.EncounterId;

        var snapshot = IsViewingArchivedEncounter && SelectedEncounterHistory is not null
            ? SelectedEncounterHistory.Record.Snapshot
            : _displayedSnapshot;

        if (IsViewingArchivedEncounter && SelectedEncounterHistory is { } history)
        {
            var detail = history.Record.ScenePayload.CreateDetailDelta(SelectedCombatant.Id);
            CombatantDetails.SelectSceneEncounterCombatant(encounterContextId, SelectedCombatant.Id, snapshot, detail, forceRefresh);
            return;
        }

        if (!IsViewingArchivedEncounter)
        {
            if (_latestLiveFrame.DetailCombatantId != SelectedCombatant.Id || _latestLiveFrame.DetailUpdate.CombatantId != SelectedCombatant.Id || forceRefresh)
            {
                _latestLiveFrame = CreateLiveFrame(SelectedCombatant.Id, forceRefresh);
                _latestLiveSnapshot = _latestLiveFrame.Snapshot;
                _displayedSnapshot = _latestLiveFrame.Snapshot;
                snapshot = _displayedSnapshot;
            }

            CombatantDetails.SelectLiveSceneEncounterCombatant(encounterContextId, SelectedCombatant.Id, snapshot, _latestLiveFrame.DetailUpdate, forceRefresh);
            return;
        }

        CombatantDetails.Clear();
    }

    private void RefreshCaptureIndicators()
    {
        DriverIndicatorColor = _captureService.HasDriverError
            ? IndicatorErrorColor
            : _captureService.IsDriverActive
                ? IndicatorOkColor
                : IndicatorIdleColor;
        DriverIndicatorToolTip = _captureService.HasDriverError && !string.IsNullOrWhiteSpace(_captureService.LastStatusMessage)
            ? _captureService.LastStatusMessage
            : _captureService.IsDriverActive
                ? Localization["Status_DriverReady"]
                : Localization["Status_DriverIdle"];

        if (!_processPortDiscoveryService.IsMonitoring)
        {
            GamePortIndicatorColor = IndicatorIdleColor;
            GamePortIndicatorToolTip = Localization["Status_PortIdle"];
        }
        else if (_processPortDiscoveryService.AllPorts.Length > 0)
        {
            GamePortIndicatorColor = IndicatorOkColor;
            GamePortIndicatorToolTip = Localization["Status_PortReady"];
        }
        else
        {
            GamePortIndicatorColor = IndicatorWarnColor;
            GamePortIndicatorToolTip = Localization["Status_PortWaiting"];
        }

        var isCaptureLocked = CaptureConnectionGate.IsLocked;
        var isProxied = CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection) && lockedConnection.SourceIsLocal;
        if (!isCaptureLocked)
        {
            RoundTripTimeMilliseconds = 0;
            CaptureLockIndicatorColor = IndicatorIdleColor;
            CaptureLockIndicatorToolTip = Localization["Status_Unlocked"];
        }
        else if (isProxied)
        {
            CaptureLockIndicatorColor = IndicatorWarnColor;
            CaptureLockIndicatorToolTip = Localization["Status_LockedProxy"];
        }
        else
        {
            CaptureLockIndicatorColor = IndicatorOkColor;
            CaptureLockIndicatorToolTip = Localization["Status_Locked"];
        }

        var currentRttMilliseconds = _captureService.CurrentRoundTripTimeMilliseconds;
        if (!currentRttMilliseconds.HasValue || currentRttMilliseconds.Value <= 0)
        {
            RoundTripTimeMilliseconds = 0;
            LatencyIndicatorColor = IndicatorIdleColor;
            LatencyToolTip = Localization["Status_RttUnavailable"];
            return;
        }

        RoundTripTimeMilliseconds = Math.Max(1, (int)Math.Round(currentRttMilliseconds.Value));
        if (isProxied)
        {
            LatencyIndicatorColor = IndicatorWarnColor;
            LatencyToolTip = Localization["Status_LatencyEstimatedFromCombat"];
        }
        else
        {
            LatencyIndicatorColor = IndicatorInfoColor;
            LatencyToolTip = Localization["Status_RttEstimated"];
        }
    }

}
