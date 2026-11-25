using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LanguageService _languageService;
    private readonly ClientSettingsService _clientSettings;
    private readonly SettingsService _settingsService;
    private readonly PlayerNameDisplayService _playerNameDisplay;
    private readonly UiScaleService _uiScale;
    private readonly AppUpdateService _updateService;
    private readonly ProcessForegroundWatcher _processForegroundWatcher;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly bool _isApplyingPersistedSettings;
    private bool _hasObservedHotkeyRegistrationState;
    private bool _isHotkeyRegistrationWindowAttached;

    public SettingsViewModel(LocalizationService localization, LanguageService languageService, SettingsService settingsService, PlayerNameDisplayService playerNameDisplay, UiScaleService uiScale, AppUpdateService updateService, ProcessForegroundWatcher processForegroundWatcher, GlobalHotkeyService globalHotkeyService, ClientSettingsService clientSettings)
    {
        Localization = localization;
        _languageService = languageService;
        _clientSettings = clientSettings;
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
            CombatantStatisticsScope = persisted.CombatantStatisticsScope;
            SceneKind = persisted.SceneKind;
            UseCompactMainMetrics = persisted.UseCompactMainMetrics;
            ShowDamagePerSecondColumn = persisted.ShowDamagePerSecondColumn;
            ShowDamageColumn = persisted.ShowDamageColumn;
            ShowTotalDamagePerSecond = persisted.ShowTotalDamagePerSecond;
            EncounterTimeDisplayFormat = persisted.EncounterTimeDisplayFormat;
            ShowFocusStatusBar = persisted.ShowFocusStatusBar;
            HideHeaderWhenClickThrough = persisted.HideHeaderWhenClickThrough;
            ShowPlayerNames = persisted.ShowPlayerNames;
            PlayerSelfMarkerDisplayMode = persisted.PlayerSelfMarkerDisplayMode;
            ShowPlayerShortServerName = persisted.ShowPlayerShortServerName;
            ShowPlayerLegionName = persisted.ShowPlayerLegionName;
            TintPlayerNamesByFaction = persisted.TintPlayerNamesByFaction;
            SkillMonitorEnabled = persisted.SkillMonitorEnabled;
            UiScalePercent = persisted.UiScalePercent;
            BattleResetHotkey = CreateHotkey(persisted.BattleResetHotkeyModifiers, persisted.BattleResetHotkeyVirtualKey);
            OverlayInteractionHotkey = CreateHotkey(persisted.OverlayInteractionHotkeyModifiers, persisted.OverlayInteractionHotkeyVirtualKey);
            if (BattleResetHotkey is not null && BattleResetHotkey == OverlayInteractionHotkey)
            {
                OverlayInteractionHotkey = null;
            }
        }
        finally
        {
            _isApplyingPersistedSettings = false;
        }

        if (!_globalHotkeyService.TrySetHotkey(GlobalHotkeyAction.BattleReset, BattleResetHotkey))
        {
            BattleResetHotkey = null;
        }
        if (!_globalHotkeyService.TrySetHotkey(GlobalHotkeyAction.CycleOverlayInteraction, OverlayInteractionHotkey))
        {
            OverlayInteractionHotkey = null;
        }

        SelectCurrentInterfaceLanguage();
        SelectedClientVersion = _clientSettings.CurrentProfile.Version;
        RebuildResourceLanguageOptions();

        _languageService.LanguageChanged += OnLanguageServiceLanguageChanged;
        _clientSettings.ClientChanged += OnClientChanged;
        _clientSettings.ResourceLanguageChanged += OnResourceLanguageChanged;
        _processForegroundWatcher.ForegroundChanged += OnForegroundChanged;
        _updateService.PropertyChanged += OnUpdatePropertyChanged;
        Localization.LanguageChanged += OnLocalizationLanguageChanged;
    }

    public LocalizationService Localization { get; }

    public IReadOnlyList<LanguageOption> Languages { get; } = CreateLanguageOptions(LanguageService.SupportedLanguages);

    [ObservableProperty]
    public partial IReadOnlyList<LanguageOption> ResourceLanguages { get; private set; } = [];

    public IReadOnlyList<int> RowCountOptions { get; } = [5, 6, 7, 8, 9, 10];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlwaysOnTop))]
    public partial TopmostMode TopmostMode { get; set; } = TopmostMode.GameForeground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlwaysOnTop))]
    public partial bool IsTopMost { get; private set; }

    [ObservableProperty]
    public partial int MaxVisibleCombatantRows { get; set; } = 5;

    [ObservableProperty]
    public partial CombatantSortMetric CombatantSortMetric { get; set; } = CombatantSortMetric.DamagePerSecond;

    [ObservableProperty]
    public partial CombatantStatisticsScope CombatantStatisticsScope { get; set; } = CombatantStatisticsScope.All;

    [ObservableProperty]
    public partial SceneKind SceneKind { get; set; } = SceneKind.Standard;

    [ObservableProperty]
    public partial bool UseCompactMainMetrics { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDamagePerSecondColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDamageColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTotalDamagePerSecond { get; set; } = true;

    [ObservableProperty]
    public partial EncounterTimeDisplayFormat EncounterTimeDisplayFormat { get; set; } = EncounterTimeDisplayFormat.DecimalSeconds;

    [ObservableProperty]
    public partial bool ShowFocusStatusBar { get; set; } = true;

    [ObservableProperty]
    public partial bool HideHeaderWhenClickThrough { get; set; }

    [ObservableProperty]
    public partial bool ShowPlayerNames { get; set; }

    [ObservableProperty]
    public partial PlayerSelfMarkerDisplayMode PlayerSelfMarkerDisplayMode { get; set; } = PlayerSelfMarkerDisplayMode.WhenNamesHidden;

    [ObservableProperty]
    public partial bool ShowPlayerShortServerName { get; set; }

    [ObservableProperty]
    public partial bool ShowPlayerLegionName { get; set; }

    [ObservableProperty]
    public partial bool TintPlayerNamesByFaction { get; set; }

    [ObservableProperty]
    public partial bool SkillMonitorEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int UiScalePercent { get; set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedResourceLanguage { get; set; }

    [ObservableProperty]
    public partial ClientVersion SelectedClientVersion { get; set; } = ClientVersion.Purple;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetHotkeyDisplay))]
    [NotifyPropertyChangedFor(nameof(HasResetHotkey))]
    public partial HotkeyDefinition? BattleResetHotkey { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetHotkeyDisplay))]
    [NotifyPropertyChangedFor(nameof(OverlayInteractionHotkeyDisplay))]
    [NotifyPropertyChangedFor(nameof(IsCapturingResetHotkey))]
    [NotifyPropertyChangedFor(nameof(IsCapturingOverlayInteractionHotkey))]
    public partial GlobalHotkeyAction? CapturingHotkeyAction { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayInteractionHotkeyDisplay))]
    [NotifyPropertyChangedFor(nameof(HasOverlayInteractionHotkey))]
    public partial HotkeyDefinition? OverlayInteractionHotkey { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyRegistrationErrorText))]
    public partial bool HasHotkeyRegistrationError { get; private set; }

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

    public bool IsCapturingResetHotkey => CapturingHotkeyAction == GlobalHotkeyAction.BattleReset;

    public string OverlayInteractionHotkeyDisplay
    {
        get
        {
            if (IsCapturingOverlayInteractionHotkey)
            {
                return Localization["Settings_Hotkey_PressKeys"];
            }
            return OverlayInteractionHotkey?.Display ?? Localization["Settings_Hotkey_None"];
        }
    }

    public bool HasOverlayInteractionHotkey => OverlayInteractionHotkey is not null;

    public bool IsCapturingOverlayInteractionHotkey => CapturingHotkeyAction == GlobalHotkeyAction.CycleOverlayInteraction;

    public string HotkeyRegistrationErrorText => Localization["Settings_Hotkey_RegistrationUnavailable"];

    public bool IsAlwaysOnTop => TopmostMode switch
    {
        TopmostMode.Always => true,
        TopmostMode.Never => false,
        _ => IsTopMost
    };

    internal void RefreshTargetProcessForegroundState() => IsTopMost = _processForegroundWatcher.IsTargetProcessForeground();

    public bool IsUpdateSectionVisible => _updateService.IsManagedByVelopack;

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

    public string CurrentVersionText => string.Format(
        Localization["Settings_Update_CurrentVersionFormat"],
        _updateService.CurrentVersion ?? typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3));

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private void CheckForUpdates() => _updateService.CheckForUpdates();

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private Task RestartAsync() => _updateService.RestartAsync();

    partial void OnTopmostModeChanged(TopmostMode value) => PersistSettings();

    partial void OnMaxVisibleCombatantRowsChanged(int value) => PersistSettings();

    partial void OnCombatantSortMetricChanged(CombatantSortMetric value) => PersistSettings();

    partial void OnCombatantStatisticsScopeChanged(CombatantStatisticsScope value) => PersistSettings();

    partial void OnSceneKindChanged(SceneKind value) => PersistSettings();

    partial void OnUseCompactMainMetricsChanged(bool value) => PersistSettings();

    partial void OnShowDamagePerSecondColumnChanged(bool value) => PersistSettings();

    partial void OnShowDamageColumnChanged(bool value) => PersistSettings();

    partial void OnShowTotalDamagePerSecondChanged(bool value) => PersistSettings();

    partial void OnEncounterTimeDisplayFormatChanged(EncounterTimeDisplayFormat value) => PersistSettings();

    partial void OnShowFocusStatusBarChanged(bool value) => PersistSettings();

    partial void OnHideHeaderWhenClickThroughChanged(bool value) => PersistSettings();

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

    partial void OnSkillMonitorEnabledChanged(bool value) => PersistSettings();

    partial void OnUiScalePercentChanged(int value)
    {
        _uiScale.SetScalePercent(value);
        PersistSettings();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
            PersistSettings();
        }
    }

    partial void OnSelectedResourceLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
            _clientSettings.SetResourceLanguage(value.Code);
    }

    partial void OnSelectedClientVersionChanged(ClientVersion value) => _clientSettings.SetClientVersion(value);

    public void BeginCaptureHotkey(GlobalHotkeyAction action)
    {
        ValidateAction(action);
        CapturingHotkeyAction = action;
    }

    public void CancelCaptureHotkey(GlobalHotkeyAction action)
    {
        ValidateAction(action);
        if (CapturingHotkeyAction == action)
        {
            CapturingHotkeyAction = null;
        }
    }

    [RelayCommand]
    public void ClearHotkey(GlobalHotkeyAction action)
    {
        CancelCaptureHotkey(action);
        if (GetHotkey(action) is null)
        {
            return;
        }

        if (!_globalHotkeyService.TrySetHotkey(action, null))
        {
            HasHotkeyRegistrationError = true;
            return;
        }

        SetHotkeyValue(action, null);
        RefreshHotkeyRegistrationError();
        PersistSettings();
    }

    public bool ApplyCapturedHotkey(GlobalHotkeyAction action, HotkeyDefinition definition)
    {
        ValidateAction(action);
        ArgumentNullException.ThrowIfNull(definition);
        if (CapturingHotkeyAction != action)
        {
            return false;
        }

        CapturingHotkeyAction = null;
        var conflictingAction = OtherAction(action);
        var hasConflict = definition == GetHotkey(conflictingAction);
        var changed = definition != GetHotkey(action) || hasConflict;
        if (!_globalHotkeyService.TrySetHotkey(action, definition))
        {
            HasHotkeyRegistrationError = true;
            return false;
        }

        if (!changed)
        {
            RefreshHotkeyRegistrationError();
            return true;
        }

        if (hasConflict)
        {
            SetHotkeyValue(conflictingAction, null);
        }
        SetHotkeyValue(action, definition);
        RefreshHotkeyRegistrationError();
        PersistSettings();
        return true;
    }

    public void RefreshHotkeyRegistrationState(bool isRegistrationWindowAttached)
    {
        _hasObservedHotkeyRegistrationState = true;
        _isHotkeyRegistrationWindowAttached = isRegistrationWindowAttached;
        RefreshHotkeyRegistrationError();
    }

    private void SetHotkeyValue(GlobalHotkeyAction action, HotkeyDefinition? definition)
    {
        switch (action)
        {
            case GlobalHotkeyAction.BattleReset:
                BattleResetHotkey = definition;
                break;
            case GlobalHotkeyAction.CycleOverlayInteraction:
                OverlayInteractionHotkey = definition;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.");
        }
    }

    private HotkeyDefinition? GetHotkey(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => BattleResetHotkey,
        GlobalHotkeyAction.CycleOverlayInteraction => OverlayInteractionHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private static GlobalHotkeyAction OtherAction(GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.BattleReset => GlobalHotkeyAction.CycleOverlayInteraction,
        GlobalHotkeyAction.CycleOverlayInteraction => GlobalHotkeyAction.BattleReset,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported global hotkey action.")
    };

    private static void ValidateAction(GlobalHotkeyAction action) => _ = OtherAction(action);

    private static HotkeyDefinition? CreateHotkey(uint? modifiers, uint? virtualKey) =>
        modifiers is { } mods && virtualKey is { } vk
            ? HotkeyDefinition.TryCreate((HotkeyModifiers)mods, vk)
            : null;

    private void RefreshHotkeyRegistrationError()
    {
        if (_hasObservedHotkeyRegistrationState)
        {
            HasHotkeyRegistrationError =
                (!_isHotkeyRegistrationWindowAttached && (BattleResetHotkey is not null || OverlayInteractionHotkey is not null)) ||
                HasUnavailableRegisteredHotkey();
        }
        else
        {
            HasHotkeyRegistrationError = false;
        }
    }

    private bool HasUnavailableRegisteredHotkey() =>
        (BattleResetHotkey is not null && !_globalHotkeyService.IsRegistered(GlobalHotkeyAction.BattleReset)) ||
        (OverlayInteractionHotkey is not null && !_globalHotkeyService.IsRegistered(GlobalHotkeyAction.CycleOverlayInteraction));

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
            s.CombatantStatisticsScope = CombatantStatisticsScope;
            s.SceneKind = SceneKind;
            s.UseCompactMainMetrics = UseCompactMainMetrics;
            s.ShowDamagePerSecondColumn = ShowDamagePerSecondColumn;
            s.ShowDamageColumn = ShowDamageColumn;
            s.ShowTotalDamagePerSecond = ShowTotalDamagePerSecond;
            s.EncounterTimeDisplayFormat = EncounterTimeDisplayFormat;
            s.ShowFocusStatusBar = ShowFocusStatusBar;
            s.HideHeaderWhenClickThrough = HideHeaderWhenClickThrough;
            s.ShowPlayerNames = ShowPlayerNames;
            s.PlayerSelfMarkerDisplayMode = PlayerSelfMarkerDisplayMode;
            s.ShowPlayerShortServerName = ShowPlayerShortServerName;
            s.ShowPlayerLegionName = ShowPlayerLegionName;
            s.TintPlayerNamesByFaction = TintPlayerNamesByFaction;
            s.SkillMonitorEnabled = SkillMonitorEnabled;
            s.UiScalePercent = UiScalePercent;
            s.Language = SelectedLanguage?.Code ?? _languageService.CurrentLanguage;
            s.BattleResetHotkeyModifiers = BattleResetHotkey is null ? null : (uint)BattleResetHotkey.Modifiers;
            s.BattleResetHotkeyVirtualKey = BattleResetHotkey?.VirtualKey;
            s.OverlayInteractionHotkeyModifiers = OverlayInteractionHotkey is null ? null : (uint)OverlayInteractionHotkey.Modifiers;
            s.OverlayInteractionHotkeyVirtualKey = OverlayInteractionHotkey?.VirtualKey;
        });
    }

    private void OnForegroundChanged() => Dispatcher.UIThread.Post(RefreshTargetProcessForegroundState);

    private void OnLanguageServiceLanguageChanged(object? sender, string language)
    {
        Dispatcher.UIThread.Post(SelectCurrentInterfaceLanguage);
    }

    private void OnClientChanged(object? sender, ClientProfile profile)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SelectedClientVersion = _clientSettings.CurrentProfile.Version;
            RebuildResourceLanguageOptions();
        });
    }

    private void OnResourceLanguageChanged(object? sender, string language)
        => Dispatcher.UIThread.Post(RebuildResourceLanguageOptions);

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(ResetHotkeyDisplay));
        OnPropertyChanged(nameof(OverlayInteractionHotkeyDisplay));
        OnPropertyChanged(nameof(HotkeyRegistrationErrorText));
    }

    private void SelectCurrentInterfaceLanguage()
        => SelectedLanguage = Languages.First(option => option.Code == _languageService.CurrentLanguage);

    private void RebuildResourceLanguageOptions()
    {
        var options = CreateLanguageOptions(_clientSettings.ResourceLanguages);
        ResourceLanguages = options;
        SelectedResourceLanguage = options.First(option => option.Code == _clientSettings.ResourceLanguage);
    }

    private static LanguageOption[] CreateLanguageOptions(IReadOnlyList<string> languages)
        => languages.Select(static language => new LanguageOption(language, CultureInfo.GetCultureInfo(language).NativeName)).ToArray();

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

}
