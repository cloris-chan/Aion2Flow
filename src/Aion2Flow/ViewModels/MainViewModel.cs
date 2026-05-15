using System.Collections.ObjectModel;
using Avalonia.Threading;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class MainViewModel : FrameBatchedObservableObject, IAsyncDisposable
{
    private const string IndicatorIdleColor = "#6F7A8A";
    private const string IndicatorOkColor = "#6FD38A";
    private const string IndicatorWarnColor = "#F3C969";
    private const string IndicatorErrorColor = "#F07C82";
    private const string IndicatorInfoColor = "#8DD6FF";

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
    private int _displayContextVersion;
    private int _displayContextBuiltVersion = -1;
    private bool _displayContextIsArchived;
    private volatile bool _suppressRefresh;
    private bool _isDisposed;

    public LocalizationService Localization { get; }
    public CombatantDetailsFlyoutViewModel CombatantDetails => _combatantDetails;
    public SettingsFlyoutViewModel SettingsFlyout { get; }
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
        DisplayContext = CreateLiveDisplayContext(_displayedSnapshot);
        _combatantDetails.DisplayContext = DisplayContext;

        _captureService.StatusChanged += OnCaptureStatusChanged;
        _captureService.RttResolved += OnRttResolved;
        _languageService.LanguageChanged += OnLanguageChanged;
        _gameResourceService.ResourcesChanged += OnResourcesChanged;
        _encounterArchiveService.HistoryChanged += OnEncounterHistoryChanged;

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
            ArchiveEncounter(_latestLiveSnapshot, "manual-reset", isAutomatic: true);
            ResetLiveModels(RawPacketDump.RotateLogs);

            _latestLiveSnapshot = new SceneCombatSnapshot();
            _displayedSnapshot = new SceneCombatSnapshot();
            _latestLiveFrame = new SceneReadModelFrame();
            Combatants.Clear();
            CombatantDetails.Clear();
            BossFocuses.Clear();
            SelectedCombatant = null;
            SelectedEncounterHistory = null;
            IsViewingArchivedEncounter = false;
            ApplyLocalizedUiText();
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
        var record = ArchiveEncounter(_latestLiveSnapshot, "manual", isAutomatic: false);
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
        var selectedCombatantId = IsViewingArchivedEncounter ? 0 : SelectedCombatant?.Id ?? 0;
        var nextLiveFrame = CreateLiveFrame(selectedCombatantId);
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
            ? _captureService.Scene.Owner.CreateFrame(detailCombatantId, _combatantDetails, forceDetailRefresh)
            : _captureService.Scene.Owner.CreateFrame();

    private void ApplySnapshot(SceneCombatSnapshot snapshot, bool forceDetailRefresh = false)
    {
        UpdateDisplayContext(snapshot);
        NumericStableWidthScopeKey = snapshot.EncounterId;
        var encounterSeconds = snapshot.EncounterTime / 1000.0;
        EncounterTimeSeconds = encounterSeconds;
        LiveSceneMapId = snapshot.MapId;
        var liveFrame = IsViewingArchivedEncounter ? (SceneReadModelFrame?)null : _latestLiveFrame;
        RefreshBossFocus(liveFrame, snapshot);

        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (snapshot.Combatants.TryGetValue(row.Id, out var data) &&
                ShouldDisplayCombatant(data))
            {
                row.CharacterClass = data.CharacterClass;
                row.DamagePerSecond = data.DamagePerSecond;
                row.HealingPerSecond = data.HealingPerSecond;
                row.Damage = data.DamageAmount;
                row.Healing = data.HealingAmount;
                row.DamageContribution = data.DamageContribution;
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
            if (Combatants.Contains(id))
                continue;

            if (!ShouldDisplayCombatant(data))
                continue;

            Combatants.Add(new CombatantRowViewModel(
                _frameBatchService,
                id,
                data.CharacterClass,
                data.DamagePerSecond,
                data.HealingPerSecond,
                data.DamageAmount,
                data.HealingAmount,
                data.DamageContribution));
        }

        Combatants.Sort((a, b) => b.Damage.CompareTo(a.Damage));

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
            BossFocuses.Clear();
            return;
        }

        var snapshots = liveFrame?.BossFocuses ?? snapshot.BossFocuses;
        SyncBossFocuses(snapshots);
    }

    private void SyncBossFocuses(SnapshotList<SceneBossFocusSnapshot> snapshots)
    {
        for (var i = BossFocuses.Count - 1; i >= 0; i--)
        {
            var existing = BossFocuses[i];
            var stillPresent = false;
            for (var j = 0; j < snapshots.Count; j++)
            {
                if (snapshots[j].InstanceId == existing.InstanceId)
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

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var instanceId = snapshot.InstanceId;
            BossFocusViewModel? row = null;
            for (var j = 0; j < BossFocuses.Count; j++)
            {
                if (BossFocuses[j].InstanceId == instanceId)
                {
                    row = BossFocuses[j];
                    break;
                }
            }

            if (row is null)
            {
                row = new BossFocusViewModel(_frameBatchService, instanceId, snapshot.Hp, snapshot.MaxHp, snapshot.HasHp);
                BossFocuses.Add(row);
                continue;
            }
            row.Update(snapshot.Hp, snapshot.MaxHp, snapshot.HasHp);
        }
    }

    private static bool ShouldDisplayCombatant(SceneCombatantMetrics data)
    {
        if (data.CharacterClass is null)
        {
            return false;
        }

        return data.IsVisiblePlayerCombatant;
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
    }

    private void RebuildEncounterHistory()
    {
        var selectedId = SelectedEncounterHistory?.Record.Id;
        EncounterHistory.Clear();
        foreach (var record in _encounterArchiveService.History)
        {
            EncounterHistory.Add(new EncounterHistoryItemViewModel(
                record,
                CreateArchivedDisplayContext(record),
                record.Snapshot.MapId,
                record.ArchivedAt.ToString("HH:mm:ss")));
        }

        HasArchivedEncounters = EncounterHistory.Count > 0;
        SelectedEncounterHistory = EncounterHistory.FirstOrDefault(x => x.Record.Id == selectedId);
    }

    private void UpdateDisplayContext(SceneCombatSnapshot snapshot)
    {
        var isArchived = IsViewingArchivedEncounter && SelectedEncounterHistory is not null;
        var archivedRecord = isArchived ? SelectedEncounterHistory!.Record : null;
        if (ReferenceEquals(_displayContextSnapshot, snapshot) &&
            ReferenceEquals(_displayContextArchivedRecord, archivedRecord) &&
            _displayContextBuiltVersion == _displayContextVersion &&
            _displayContextIsArchived == isArchived)
        {
            return;
        }

        _displayContextSnapshot = snapshot;
        _displayContextArchivedRecord = archivedRecord;
        _displayContextBuiltVersion = _displayContextVersion;
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

    private ArchivedEncounterRecord? ArchiveEncounter(SceneCombatSnapshot snapshot, string trigger, bool isAutomatic)
        => _encounterArchiveService.Archive(_captureService.Scene.Owner.CreateArchivePayload(snapshot), trigger, isAutomatic);

    private bool TryAutoResetEncounter(SceneCombatSnapshot previousLiveSnapshot, SceneCombatSnapshot latestLiveSnapshot)
    {
        if (TryResolveMapTransitionResetReason(previousLiveSnapshot, latestLiveSnapshot, out var mapTransitionReason))
        {
            ArchiveEncounter(previousLiveSnapshot, mapTransitionReason, isAutomatic: true);
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

        if (previousLiveSnapshot.MapId == 0 ||
            latestLiveSnapshot.MapId == 0 ||
            !HasArchivableEncounter(previousLiveSnapshot))
        {
            return false;
        }

        if (previousLiveSnapshot.MapId != latestLiveSnapshot.MapId)
        {
            if (ShouldArchiveMapIdTransition(previousLiveSnapshot, latestLiveSnapshot))
            {
                reason = "map-transition";
                return true;
            }

            return false;
        }

        if (previousLiveSnapshot.MapInstanceId != latestLiveSnapshot.MapInstanceId)
        {
            if ((previousLiveSnapshot.MapInstanceId == 0) != (latestLiveSnapshot.MapInstanceId == 0))
            {
                reason = "map-instance-transition";
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool ShouldArchiveMapIdTransition(
        SceneCombatSnapshot previousLiveSnapshot,
        SceneCombatSnapshot latestLiveSnapshot)
    {
        if (previousLiveSnapshot.MapInstanceId != 0 &&
            latestLiveSnapshot.MapInstanceId != 0 &&
            previousLiveSnapshot.MapInstanceId == latestLiveSnapshot.MapInstanceId)
        {
            return false;
        }

        return IsBoundaryLayerMap(previousLiveSnapshot.MapId) ||
            IsBoundaryLayerMap(latestLiveSnapshot.MapId) ||
            previousLiveSnapshot.MapInstanceId != 0 ||
            latestLiveSnapshot.MapInstanceId != 0;
    }

    private static bool IsBoundaryLayerMap(uint mapId) => mapId >= 100000;

    private static bool HasArchivableEncounter(SceneCombatSnapshot snapshot)
        => snapshot.EncounterTime > 0 && snapshot.Combatants.Count > 0;

    private void ResetLiveModels(Func<DateTimeOffset> resolveSessionStarted)
    {
        _captureService.Scene.Reset(resolveSessionStarted);
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
