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
    private readonly PlayerNamePrivacyService _playerNamePrivacy;
    private readonly AppUpdateService _updateService;
    private readonly ProcessForegroundWatcher _processForegroundWatcher;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly bool _isApplyingPersistedSettings;

    public SettingsFlyoutViewModel(LocalizationService localization, LanguageService languageService, SettingsService settingsService, PlayerNamePrivacyService playerNamePrivacy, AppUpdateService updateService, ProcessForegroundWatcher processForegroundWatcher, GlobalHotkeyService globalHotkeyService)
    {
        Localization = localization;
        _languageService = languageService;
        _settingsService = settingsService;
        _playerNamePrivacy = playerNamePrivacy;
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
            HidePlayerNames = persisted.HidePlayerNames;
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
    [NotifyPropertyChangedFor(nameof(HidePlayerNamesDisplay))]
    public partial bool HidePlayerNames { get; set; }

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

    public string HidePlayerNamesDisplay => Localization[HidePlayerNames ? "Settings_HidePlayerNames_On" : "Settings_HidePlayerNames_Off"];

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

    partial void OnHidePlayerNamesChanged(bool value)
    {
        _playerNamePrivacy.HidePlayerNames = value;
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
            s.HidePlayerNames = HidePlayerNames;
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
        OnPropertyChanged(nameof(HidePlayerNamesDisplay));
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
