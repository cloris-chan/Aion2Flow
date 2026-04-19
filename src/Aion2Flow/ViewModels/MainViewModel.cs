using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
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

    private readonly WinDivertCaptureService _captureService;
    private readonly CombatMetricsEngine _engine;
    private readonly CombatMetricsStore _store;
    private readonly ProcessForegroundWatcher _processForegroundWatcher;
    private readonly LanguageService _languageService;
    private readonly GameResourceService _gameResourceService;
    private readonly BattleArchiveService _battleArchiveService;
    private readonly CombatantDetailsFlyoutViewModel _combatantDetails;

    private PeriodicTimer? _refreshTimer;
    private Task? _refreshTask;
    private DamageMeterSnapshot _latestLiveDamage = new();
    private DamageMeterSnapshot _displayedSnapshot = new();
    private volatile bool _suppressRefresh;
    private bool _isDisposed;

    public LocalizationService Localization { get; }
    public CombatantDetailsFlyoutViewModel CombatantDetails => _combatantDetails;
    public KeyedObservableCollection<int, CombatantRowViewModel> Combatants { get; } = new(x => x.Id);
    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<BattleHistoryItemViewModel> BattleHistory { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int RoundTripTimeMilliseconds { get; set; }

    [ObservableProperty]
    public partial double BattleTimeSeconds { get; set; }

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
    public partial int MaxVisibleCombatantRows { get; set; } = 4;

    [ObservableProperty]
    public partial bool IsTopMost { get; private set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial BattleHistoryItemViewModel? SelectedBattleHistory { get; set; }

    [ObservableProperty]
    public partial bool IsViewingArchivedBattle { get; set; }

    [ObservableProperty]
    public partial bool HasArchivedBattles { get; set; }

    public MainViewModel(
        WinDivertCaptureService captureService,
        CombatMetricsEngine engine,
        CombatMetricsStore store,
        ProcessForegroundWatcher processForegroundWatcher,
        LanguageService languageService,
        GameResourceService gameResourceService,
        BattleArchiveService battleArchiveService,
        CombatantDetailsFlyoutViewModel combatantDetails,
        LocalizationService localization)
    {
        _captureService = captureService;
        _engine = engine;
        _store = store;
        _processForegroundWatcher = processForegroundWatcher;
        _languageService = languageService;
        _gameResourceService = gameResourceService;
        _battleArchiveService = battleArchiveService;
        _combatantDetails = combatantDetails;
        Localization = localization;

        _captureService.StatusChanged += OnCaptureStatusChanged;
        _captureService.RttResolved += OnRttResolved;
        _processForegroundWatcher.ForegroundChanged += OnForegroundChanged;
        _languageService.LanguageChanged += OnLanguageChanged;
        _gameResourceService.ResourcesChanged += OnResourcesChanged;
        _battleArchiveService.HistoryChanged += OnBattleHistoryChanged;

        RebuildLanguageOptions();
        RebuildBattleHistory();
        ApplyLocalizedUiText();
        RefreshCaptureIndicators();
        SelectedLanguage = Languages.FirstOrDefault(x => string.Equals(x.Code, _languageService.CurrentLanguage, StringComparison.Ordinal));
    }

    public Task InitializeAsync() => StartCaptureAsync();

    private void OnRttResolved(double rtt)
    {
        Dispatcher.UIThread.Post(RefreshCaptureIndicators);
    }

    private void OnForegroundChanged(bool isTopMost)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsTopMost = isTopMost;
        });
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
            RebuildLanguageOptions();
            RebuildBattleHistory();
            ApplyLocalizedUiText();
            RefreshCaptureIndicators();
            RefreshDisplayedSnapshot();
        });
    }

    private void OnResourcesChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RebuildBattleHistory();
            RefreshDisplayedSnapshot();
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
            _engine.Reset();
            RawPacketDump.RotateLogs();

            _latestLiveDamage = new DamageMeterSnapshot();
            _displayedSnapshot = new DamageMeterSnapshot();
            Combatants.Clear();
            CombatantDetails.Clear();
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

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
        }
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
        var nextLiveSnapshot = _engine.CreateBattleSnapshot();
        if (TryAutoArchive(previousLiveSnapshot, nextLiveSnapshot))
        {
            nextLiveSnapshot = _engine.CreateBattleSnapshot();
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

    private void ApplySnapshot(DamageMeterSnapshot snapshot)
    {
        var battleSeconds = snapshot.BattleTime / 1000.0;
        BattleTimeSeconds = battleSeconds;

        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (snapshot.Combatants.TryGetValue(row.Id, out var data))
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

            if (data.CharacterClass is null)
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

        RefreshCombatantDetails();
    }

    private void RefreshDisplayedSnapshot()
    {
        if (IsViewingArchivedBattle && SelectedBattleHistory is not null)
        {
            _displayedSnapshot = SelectedBattleHistory.Record.Snapshot;
            ApplySnapshot(_displayedSnapshot);
            return;
        }

        _displayedSnapshot = _latestLiveDamage;
        ApplySnapshot(_displayedSnapshot);
    }

    private string ResolveDisplayName(DamageMeterSnapshot snapshot, int id)
    {
        var store = IsViewingArchivedBattle && SelectedBattleHistory is not null
            ? SelectedBattleHistory.Record.Store
            : _store;
        return CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, id);
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
        _processForegroundWatcher.ForegroundChanged -= OnForegroundChanged;
        _languageService.LanguageChanged -= OnLanguageChanged;
        _gameResourceService.ResourcesChanged -= OnResourcesChanged;
        _battleArchiveService.HistoryChanged -= OnBattleHistoryChanged;
        await StopCaptureAsync().ConfigureAwait(false);
        _processForegroundWatcher.Dispose();
    }

    private void ApplyLocalizedUiText()
    {
        Status = Localization["Status.Ready"];
        BattleTimeSeconds = 0d;
        RoundTripTimeMilliseconds = 0;
    }

    private void RebuildLanguageOptions()
    {
        var selectedCode = SelectedLanguage?.Code ?? _languageService.CurrentLanguage;
        Languages.Clear();
        Languages.Add(new LanguageOption(LanguageService.TraditionalChinese, "繁體中文", ResolveIconGeometry("language-zh")));
        Languages.Add(new LanguageOption(LanguageService.English, "English", ResolveIconGeometry("language-en")));
        Languages.Add(new LanguageOption(LanguageService.Korean, "한국어", ResolveIconGeometry("language-ko")));
        SelectedLanguage = Languages.FirstOrDefault(x => x.Code == selectedCode) ?? Languages.FirstOrDefault();
    }

    private void RebuildBattleHistory()
    {
        var selectedId = SelectedBattleHistory?.Record.Id;
        BattleHistory.Clear();
        foreach (var record in _battleArchiveService.History)
        {
            BattleHistory.Add(new BattleHistoryItemViewModel(record, record.DisplayName));
        }

        HasArchivedBattles = BattleHistory.Count > 0;
        SelectedBattleHistory = BattleHistory.FirstOrDefault(x => x.Record.Id == selectedId);
    }

    private ArchivedBattleRecord? ArchiveSnapshot(DamageMeterSnapshot snapshot, string trigger, bool isAutomatic)
    {
        return _battleArchiveService.Archive(snapshot, _store, trigger, isAutomatic);
    }

    private bool TryAutoArchive(DamageMeterSnapshot previousLiveSnapshot, DamageMeterSnapshot latestLiveSnapshot)
    {
        if (previousLiveSnapshot.BattleTime <= 0 || previousLiveSnapshot.Combatants.Count == 0)
        {
            return false;
        }

        if (latestLiveSnapshot.Encounter.ShouldArchive && previousLiveSnapshot.Encounter.IsActive)
        {
            ArchiveSnapshot(previousLiveSnapshot, latestLiveSnapshot.Encounter.Reason, isAutomatic: true);
            _engine.Reset();
            return true;
        }

        return false;
    }

    private void RefreshCombatantDetails()
    {
        if (SelectedCombatant is null)
        {
            CombatantDetails.Clear();
            return;
        }

        var battleContextId = IsViewingArchivedBattle
            ? SelectedBattleHistory?.Record.BattleId ?? Guid.Empty
            : _displayedSnapshot.BattleId;

        CombatantDetails.SelectBattleCombatant(battleContextId, SelectedCombatant.Id);
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
                ? Localization["Status.DriverReady"]
                : Localization["Status.DriverIdle"];

        if (!_captureService.IsPortMonitoringActive)
        {
            GamePortIndicatorColor = IndicatorIdleColor;
            GamePortIndicatorToolTip = Localization["Status.PortIdle"];
        }
        else if (_captureService.HasTrackedGamePorts)
        {
            GamePortIndicatorColor = IndicatorOkColor;
            GamePortIndicatorToolTip = Localization["Status.PortReady"];
        }
        else
        {
            GamePortIndicatorColor = IndicatorWarnColor;
            GamePortIndicatorToolTip = Localization["Status.PortWaiting"];
        }

        var isCaptureLocked = CaptureConnectionGate.IsLocked;
        var isProxied = CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection) && lockedConnection.IsLocalNetwork;
        if (!isCaptureLocked)
        {
            RoundTripTimeMilliseconds = 0;
            CaptureLockIndicatorColor = IndicatorIdleColor;
            CaptureLockIndicatorToolTip = Localization["Status.Unlocked"];
        }
        else if (isProxied)
        {
            CaptureLockIndicatorColor = IndicatorWarnColor;
            CaptureLockIndicatorToolTip = Localization["Status.LockedProxy"];
        }
        else
        {
            CaptureLockIndicatorColor = IndicatorOkColor;
            CaptureLockIndicatorToolTip = Localization["Status.Locked"];
        }

        var currentRttMilliseconds = _captureService.CurrentRoundTripTimeMilliseconds;
        if (!currentRttMilliseconds.HasValue || currentRttMilliseconds.Value <= 0)
        {
            RoundTripTimeMilliseconds = 0;
            LatencyIndicatorColor = IndicatorIdleColor;
            LatencyToolTip = Localization["Status.RttUnavailable"];
            return;
        }

        RoundTripTimeMilliseconds = Math.Max(1, (int)Math.Round(currentRttMilliseconds.Value));
        if (isProxied)
        {
            LatencyIndicatorColor = IndicatorWarnColor;
            LatencyToolTip = Localization["Status.RttLoopbackProtocol"];
        }
        else
        {
            LatencyIndicatorColor = IndicatorInfoColor;
            LatencyToolTip = Localization["Status.RttEstimated"];
        }
    }

    private static Geometry? ResolveIconGeometry(string resourceKey)
    {
        if (Application.Current is null)
        {
            return null;
        }

        return Application.Current.Resources.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) &&
               resource is Geometry geometry
            ? geometry
            : null;
    }
}
