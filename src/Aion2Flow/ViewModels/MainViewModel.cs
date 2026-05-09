using System.Collections.ObjectModel;
using Avalonia.Threading;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string IndicatorIdleColor = "#6F7A8A";
    private const string IndicatorOkColor = "#6FD38A";
    private const string IndicatorWarnColor = "#F3C969";
    private const string IndicatorErrorColor = "#F07C82";
    private const string IndicatorInfoColor = "#8DD6FF";
    private const long BossFocusVisibilityTimeoutMilliseconds = 2_000;

    private readonly WinDivertCaptureService _captureService;
    private readonly ProcessPortDiscoveryService _processPortDiscoveryService;
    private readonly LanguageService _languageService;
    private readonly GameResourceService _gameResourceService;
    private readonly EncounterArchiveService _encounterArchiveService;
    private readonly CombatantDetailsFlyoutViewModel _combatantDetails;

    private PeriodicTimer? _refreshTimer;
    private Task? _refreshTask;
    private SceneCombatSnapshot _latestLiveSnapshot = new();
    private SceneCombatSnapshot _displayedSnapshot = new();
    private volatile bool _suppressRefresh;
    private bool _isDisposed;

    public LocalizationService Localization { get; }
    public CombatantDetailsFlyoutViewModel CombatantDetails => _combatantDetails;
    public SettingsFlyoutViewModel SettingsFlyout { get; }
    public ObservableCollection<BossFocusViewModel> BossFocuses { get; } = [];
    public KeyedObservableCollection<int, CombatantRowViewModel> Combatants { get; } = new(x => x.Id);
    public ObservableCollection<EncounterHistoryItemViewModel> EncounterHistory { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int RoundTripTimeMilliseconds { get; set; }

    [ObservableProperty]
    public partial double EncounterTimeSeconds { get; set; }

    [ObservableProperty]
    public partial string LiveSceneName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DriverIndicatorColor { get; set; } = IndicatorIdleColor;

    [ObservableProperty]
    public partial string GamePortIndicatorColor { get; set; } = IndicatorIdleColor;

    [ObservableProperty]
    public partial string CaptureLockIndicatorColor { get; set; } = IndicatorIdleColor;

    [ObservableProperty]
    public partial string LatencyIndicatorColor { get; set; } = IndicatorIdleColor;

    [ObservableProperty]
    public partial string DriverIndicatorToolTip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GamePortIndicatorToolTip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CaptureLockIndicatorToolTip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatencyToolTip { get; set; } = string.Empty;

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
        SettingsFlyoutViewModel settingsFlyout)
    {
        _captureService = captureService;
        _processPortDiscoveryService = processPortDiscoveryService;
        _languageService = languageService;
        _gameResourceService = gameResourceService;
        _encounterArchiveService = encounterArchiveService;
        _combatantDetails = combatantDetails;
        Localization = localization;
        SettingsFlyout = settingsFlyout;

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
            _refreshTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            await _processPortDiscoveryService.StartAsync();
            await _captureService.StartAsync();
            _refreshTask = RunRefreshLoopAsync(_refreshTimer);
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
        _refreshTimer?.Dispose();
        if (_refreshTask is not null)
        {
            await _refreshTask;
        }
        _refreshTimer = null;
        _refreshTask = null;
        IsCapturing = false;
        RefreshCaptureIndicators();
    }

    private async Task RunRefreshLoopAsync(PeriodicTimer timer)
    {
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            Dispatcher.UIThread.Post(RefreshCombatStats);
        }
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
        var nextLiveSnapshot = CreateLiveSnapshot();
        if (TryAutoResetEncounter(previousLiveSnapshot, nextLiveSnapshot))
        {
            nextLiveSnapshot = CreateLiveSnapshot();
        }

        _latestLiveSnapshot = nextLiveSnapshot;
        RefreshCaptureIndicators();

        if (IsViewingArchivedEncounter)
        {
            return;
        }

        _displayedSnapshot = _latestLiveSnapshot;
        ApplySnapshot(_displayedSnapshot);
    }

    internal void RefreshCombatStatsForTesting() => RefreshCombatStats();

    internal void ResetLiveModelsForTesting() => ResetLiveModels(static () => DateTimeOffset.Now);

    private SceneCombatSnapshot CreateLiveSnapshot() => _captureService.Scene.Owner.CreateSnapshot();

    private void ApplySnapshot(SceneCombatSnapshot snapshot, bool forceDetailRefresh = false)
    {
        var encounterSeconds = snapshot.EncounterTime / 1000.0;
        EncounterTimeSeconds = encounterSeconds;
        LiveSceneName = ResolveSceneDisplayName(snapshot.MapId);
        var sceneOwner = IsViewingArchivedEncounter ? null : _captureService.Scene.Owner;
        RefreshBossFocus(sceneOwner, snapshot);

        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (snapshot.Combatants.TryGetValue(row.Id, out var data) &&
                ShouldDisplayCombatant(sceneOwner, row.Id, data))
            {
                row.DisplayName = ResolveDisplayName(snapshot, sceneOwner, row.Id);
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

        foreach (var (id, data) in snapshot.Combatants)
        {
            if (Combatants.Contains(id))
                continue;

            if (!ShouldDisplayCombatant(sceneOwner, id, data))
                continue;
            var displayName = ResolveDisplayName(snapshot, sceneOwner, id);

            Combatants.Add(new CombatantRowViewModel
            {
                Id = id,
                DisplayName = displayName,
                CharacterClass = data.CharacterClass,
                DamagePerSecond = data.DamagePerSecond,
                HealingPerSecond = data.HealingPerSecond,
                Damage = data.DamageAmount,
                Healing = data.HealingAmount,
                DamageContribution = data.DamageContribution
            });
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

    private string ResolveDisplayName(SceneCombatSnapshot snapshot, SceneReadModelOwner? sceneOwner, int id)
        => sceneOwner is not null
            ? ResolveSceneDisplayName(snapshot, sceneOwner.Entities, sceneOwner.Metadata, id)
            : ResolveSnapshotDisplayName(snapshot, id);

    private static string ResolveSceneDisplayName(SceneCombatSnapshot snapshot, EntityStore entities, MetadataStore metadata, int id)
    {
        if (metadata.TryGetDisplayName(id, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (entities.TryGet(id, out var entity))
        {
            if (!string.IsNullOrWhiteSpace(entity.Nickname))
                return entity.Nickname;

            if (entity.NpcCode is int npcCode)
            {
                if (CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
                    return catalogEntry.Name;

                if (metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName))
                    return npcName;
            }
        }

        return snapshot.Combatants.TryGetValue(id, out var combatant) && !string.IsNullOrWhiteSpace(combatant.Nickname)
            ? combatant.Nickname
            : id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ResolveSnapshotDisplayName(SceneCombatSnapshot snapshot, int id)
        => snapshot.Combatants.TryGetValue(id, out var combatant) && !string.IsNullOrWhiteSpace(combatant.Nickname)
            ? combatant.Nickname
            : id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void RefreshBossFocus(SceneReadModelOwner? sceneOwner, SceneCombatSnapshot snapshot)
    {
        if (IsViewingArchivedEncounter)
        {
            BossFocuses.Clear();
            return;
        }

        if (sceneOwner is not null)
        {
            var now = ResolveBossFocusNow(snapshot);
            var snapshots = sceneOwner.BossFocus.GetObservedBosses(now, BossFocusVisibilityTimeoutMilliseconds);
            SyncBossFocuses(snapshots.Count, i => snapshots[i].InstanceId, i => snapshots[i].Hp, i => snapshots[i].MaxHp, i => snapshots[i].HasHp, id => ResolveSceneDisplayName(snapshot, sceneOwner.Entities, sceneOwner.Metadata, id));
        }
    }

    private static long ResolveBossFocusNow(SceneCombatSnapshot snapshot)
        => snapshot.EncounterEndTime > 0
            ? snapshot.EncounterEndTime
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void SyncBossFocuses(int snapshotCount, Func<int, int> getInstanceId, Func<int, int> getHp, Func<int, int> getMaxHp, Func<int, bool> getHasHp, Func<int, string> resolveDisplayName)
    {
        for (var i = BossFocuses.Count - 1; i >= 0; i--)
        {
            var existing = BossFocuses[i];
            var stillPresent = false;
            for (var j = 0; j < snapshotCount; j++)
            {
                if (getInstanceId(j) == existing.InstanceId)
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

        for (var i = 0; i < snapshotCount; i++)
        {
            var instanceId = getInstanceId(i);
            BossFocusViewModel? row = null;
            for (var j = 0; j < BossFocuses.Count; j++)
            {
                if (BossFocuses[j].InstanceId == instanceId)
                {
                    row = BossFocuses[j];
                    break;
                }
            }

            var displayName = resolveDisplayName(instanceId);
            if (row is null)
            {
                row = new BossFocusViewModel { InstanceId = instanceId };
                BossFocuses.Add(row);
            }
            row.Update(displayName, getHp(i), getMaxHp(i), getHasHp(i));
        }
    }

    private static bool ShouldDisplayCombatant(SceneReadModelOwner? sceneOwner, int combatantId, SceneCombatantMetrics data)
    {
        if (data.CharacterClass is null)
        {
            return false;
        }

        if (sceneOwner is not null)
            return ShouldDisplaySceneCombatant(sceneOwner.Entities, combatantId);

        return true;
    }

    private static bool ShouldDisplaySceneCombatant(EntityStore entities, int combatantId)
    {
        if (!entities.TryGet(combatantId, out var entity))
            return true;

        if (entity.NpcCode.HasValue)
            return false;

        return entity.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon);
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
        EncounterTimeSeconds = 0d;
        RoundTripTimeMilliseconds = 0;
        LiveSceneName = ResolveSceneDisplayName(_displayedSnapshot.MapId);
    }

    private void RebuildEncounterHistory()
    {
        var selectedId = SelectedEncounterHistory?.Record.Id;
        EncounterHistory.Clear();
        foreach (var record in _encounterArchiveService.History)
        {
            EncounterHistory.Add(new EncounterHistoryItemViewModel(record, BuildHistoryDisplayName(record)));
        }

        HasArchivedEncounters = EncounterHistory.Count > 0;
        SelectedEncounterHistory = EncounterHistory.FirstOrDefault(x => x.Record.Id == selectedId);
    }

    private string BuildHistoryDisplayName(ArchivedEncounterRecord record)
        => $"{ResolveSceneDisplayName(record.Snapshot.MapId)} {record.ArchivedAt:HH:mm:ss}";

    private string ResolveSceneDisplayName(uint mapId)
    {
        var mapName = mapId == 0
            ? string.Empty
            : _gameResourceService.ResolveMapName(mapId);

        if (string.IsNullOrEmpty(mapName))
        {
            mapName = Localization["Scene_Unknown"];
            if (string.IsNullOrEmpty(mapName))
            {
                mapName = "Scene_Unknown";
            }
        }

        return $"[{mapName}]";
    }

    private ArchivedEncounterRecord? ArchiveEncounter(SceneCombatSnapshot snapshot, string trigger, bool isAutomatic)
        => _encounterArchiveService.Archive(SceneArchivePayload.Create(_captureService.Scene.Owner, snapshot), trigger, isAutomatic);

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

        if (latestLiveSnapshot.MapId == 0 || !HasArchivableEncounter(previousLiveSnapshot))
        {
            return false;
        }

        if (previousLiveSnapshot.MapId != latestLiveSnapshot.MapId)
        {
            reason = "map-transition";
            return true;
        }

        if (previousLiveSnapshot.MapInstanceId != latestLiveSnapshot.MapInstanceId)
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

    private void RefreshCombatantDetails(bool forceRefresh = false)
    {
        if (SelectedCombatant is null)
        {
            CombatantDetails.Clear();
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
            var detail = _captureService.Scene.Owner.CreateDetailDelta(snapshot, SelectedCombatant.Id, forceRefresh);
            CombatantDetails.SelectSceneEncounterCombatant(encounterContextId, SelectedCombatant.Id, snapshot, detail, forceRefresh);
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
