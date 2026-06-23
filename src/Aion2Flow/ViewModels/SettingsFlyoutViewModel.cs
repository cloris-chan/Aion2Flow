using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SettingsFlyoutViewModel : ObservableObject
{
    private readonly LanguageService _languageService;
    private readonly SettingsService _settingsService;
    private readonly PlayerNameDisplayService _playerNameDisplay;
    private readonly UiScaleService _uiScale;
    private readonly AppUpdateService _updateService;
    private readonly ProcessForegroundWatcher _processForegroundWatcher;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly bool _isApplyingPersistedSettings;

    public SettingsFlyoutViewModel(LocalizationService localization, LanguageService languageService, SettingsService settingsService, PlayerNameDisplayService playerNameDisplay, UiScaleService uiScale, AppUpdateService updateService, ProcessForegroundWatcher processForegroundWatcher, GlobalHotkeyService globalHotkeyService)
    {
        Localization = localization;
        _languageService = languageService;
        _settingsService = settingsService;
        _playerNameDisplay = playerNameDisplay;
        _uiScale = uiScale;
        _updateService = updateService;
        _processForegroundWatcher = processForegroundWatcher;
        _globalHotkeyService = globalHotkeyService;

        var persisted = _settingsService.Current;
        if (!string.IsNullOrWhiteSpace(persisted.Language))
        {
            _languageService.SetLanguage(persisted.Language);
        }

        _isApplyingPersistedSettings = true;
        try
        {
            TopmostMode = persisted.TopmostMode;
            MaxVisibleCombatantRows = persisted.MaxVisibleCombatantRows;
            CombatantSortMetric = persisted.CombatantSortMetric;
            SceneKind = persisted.SceneKind;
            UseCompactMainMetrics = persisted.UseCompactMainMetrics;
            ShowFocusStatusBar = persisted.ShowFocusStatusBar;
            ShowPlayerNames = persisted.ShowPlayerNames;
            PlayerSelfMarkerDisplayMode = persisted.PlayerSelfMarkerDisplayMode;
            ShowPlayerShortServerName = persisted.ShowPlayerShortServerName;
            ShowPlayerLegionName = persisted.ShowPlayerLegionName;
            TintPlayerNamesByFaction = persisted.TintPlayerNamesByFaction;
            UiScalePercent = persisted.UiScalePercent;
            if (persisted.BattleResetHotkeyVirtualKey is { } vk && persisted.BattleResetHotkeyModifiers is { } mods)
            {
                BattleResetHotkey = new HotkeyDefinition((HotkeyModifiers)mods, vk);
            }
        }
        finally
        {
            _isApplyingPersistedSettings = false;
        }

        _globalHotkeyService.SetHotkey(BattleResetHotkey);

        RebuildLanguageOptions();
        SelectedLanguage = Languages.FirstOrDefault(x => string.Equals(x.Code, _languageService.CurrentLanguage, StringComparison.Ordinal));

        _languageService.LanguageChanged += OnLanguageServiceLanguageChanged;
        _processForegroundWatcher.ForegroundChanged += OnForegroundChanged;
        _updateService.PropertyChanged += OnUpdatePropertyChanged;
        Localization.LanguageChanged += OnLocalizationLanguageChanged;
    }

    public LocalizationService Localization { get; }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public IReadOnlyList<TopmostMode> TopmostModeOptions { get; } = [TopmostMode.GameForeground, TopmostMode.Always, TopmostMode.Never];

    public IReadOnlyList<int> RowCountOptions { get; } = [3, 4, 5, 6];

    public IReadOnlyList<CombatantSortMetric> CombatantSortMetricOptions { get; } = [CombatantSortMetric.DamagePerSecond, CombatantSortMetric.TotalDamage];

    public IReadOnlyList<SceneKind> SceneKindOptions { get; } = [SceneKind.Standard, SceneKind.Boss];

    public IReadOnlyList<PlayerSelfMarkerDisplayMode> PlayerSelfMarkerDisplayModeOptions { get; } = [PlayerSelfMarkerDisplayMode.Always, PlayerSelfMarkerDisplayMode.WhenNamesHidden, PlayerSelfMarkerDisplayMode.Hidden];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlwaysOnTop))]
    [NotifyPropertyChangedFor(nameof(TopmostModeDisplay))]
    public partial TopmostMode TopmostMode { get; set; } = TopmostMode.GameForeground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlwaysOnTop))]
    public partial bool IsTopMost { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxVisibleCombatantRowsDisplay))]
    public partial int MaxVisibleCombatantRows { get; set; } = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CombatantSortMetricDisplay))]
    public partial CombatantSortMetric CombatantSortMetric { get; set; } = CombatantSortMetric.DamagePerSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SceneKindDisplay))]
    public partial SceneKind SceneKind { get; set; } = SceneKind.Standard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCompactMainMetricsDisplay))]
    public partial bool UseCompactMainMetrics { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFocusStatusBarDisplay))]
    public partial bool ShowFocusStatusBar { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayerNamesDisplay))]
    [NotifyPropertyChangedFor(nameof(PlayerNameSettingsDisplay))]
    public partial bool ShowPlayerNames { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayerSelfMarkerDisplayModeDisplay))]
    public partial PlayerSelfMarkerDisplayMode PlayerSelfMarkerDisplayMode { get; set; } = PlayerSelfMarkerDisplayMode.WhenNamesHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayerShortServerNameDisplay))]
    public partial bool ShowPlayerShortServerName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayerLegionNameDisplay))]
    public partial bool ShowPlayerLegionName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TintPlayerNamesByFactionDisplay))]
    public partial bool TintPlayerNamesByFaction { get; set; }

    [ObservableProperty]
    public partial int UiScalePercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageDisplay))]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetHotkeyDisplay))]
    [NotifyPropertyChangedFor(nameof(HasResetHotkey))]
    public partial HotkeyDefinition? BattleResetHotkey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetHotkeyDisplay))]
    public partial bool IsCapturingResetHotkey { get; set; }

    public string ResetHotkeyDisplay
    {
        get
        {
            if (IsCapturingResetHotkey)
            {
                return Localization["Settings_Hotkey_PressKeys"];
            }
            return BattleResetHotkey?.Display ?? Localization["Settings_Hotkey_None"];
        }
    }

    public bool HasResetHotkey => BattleResetHotkey is not null;

    public bool IsAlwaysOnTop => TopmostMode switch
    {
        TopmostMode.Always => true,
        TopmostMode.Never => false,
        _ => IsTopMost
    };

    public string TopmostModeDisplay => Localization[$"Settings_Topmost_{TopmostMode}"];

    public string MaxVisibleCombatantRowsDisplay => MaxVisibleCombatantRows.ToString();

    public string CombatantSortMetricDisplay => Localization[$"Settings_CombatantSortMetric_{CombatantSortMetric}"];

    public string SceneKindDisplay => Localization[$"Settings_SceneKind_{SceneKind}"];

    public string UseCompactMainMetricsDisplay => Localization[UseCompactMainMetrics ? "Settings_MainMetricsCompact_On" : "Settings_MainMetricsCompact_Off"];

    public string ShowFocusStatusBarDisplay => Localization[ShowFocusStatusBar ? "Settings_FocusStatusBar_On" : "Settings_FocusStatusBar_Off"];

    public string ShowPlayerNamesDisplay => Localization[ShowPlayerNames ? "Settings_ShowPlayerNames_On" : "Settings_ShowPlayerNames_Off"];

    public string PlayerSelfMarkerDisplayModeDisplay => Localization[$"Settings_PlayerSelfMarker_{PlayerSelfMarkerDisplayMode}"];

    public string ShowPlayerShortServerNameDisplay => Localization[ShowPlayerShortServerName ? "Settings_PlayerShortServerName_On" : "Settings_PlayerShortServerName_Off"];

    public string ShowPlayerLegionNameDisplay => Localization[ShowPlayerLegionName ? "Settings_PlayerLegionName_On" : "Settings_PlayerLegionName_Off"];

    public string TintPlayerNamesByFactionDisplay => Localization[TintPlayerNamesByFaction ? "Settings_PlayerFactionTint_On" : "Settings_PlayerFactionTint_Off"];

    public string PlayerNameSettingsDisplay => ShowPlayerNamesDisplay;

    public string LanguageDisplay => SelectedLanguage?.DisplayName ?? string.Empty;

    public bool IsUpdateSectionVisible => _updateService.IsManagedByVelopack;

    public string? CurrentVersion => _updateService.CurrentVersion;

    public AppUpdateState UpdateState => _updateService.State;

    public int DownloadProgress => _updateService.DownloadProgress;

    public string? AvailableVersion => _updateService.AvailableVersion;

    public string? UpdateStatusMessage => _updateService.StatusMessage;

    public bool IsCheckingUpdate => UpdateState == AppUpdateState.Checking;

    public bool IsDownloadingUpdate => UpdateState == AppUpdateState.Downloading;

    public bool CanCheckForUpdates => UpdateState is AppUpdateState.Idle or AppUpdateState.UpToDate or AppUpdateState.Failed;

    public bool CanRestartToUpdate => UpdateState == AppUpdateState.ReadyToRestart;

    public string UpdateStatusText => UpdateState switch
    {
        AppUpdateState.Checking => Localization["Settings_Update_Checking"],
        AppUpdateState.Downloading => string.Format(Localization["Settings_Update_DownloadingFormat"], DownloadProgress),
        AppUpdateState.UpToDate => Localization["Settings_Update_UpToDate"],
        AppUpdateState.ReadyToRestart => string.Format(Localization["Settings_Update_ReadyFormat"], AvailableVersion ?? string.Empty),
        AppUpdateState.Failed => Localization["Settings_Update_Failed"],
        _ => string.Empty
    };

    public string CurrentVersionText
    {
        get
        {
            var version = CurrentVersion;
            return string.IsNullOrWhiteSpace(version)
                ? string.Empty
                : string.Format(Localization["Settings_Update_CurrentVersionFormat"], version);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private void CheckForUpdates() => _updateService.CheckForUpdates();

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private Task RestartAsync() => _updateService.RestartAsync();

    partial void OnTopmostModeChanged(TopmostMode value) => PersistSettings();

    partial void OnMaxVisibleCombatantRowsChanged(int value) => PersistSettings();

    partial void OnCombatantSortMetricChanged(CombatantSortMetric value) => PersistSettings();

    partial void OnSceneKindChanged(SceneKind value) => PersistSettings();

    partial void OnUseCompactMainMetricsChanged(bool value) => PersistSettings();

    partial void OnShowFocusStatusBarChanged(bool value) => PersistSettings();

    partial void OnShowPlayerNamesChanged(bool value)
    {
        _playerNameDisplay.ShowPlayerNames = value;
        PersistSettings();
    }

    partial void OnPlayerSelfMarkerDisplayModeChanged(PlayerSelfMarkerDisplayMode value)
    {
        _playerNameDisplay.SelfMarkerDisplayMode = value;
        PersistSettings();
    }

    partial void OnShowPlayerShortServerNameChanged(bool value)
    {
        _playerNameDisplay.ShowShortServerName = value;
        PersistSettings();
    }

    partial void OnShowPlayerLegionNameChanged(bool value)
    {
        _playerNameDisplay.ShowLegionName = value;
        PersistSettings();
    }

    partial void OnTintPlayerNamesByFactionChanged(bool value)
    {
        _playerNameDisplay.TintPlayerNamesByFaction = value;
        PersistSettings();
    }

    partial void OnUiScalePercentChanged(int value)
    {
        _uiScale.SetScalePercent(value);
        PersistSettings();
    }

    public void SetUiScalePercent(int percent)
    {
        if (UiScalePercent != percent)
            UiScalePercent = percent;
        else
            _uiScale.SetScalePercent(percent);
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
            PersistSettings();
        }
    }

    partial void OnBattleResetHotkeyChanged(HotkeyDefinition? value)
    {
        _globalHotkeyService.SetHotkey(value);
        PersistSettings();
    }

    [RelayCommand]
    private void BeginCaptureBattleResetHotkey() => IsCapturingResetHotkey = true;

    [RelayCommand]
    private void CancelCaptureBattleResetHotkey() => IsCapturingResetHotkey = false;

    [RelayCommand]
    private void ClearBattleResetHotkey()
    {
        IsCapturingResetHotkey = false;
        BattleResetHotkey = null;
    }

    public void ApplyCapturedHotkey(HotkeyDefinition definition)
    {
        IsCapturingResetHotkey = false;
        BattleResetHotkey = definition;
    }

    private void PersistSettings()
    {
        if (_isApplyingPersistedSettings)
        {
            return;
        }

        _settingsService.Update(s =>
        {
            s.TopmostMode = TopmostMode;
            s.MaxVisibleCombatantRows = MaxVisibleCombatantRows;
            s.CombatantSortMetric = CombatantSortMetric;
            s.SceneKind = SceneKind;
            s.UseCompactMainMetrics = UseCompactMainMetrics;
            s.ShowFocusStatusBar = ShowFocusStatusBar;
            s.ShowPlayerNames = ShowPlayerNames;
            s.PlayerSelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode;
            s.ShowPlayerShortServerName = ShowPlayerShortServerName;
            s.ShowPlayerLegionName = ShowPlayerLegionName;
            s.TintPlayerNamesByFaction = TintPlayerNamesByFaction;
            s.UiScalePercent = UiScalePercent;
            s.Language = SelectedLanguage?.Code ?? _languageService.CurrentLanguage;
            s.BattleResetHotkeyModifiers = BattleResetHotkey is null ? null : (uint)BattleResetHotkey.Modifiers;
            s.BattleResetHotkeyVirtualKey = BattleResetHotkey?.VirtualKey;
        });
    }

    private void OnForegroundChanged(bool isTopMost)
    {
        Dispatcher.UIThread.Post(() => IsTopMost = isTopMost);
    }

    private void OnLanguageServiceLanguageChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(RebuildLanguageOptions);
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TopmostModeDisplay));
        OnPropertyChanged(nameof(CombatantSortMetricDisplay));
        OnPropertyChanged(nameof(SceneKindDisplay));
        OnPropertyChanged(nameof(UseCompactMainMetricsDisplay));
        OnPropertyChanged(nameof(ShowFocusStatusBarDisplay));
        OnPropertyChanged(nameof(ShowPlayerNamesDisplay));
        OnPropertyChanged(nameof(PlayerSelfMarkerDisplayModeDisplay));
        OnPropertyChanged(nameof(ShowPlayerShortServerNameDisplay));
        OnPropertyChanged(nameof(ShowPlayerLegionNameDisplay));
        OnPropertyChanged(nameof(TintPlayerNamesByFactionDisplay));
        OnPropertyChanged(nameof(PlayerNameSettingsDisplay));
        OnPropertyChanged(nameof(LanguageDisplay));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(ResetHotkeyDisplay));
    }

    private void RebuildLanguageOptions()
    {
        var selectedCode = SelectedLanguage?.Code ?? _languageService.CurrentLanguage;
        Languages.Clear();
        Languages.Add(new LanguageOption(LanguageService.TraditionalChinese, "繁體中文"));
        Languages.Add(new LanguageOption(LanguageService.English, "English"));
        Languages.Add(new LanguageOption(LanguageService.Korean, "한국어"));
        SelectedLanguage = Languages.FirstOrDefault(x => x.Code == selectedCode) ?? Languages.FirstOrDefault();
    }

    private void OnUpdatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppUpdateService.State):
                OnPropertyChanged(nameof(UpdateState));
                OnPropertyChanged(nameof(IsCheckingUpdate));
                OnPropertyChanged(nameof(IsDownloadingUpdate));
                OnPropertyChanged(nameof(IsBusyOrChecking));
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CanRestartToUpdate));
                OnPropertyChanged(nameof(ShowCheckButton));
                OnPropertyChanged(nameof(UpdateStatusText));
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
                RestartCommand.NotifyCanExecuteChanged();
                break;
            case nameof(AppUpdateService.DownloadProgress):
                OnPropertyChanged(nameof(DownloadProgress));
                OnPropertyChanged(nameof(UpdateStatusText));
                break;
            case nameof(AppUpdateService.AvailableVersion):
                OnPropertyChanged(nameof(AvailableVersion));
                OnPropertyChanged(nameof(UpdateStatusText));
                break;
            case nameof(AppUpdateService.StatusMessage):
                OnPropertyChanged(nameof(UpdateStatusMessage));
                break;
        }
    }

    public bool IsBusyOrChecking => IsCheckingUpdate || IsDownloadingUpdate;

    public bool ShowCheckButton => CanCheckForUpdates;
}
