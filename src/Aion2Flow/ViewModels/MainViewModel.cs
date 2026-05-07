using System.Collections.ObjectModel;
using Avalonia.Threading;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.Services.Settings;
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
    private readonly CombatMetricsEngine _engine;
    private readonly CombatMetricsStore _store;
    private readonly LanguageService _languageService;
    private readonly GameResourceService _gameResourceService;
    private readonly BattleArchiveService _battleArchiveService;
    private readonly CombatantDetailsFlyoutViewModel _combatantDetails;
    private readonly SettingsService _settingsService;

    private PeriodicTimer? _refreshTimer;
    private Task? _refreshTask;
    private DamageMeterSnapshot _latestLiveDamage = new();
    private DamageMeterSnapshot _displayedSnapshot = new();
    private volatile bool _suppressRefresh;
    private bool _isDisposed;

    public LocalizationService Localization { get; }
    public CombatantDetailsFlyoutViewModel CombatantDetails => _combatantDetails;
    public SettingsFlyoutViewModel SettingsFlyout { get; }
    public ObservableCollection<BossFocusViewModel> BossFocuses { get; } = [];
    public KeyedObservableCollection<int, CombatantRowViewModel> Combatants { get; } = new(x => x.Id);
    public ObservableCollection<BattleHistoryItemViewModel> BattleHistory { get; } = [];
    internal string LastSceneSnapshotDiff { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int RoundTripTimeMilliseconds { get; set; }

    [ObservableProperty]
    public partial double BattleTimeSeconds { get; set; }

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
    public partial BattleHistoryItemViewModel? SelectedBattleHistory { get; set; }

    [ObservableProperty]
    public partial bool IsViewingArchivedBattle { get; set; }

    [ObservableProperty]
    public partial bool HasArchivedBattles { get; set; }

    public MainViewModel(
        WinDivertCaptureService captureService,
        ProcessPortDiscoveryService processPortDiscoveryService,
        CombatMetricsEngine engine,
        CombatMetricsStore store,
        LanguageService languageService,
        GameResourceService gameResourceService,
        BattleArchiveService battleArchiveService,
        CombatantDetailsFlyoutViewModel combatantDetails,
        LocalizationService localization,
        SettingsService settingsService,
        SettingsFlyoutViewModel settingsFlyout)
    {
        _captureService = captureService;
        _processPortDiscoveryService = processPortDiscoveryService;
        _engine = engine;
        _store = store;
        _languageService = languageService;
        _gameResourceService = gameResourceService;
        _battleArchiveService = battleArchiveService;
        _combatantDetails = combatantDetails;
        _settingsService = settingsService;
        Localization = localization;
        SettingsFlyout = settingsFlyout;

        _captureService.StatusChanged += OnCaptureStatusChanged;
        _captureService.RttResolved += OnRttResolved;
        _languageService.LanguageChanged += OnLanguageChanged;
        _gameResourceService.ResourcesChanged += OnResourcesChanged;
        _battleArchiveService.HistoryChanged += OnBattleHistoryChanged;

        RebuildBattleHistory();
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
            RebuildBattleHistory();
            ApplyLocalizedUiText();
            RefreshCaptureIndicators();
            RefreshDisplayedSnapshot(forceDetailRefresh: true);
        });
    }

    private void OnResourcesChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RebuildBattleHistory();
            RefreshDisplayedSnapshot(forceDetailRefresh: true);
        });
    }

    private void OnBattleHistoryChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RebuildBattleHistory);
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
            ArchiveSnapshot(_latestLiveDamage, "manual-reset", isAutomatic: true);
            ResetLiveModels();
            RawPacketDump.RotateLogs();

            _latestLiveDamage = new DamageMeterSnapshot();
            _displayedSnapshot = new DamageMeterSnapshot();
            Combatants.Clear();
            CombatantDetails.Clear();
            BossFocuses.Clear();
            SelectedCombatant = null;
            SelectedBattleHistory = null;
            IsViewingArchivedBattle = false;
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

    partial void OnSelectedBattleHistoryChanged(BattleHistoryItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        IsViewingArchivedBattle = true;
        _displayedSnapshot = value.Record.Snapshot;
        ApplySnapshot(_displayedSnapshot);
    }

    [RelayCommand]
    private void ArchiveCurrentBattle()
    {
        var record = ArchiveSnapshot(_latestLiveDamage, "manual", isAutomatic: false);
        if (record is null)
        {
            return;
        }

        RebuildBattleHistory();
        SelectedBattleHistory = BattleHistory.FirstOrDefault(x => x.Record.Id == record.Id);
    }

    [RelayCommand]
    private void ReturnToLive()
    {
        IsViewingArchivedBattle = false;
        SelectedBattleHistory = null;
        ApplySnapshot(_latestLiveDamage);
    }

    private void RefreshCombatStats()
    {
        if (_suppressRefresh)
        {
            return;
        }

        var previousLiveSnapshot = _latestLiveDamage;
        var nextLiveSnapshot = CreateLiveSnapshot();
        if (TryAutoResetBattle(previousLiveSnapshot, nextLiveSnapshot))
        {
            nextLiveSnapshot = CreateLiveSnapshot();
        }

        _latestLiveDamage = nextLiveSnapshot;
        RefreshCaptureIndicators();

        if (IsViewingArchivedBattle)
        {
            return;
        }

        _displayedSnapshot = _latestLiveDamage;
        ApplySnapshot(_displayedSnapshot);
    }

    internal void RefreshCombatStatsForTesting() => RefreshCombatStats();

    private DamageMeterSnapshot CreateLiveSnapshot()
    {
        return _settingsService.Current.SceneSnapshotReadMode switch
        {
            SceneSnapshotReadMode.Scene => _captureService.Scene.Owner.CreateSnapshot(),
            SceneSnapshotReadMode.Both => CreateBothLiveSnapshot(),
            _ => _engine.CreateBattleSnapshot()
        };
    }

    private DamageMeterSnapshot CreateBothLiveSnapshot()
    {
        var legacy = _engine.CreateBattleSnapshot();
        var scene = _captureService.Scene.Owner.CreateSnapshot();
        RecordSceneSnapshotDiff(legacy, scene);
        return legacy;
    }

    private void RecordSceneSnapshotDiff(DamageMeterSnapshot legacy, DamageMeterSnapshot scene)
    {
        LastSceneSnapshotDiff = BuildSceneSnapshotDiff(legacy, scene);
        if (!string.IsNullOrEmpty(LastSceneSnapshotDiff))
            AppLog.Write(AppLogLevel.Debug, $"Scene snapshot diff: {LastSceneSnapshotDiff}");
    }

    private void ApplySnapshot(DamageMeterSnapshot snapshot, bool forceDetailRefresh = false)
    {
        var battleSeconds = snapshot.BattleTime / 1000.0;
        BattleTimeSeconds = battleSeconds;
        LiveSceneName = ResolveSceneDisplayName(snapshot.MapId);
        var displayStore = ResolveDisplayStore();
        RefreshBossFocus(displayStore);

        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (snapshot.Combatants.TryGetValue(row.Id, out var data) &&
                ShouldDisplayCombatant(displayStore, row.Id, data))
            {
                row.DisplayName = ResolveDisplayName(snapshot, row.Id);
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

            if (!ShouldDisplayCombatant(displayStore, id, data))
                continue;
            var displayName = ResolveDisplayName(snapshot, id);

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
        if (IsViewingArchivedBattle && SelectedBattleHistory is not null)
        {
            _displayedSnapshot = SelectedBattleHistory.Record.Snapshot;
            ApplySnapshot(_displayedSnapshot, forceDetailRefresh);
            return;
        }

        _displayedSnapshot = _latestLiveDamage;
        ApplySnapshot(_displayedSnapshot, forceDetailRefresh);
    }

    private string ResolveDisplayName(DamageMeterSnapshot snapshot, int id)
    {
        return CombatMetricsEngine.ResolveCombatantDisplayName(ResolveDisplayStore(), snapshot, id);
    }

    private CombatMetricsStore ResolveDisplayStore()
        => IsViewingArchivedBattle && SelectedBattleHistory is not null
            ? SelectedBattleHistory.Record.Store
            : _store;

    private void RefreshBossFocus(CombatMetricsStore displayStore)
    {
        if (IsViewingArchivedBattle || displayStore != _store)
        {
            BossFocuses.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshots = _store.GetObservedBosses(now, BossFocusVisibilityTimeoutMilliseconds);
        SyncBossFocuses(snapshots);
    }

    private void SyncBossFocuses(IReadOnlyList<CombatMetricsStore.ObservedBossSnapshot> snapshots)
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
            BossFocusViewModel? row = null;
            for (var j = 0; j < BossFocuses.Count; j++)
            {
                if (BossFocuses[j].InstanceId == snapshot.InstanceId)
                {
                    row = BossFocuses[j];
                    break;
                }
            }

            var displayName = CombatMetricsEngine.ResolveCombatantDisplayName(_store, _latestLiveDamage, snapshot.InstanceId);
            if (row is null)
            {
                row = new BossFocusViewModel { InstanceId = snapshot.InstanceId };
                BossFocuses.Add(row);
            }
            row.Update(displayName, snapshot.Hp, snapshot.MaxHp, snapshot.HasHp);
        }
    }

    internal static bool ShouldDisplayCombatant(CombatMetricsStore store, int combatantId, CombatantMetrics data)
    {
        if (data.CharacterClass is null)
        {
            return false;
        }

        if (!store.TryGetNpcRuntimeState(combatantId, out var npcState))
        {
            return true;
        }

        if (npcState.NpcCode.HasValue)
        {
            return false;
        }

        return npcState.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon);
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
        _battleArchiveService.HistoryChanged -= OnBattleHistoryChanged;
        await StopCaptureAsync().ConfigureAwait(false);
        await _processPortDiscoveryService.DisposeAsync().ConfigureAwait(false);
    }

    private void ApplyLocalizedUiText()
    {
        Status = Localization["Status_Ready"];
        BattleTimeSeconds = 0d;
        RoundTripTimeMilliseconds = 0;
        LiveSceneName = ResolveSceneDisplayName(_displayedSnapshot.MapId);
    }

    private void RebuildBattleHistory()
    {
        var selectedId = SelectedBattleHistory?.Record.Id;
        BattleHistory.Clear();
        foreach (var record in _battleArchiveService.History)
        {
            BattleHistory.Add(new BattleHistoryItemViewModel(record, BuildHistoryDisplayName(record)));
        }

        HasArchivedBattles = BattleHistory.Count > 0;
        SelectedBattleHistory = BattleHistory.FirstOrDefault(x => x.Record.Id == selectedId);
    }

    private string BuildHistoryDisplayName(ArchivedBattleRecord record)
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

    private ArchivedBattleRecord? ArchiveSnapshot(DamageMeterSnapshot snapshot, string trigger, bool isAutomatic)
    {
        return _battleArchiveService.Archive(snapshot, _store, trigger, isAutomatic);
    }

    private bool TryAutoResetBattle(DamageMeterSnapshot previousLiveSnapshot, DamageMeterSnapshot latestLiveSnapshot)
    {
        if (TryResolveMapTransitionResetReason(previousLiveSnapshot, latestLiveSnapshot, out var mapTransitionReason))
        {
            ArchiveSnapshot(previousLiveSnapshot, mapTransitionReason, isAutomatic: true);
            ResetLiveModels();
            RawPacketDump.RotateLogs();
            return true;
        }

        return false;
    }

    internal static bool TryResolveMapTransitionResetReason(
        DamageMeterSnapshot previousLiveSnapshot,
        DamageMeterSnapshot latestLiveSnapshot,
        out string reason)
    {
        reason = string.Empty;

        if (latestLiveSnapshot.MapId == 0 || !HasArchivableBattle(previousLiveSnapshot))
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

    private static bool HasArchivableBattle(DamageMeterSnapshot snapshot)
        => snapshot.BattleTime > 0 && snapshot.Combatants.Count > 0;

    private void ResetLiveModels()
    {
        _engine.Reset();
        _captureService.Scene.Reset();
        LastSceneSnapshotDiff = string.Empty;
    }

    internal static string BuildSceneSnapshotDiff(DamageMeterSnapshot legacy, DamageMeterSnapshot scene)
    {
        var diffs = new List<string>();
        AppendDiff(diffs, "battleTime", legacy.BattleTime, scene.BattleTime);
        AppendDiff(diffs, "battleStart", legacy.BattleStartTime, scene.BattleStartTime);
        AppendDiff(diffs, "battleEnd", legacy.BattleEndTime, scene.BattleEndTime);
        AppendDiff(diffs, "mapId", legacy.MapId, scene.MapId);
        AppendDiff(diffs, "mapInstanceId", legacy.MapInstanceId, scene.MapInstanceId);
        AppendDiff(diffs, "target", legacy.Encounter.TrackingTargetId, scene.Encounter.TrackingTargetId);

        foreach (var id in legacy.Combatants.Keys.Concat(scene.Combatants.Keys).Distinct().Order())
        {
            legacy.Combatants.TryGetValue(id, out var l);
            scene.Combatants.TryGetValue(id, out var s);
            AppendDiff(diffs, $"combatant:{id}:damage", l?.DamageAmount ?? 0, s?.DamageAmount ?? 0);
            AppendDiff(diffs, $"combatant:{id}:healing", l?.HealingAmount ?? 0, s?.HealingAmount ?? 0);
            AppendDiff(diffs, $"combatant:{id}:shieldAbsorbed", l?.ShieldAbsorbedAmount ?? 0, s?.ShieldAbsorbedAmount ?? 0);
            if (l?.CharacterClass != s?.CharacterClass)
                diffs.Add($"combatant:{id}:class legacy={l?.CharacterClass?.ToString() ?? "<null>"} scene={s?.CharacterClass?.ToString() ?? "<null>"}");
        }

        return string.Join("; ", diffs);
    }

    private static void AppendDiff(List<string> diffs, string field, long legacy, long scene)
    {
        if (legacy != scene)
            diffs.Add($"{field} legacy={legacy} scene={scene}");
    }

    private void RefreshCombatantDetails(bool forceRefresh = false)
    {
        if (SelectedCombatant is null)
        {
            CombatantDetails.Clear();
            return;
        }

        var battleContextId = IsViewingArchivedBattle
            ? SelectedBattleHistory?.Record.BattleId ?? Guid.Empty
            : _displayedSnapshot.BattleId;

        var snapshot = IsViewingArchivedBattle && SelectedBattleHistory is not null
            ? SelectedBattleHistory.Record.Snapshot
            : _displayedSnapshot;

        var store = IsViewingArchivedBattle && SelectedBattleHistory is not null
            ? SelectedBattleHistory.Record.Store
            : _store;

        if (!IsViewingArchivedBattle && _settingsService.Current.SceneSnapshotReadMode == SceneSnapshotReadMode.Scene)
        {
            var detail = _captureService.Scene.Owner.CreateDetailDelta(snapshot, SelectedCombatant.Id);
            CombatantDetails.SelectSceneBattleCombatant(battleContextId, SelectedCombatant.Id, snapshot, detail, forceRefresh);
            return;
        }

        CombatantDetails.SelectBattleCombatant(battleContextId, SelectedCombatant.Id, snapshot, store, forceRefresh);
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

