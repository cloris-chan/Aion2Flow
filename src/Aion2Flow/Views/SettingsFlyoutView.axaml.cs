using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class SettingsFlyoutView : UserControl
{
    private MenuItem? _topmostMenuItem;
    private MenuItem? _visibleRowsMenuItem;
    private MenuItem? _combatantSortMetricMenuItem;
    private MenuItem? _sceneKindMenuItem;
    private MenuItem? _compactMainMetricsMenuItem;
    private MenuItem? _focusStatusBarMenuItem;
    private MenuItem? _playerNameSettingsMenuItem;
    private MenuItem? _showPlayerNamesMenuItem;
    private MenuItem? _playerSelfMarkerMenuItem;
    private MenuItem? _showPlayerShortServerNameMenuItem;
    private MenuItem? _showPlayerLegionNameMenuItem;
    private MenuItem? _tintPlayerNamesByFactionMenuItem;
    private MenuItem? _languageMenuItem;
    private SettingsFlyoutViewModel? _viewModel;
    private Services.LocalizationService? _localization;

    public SettingsFlyoutView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private SettingsFlyoutViewModel? ViewModel => DataContext as SettingsFlyoutViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Languages.CollectionChanged -= OnLanguagesCollectionChanged;
        }
        _localization?.LanguageChanged -= OnLocalizationLanguageChanged;

        _viewModel = ViewModel;
        _localization = _viewModel?.Localization;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Languages.CollectionChanged += OnLanguagesCollectionChanged;
        }
        _localization?.LanguageChanged += OnLocalizationLanguageChanged;

        RebuildTopmostMenuItems();
        RebuildVisibleRowsMenuItems();
        RebuildCombatantSortMetricMenuItems();
        RebuildSceneKindMenuItems();
        RefreshCompactMainMetricsMenuItem();
        RefreshFocusStatusBarMenuItem();
        RebuildPlayerNameSettingsMenuItems();
        RebuildLanguageMenuItems();
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RebuildTopmostMenuItems();
        RebuildVisibleRowsMenuItems();
        RebuildCombatantSortMetricMenuItems();
        RebuildSceneKindMenuItems();
        RefreshCompactMainMetricsMenuItem();
        RefreshFocusStatusBarMenuItem();
        RebuildPlayerNameSettingsMenuItems();
        RebuildLanguageMenuItems();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsFlyoutViewModel.TopmostMode):
            case nameof(SettingsFlyoutViewModel.TopmostModeDisplay):
                RefreshTopmostHeader();
                RefreshTopmostCheckmarks();
                break;
            case nameof(SettingsFlyoutViewModel.MaxVisibleCombatantRows):
            case nameof(SettingsFlyoutViewModel.MaxVisibleCombatantRowsDisplay):
                RefreshVisibleRowsHeader();
                RefreshVisibleRowsCheckmarks();
                break;
            case nameof(SettingsFlyoutViewModel.CombatantSortMetric):
            case nameof(SettingsFlyoutViewModel.CombatantSortMetricDisplay):
                RefreshCombatantSortMetricHeader();
                RefreshCombatantSortMetricCheckmarks();
                break;
            case nameof(SettingsFlyoutViewModel.SceneKind):
            case nameof(SettingsFlyoutViewModel.SceneKindDisplay):
                RefreshSceneKindHeader();
                RefreshSceneKindCheckmarks();
                break;
            case nameof(SettingsFlyoutViewModel.UseCompactMainMetrics):
            case nameof(SettingsFlyoutViewModel.UseCompactMainMetricsDisplay):
                RefreshCompactMainMetricsMenuItem();
                break;
            case nameof(SettingsFlyoutViewModel.ShowFocusStatusBar):
            case nameof(SettingsFlyoutViewModel.ShowFocusStatusBarDisplay):
                RefreshFocusStatusBarMenuItem();
                break;
            case nameof(SettingsFlyoutViewModel.ShowPlayerNames):
            case nameof(SettingsFlyoutViewModel.ShowPlayerNamesDisplay):
            case nameof(SettingsFlyoutViewModel.PlayerSelfMarkerDisplayMode):
            case nameof(SettingsFlyoutViewModel.PlayerSelfMarkerDisplayModeDisplay):
            case nameof(SettingsFlyoutViewModel.ShowPlayerShortServerName):
            case nameof(SettingsFlyoutViewModel.ShowPlayerShortServerNameDisplay):
            case nameof(SettingsFlyoutViewModel.ShowPlayerLegionName):
            case nameof(SettingsFlyoutViewModel.ShowPlayerLegionNameDisplay):
            case nameof(SettingsFlyoutViewModel.TintPlayerNamesByFaction):
            case nameof(SettingsFlyoutViewModel.TintPlayerNamesByFactionDisplay):
            case nameof(SettingsFlyoutViewModel.PlayerNameSettingsDisplay):
                RefreshPlayerNameSettingsMenuItems();
                break;
            case nameof(SettingsFlyoutViewModel.SelectedLanguage):
            case nameof(SettingsFlyoutViewModel.LanguageDisplay):
                RefreshLanguageHeader();
                RefreshLanguageCheckmarks();
                break;
        }
    }

    private void OnLanguagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLanguageMenuItems();
        RebuildTopmostMenuItems();
        RebuildCombatantSortMetricMenuItems();
        RebuildSceneKindMenuItems();
        RefreshCompactMainMetricsMenuItem();
        RefreshFocusStatusBarMenuItem();
        RebuildPlayerNameSettingsMenuItems();
    }

    private void TopmostMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _topmostMenuItem != mi)
        {
            _topmostMenuItem = mi;
            RebuildTopmostMenuItems();
        }
    }

    private void VisibleRowsMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _visibleRowsMenuItem != mi)
        {
            _visibleRowsMenuItem = mi;
            RebuildVisibleRowsMenuItems();
        }
    }

    private void LanguageMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _languageMenuItem != mi)
        {
            _languageMenuItem = mi;
            RebuildLanguageMenuItems();
        }
    }

    private void CombatantSortMetricMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _combatantSortMetricMenuItem != mi)
        {
            _combatantSortMetricMenuItem = mi;
            RebuildCombatantSortMetricMenuItems();
        }
    }

    private void SceneKindMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _sceneKindMenuItem != mi)
        {
            _sceneKindMenuItem = mi;
            RebuildSceneKindMenuItems();
        }
    }

    private void PlayerNameSettingsMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _playerNameSettingsMenuItem != mi)
        {
            _playerNameSettingsMenuItem = mi;
            RebuildPlayerNameSettingsMenuItems();
        }
    }

    private void CompactMainMetricsMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _compactMainMetricsMenuItem != mi)
        {
            _compactMainMetricsMenuItem = mi;
            RefreshCompactMainMetricsMenuItem();
        }
    }

    private void FocusStatusBarMenuItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && _focusStatusBarMenuItem != mi)
        {
            _focusStatusBarMenuItem = mi;
            RefreshFocusStatusBarMenuItem();
        }
    }

    private void RebuildTopmostMenuItems()
    {
        RefreshTopmostHeader();
        var vm = ViewModel;
        if (_topmostMenuItem is null || vm is null)
        {
            return;
        }

        _topmostMenuItem.Items.Clear();
        foreach (var mode in vm.TopmostModeOptions)
        {
            var item = new MenuItem
            {
                Header = vm.Localization[$"Settings_Topmost_{mode}"],
                Tag = mode
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(mode == vm.TopmostMode);
            item.Click += TopmostModeItemClicked;
            _topmostMenuItem.Items.Add(item);
        }
    }

    private void RefreshTopmostCheckmarks()
    {
        var vm = ViewModel;
        if (_topmostMenuItem is null || vm is null)
        {
            return;
        }

        foreach (var child in _topmostMenuItem.Items)
        {
            if (child is MenuItem { Tag: TopmostMode mode } mi)
            {
                mi.Icon = CreateCheckmark(mode == vm.TopmostMode);
            }
        }
    }

    private void RebuildVisibleRowsMenuItems()
    {
        RefreshVisibleRowsHeader();
        var vm = ViewModel;
        if (_visibleRowsMenuItem is null || vm is null)
        {
            return;
        }

        _visibleRowsMenuItem.Items.Clear();
        foreach (var count in vm.RowCountOptions)
        {
            var item = new MenuItem
            {
                Header = count.ToString(),
                Tag = count
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(count == vm.MaxVisibleCombatantRows);
            item.Click += VisibleRowsItemClicked;
            _visibleRowsMenuItem.Items.Add(item);
        }
    }

    private void RefreshVisibleRowsCheckmarks()
    {
        var vm = ViewModel;
        if (_visibleRowsMenuItem is null || vm is null)
        {
            return;
        }

        foreach (var child in _visibleRowsMenuItem.Items)
        {
            if (child is MenuItem { Tag: int count } mi)
            {
                mi.Icon = CreateCheckmark(count == vm.MaxVisibleCombatantRows);
            }
        }
    }

    private void RebuildCombatantSortMetricMenuItems()
    {
        RefreshCombatantSortMetricHeader();
        var vm = ViewModel;
        if (_combatantSortMetricMenuItem is null || vm is null)
        {
            return;
        }

        _combatantSortMetricMenuItem.Items.Clear();
        foreach (var metric in vm.CombatantSortMetricOptions)
        {
            var item = new MenuItem
            {
                Header = vm.Localization[$"Settings_CombatantSortMetric_{metric}"],
                Tag = metric
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(metric == vm.CombatantSortMetric);
            item.Click += CombatantSortMetricItemClicked;
            _combatantSortMetricMenuItem.Items.Add(item);
        }
    }

    private void RefreshCombatantSortMetricCheckmarks()
    {
        var vm = ViewModel;
        if (_combatantSortMetricMenuItem is null || vm is null)
        {
            return;
        }

        foreach (var child in _combatantSortMetricMenuItem.Items)
        {
            if (child is MenuItem { Tag: CombatantSortMetric metric } mi)
            {
                mi.Icon = CreateCheckmark(metric == vm.CombatantSortMetric);
            }
        }
    }

    private void RebuildSceneKindMenuItems()
    {
        RefreshSceneKindHeader();
        var vm = ViewModel;
        if (_sceneKindMenuItem is null || vm is null)
        {
            return;
        }

        _sceneKindMenuItem.Items.Clear();
        foreach (var kind in vm.SceneKindOptions)
        {
            var item = new MenuItem
            {
                Header = vm.Localization[$"Settings_SceneKind_{kind}"],
                Tag = kind
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(kind == vm.SceneKind);
            item.Click += SceneKindItemClicked;
            _sceneKindMenuItem.Items.Add(item);
        }
    }

    private void RefreshSceneKindCheckmarks()
    {
        var vm = ViewModel;
        if (_sceneKindMenuItem is null || vm is null)
        {
            return;
        }

        foreach (var child in _sceneKindMenuItem.Items)
        {
            if (child is MenuItem { Tag: SceneKind kind } mi)
                mi.Icon = CreateCheckmark(kind == vm.SceneKind);
        }
    }

    private void RebuildPlayerNameSettingsMenuItems()
    {
        var vm = ViewModel;
        RefreshPlayerNameSettingsHeader();
        if (_playerNameSettingsMenuItem is null || vm is null)
        {
            return;
        }

        _playerNameSettingsMenuItem.Items.Clear();
        _showPlayerNamesMenuItem = CreateToggleMenuItem(vm.Localization["Settings_ShowPlayerNames"], vm.ShowPlayerNamesDisplay, vm.ShowPlayerNames, ShowPlayerNamesMenuItemClicked);
        _playerSelfMarkerMenuItem = new MenuItem();
        _playerSelfMarkerMenuItem.Classes.Add("FlyoutMenuItem");
        _playerSelfMarkerMenuItem.Classes.Add("SettingsRowItem");
        _playerSelfMarkerMenuItem.Items.Clear();
        foreach (var mode in vm.PlayerSelfMarkerDisplayModeOptions)
        {
            var item = new MenuItem
            {
                Header = vm.Localization[$"Settings_PlayerSelfMarker_{mode}"],
                Tag = mode,
                StaysOpenOnClick = true
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(mode == vm.PlayerSelfMarkerDisplayMode);
            item.Click += PlayerSelfMarkerDisplayModeItemClicked;
            _playerSelfMarkerMenuItem.Items.Add(item);
        }

        _showPlayerShortServerNameMenuItem = CreateToggleMenuItem(vm.Localization["Settings_PlayerShortServerName"], vm.ShowPlayerShortServerNameDisplay, vm.ShowPlayerShortServerName, ShowPlayerShortServerNameMenuItemClicked);
        _showPlayerLegionNameMenuItem = CreateToggleMenuItem(vm.Localization["Settings_PlayerLegionName"], vm.ShowPlayerLegionNameDisplay, vm.ShowPlayerLegionName, ShowPlayerLegionNameMenuItemClicked);
        _tintPlayerNamesByFactionMenuItem = CreateToggleMenuItem(vm.Localization["Settings_PlayerFactionTint"], vm.TintPlayerNamesByFactionDisplay, vm.TintPlayerNamesByFaction, TintPlayerNamesByFactionMenuItemClicked);

        _playerNameSettingsMenuItem.Items.Add(_showPlayerNamesMenuItem);
        _playerNameSettingsMenuItem.Items.Add(_playerSelfMarkerMenuItem);
        _playerNameSettingsMenuItem.Items.Add(_showPlayerShortServerNameMenuItem);
        _playerNameSettingsMenuItem.Items.Add(_showPlayerLegionNameMenuItem);
        _playerNameSettingsMenuItem.Items.Add(_tintPlayerNamesByFactionMenuItem);
        RefreshPlayerNameSettingsMenuItems();
    }

    private void RefreshPlayerNameSettingsMenuItems()
    {
        RefreshPlayerNameSettingsHeader();
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        if (_showPlayerNamesMenuItem is not null)
        {
            _showPlayerNamesMenuItem.Header = CreateRowHeader(vm.Localization["Settings_ShowPlayerNames"], vm.ShowPlayerNamesDisplay);
            _showPlayerNamesMenuItem.Icon = CreateCheckmark(vm.ShowPlayerNames);
        }

        if (_playerSelfMarkerMenuItem is not null)
        {
            _playerSelfMarkerMenuItem.Header = CreateRowHeader(vm.Localization["Settings_PlayerSelfMarker"], vm.PlayerSelfMarkerDisplayModeDisplay);
            foreach (var child in _playerSelfMarkerMenuItem.Items)
            {
                if (child is MenuItem { Tag: PlayerSelfMarkerDisplayMode mode } mi)
                {
                    mi.Icon = CreateCheckmark(mode == vm.PlayerSelfMarkerDisplayMode);
                }
            }
        }

        if (_showPlayerShortServerNameMenuItem is not null)
        {
            _showPlayerShortServerNameMenuItem.Header = CreateRowHeader(vm.Localization["Settings_PlayerShortServerName"], vm.ShowPlayerShortServerNameDisplay);
            _showPlayerShortServerNameMenuItem.Icon = CreateCheckmark(vm.ShowPlayerShortServerName);
        }

        if (_showPlayerLegionNameMenuItem is not null)
        {
            _showPlayerLegionNameMenuItem.Header = CreateRowHeader(vm.Localization["Settings_PlayerLegionName"], vm.ShowPlayerLegionNameDisplay);
            _showPlayerLegionNameMenuItem.Icon = CreateCheckmark(vm.ShowPlayerLegionName);
        }

        if (_tintPlayerNamesByFactionMenuItem is not null)
        {
            _tintPlayerNamesByFactionMenuItem.Header = CreateRowHeader(vm.Localization["Settings_PlayerFactionTint"], vm.TintPlayerNamesByFactionDisplay);
            _tintPlayerNamesByFactionMenuItem.Icon = CreateCheckmark(vm.TintPlayerNamesByFaction);
        }
    }

    private void RefreshCompactMainMetricsMenuItem()
    {
        var vm = ViewModel;
        if (_compactMainMetricsMenuItem is null || vm is null)
        {
            return;
        }

        _compactMainMetricsMenuItem.Header = CreateRowHeader(vm.Localization["Settings_MainMetricsCompact"], vm.UseCompactMainMetricsDisplay);
        _compactMainMetricsMenuItem.Icon = CreateCheckmark(vm.UseCompactMainMetrics);
    }

    private void RefreshFocusStatusBarMenuItem()
    {
        var vm = ViewModel;
        if (_focusStatusBarMenuItem is null || vm is null)
        {
            return;
        }

        _focusStatusBarMenuItem.Header = CreateRowHeader(vm.Localization["Settings_FocusStatusBar"], vm.ShowFocusStatusBarDisplay);
        _focusStatusBarMenuItem.Icon = CreateCheckmark(vm.ShowFocusStatusBar);
    }

    private void RebuildLanguageMenuItems()
    {
        RefreshLanguageHeader();
        var vm = ViewModel;
        if (_languageMenuItem is null || vm is null)
        {
            return;
        }

        _languageMenuItem.Items.Clear();
        foreach (var option in vm.Languages)
        {
            var item = new MenuItem
            {
                Header = option.DisplayName,
                Tag = option.Code
            };
            item.Classes.Add("FlyoutMenuItem");
            item.Icon = CreateCheckmark(string.Equals(option.Code, vm.SelectedLanguage?.Code, StringComparison.Ordinal));
            item.Click += LanguageItemClicked;
            _languageMenuItem.Items.Add(item);
        }
    }

    private void RefreshLanguageCheckmarks()
    {
        var vm = ViewModel;
        if (_languageMenuItem is null || vm is null)
        {
            return;
        }

        foreach (var child in _languageMenuItem.Items)
        {
            if (child is MenuItem { Tag: string code } mi)
            {
                mi.Icon = CreateCheckmark(string.Equals(code, vm.SelectedLanguage?.Code, StringComparison.Ordinal));
            }
        }
    }

    private void TopmostModeItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: TopmostMode mode })
        {
            vm.TopmostMode = mode;
        }
    }

    private void VisibleRowsItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: int count })
        {
            vm.MaxVisibleCombatantRows = count;
        }
    }

    private void CombatantSortMetricItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: CombatantSortMetric metric })
        {
            vm.CombatantSortMetric = metric;
        }
    }

    private void SceneKindItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: SceneKind kind })
            vm.SceneKind = kind;
    }

    private void ShowPlayerNamesMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.ShowPlayerNames = !vm.ShowPlayerNames;
        }

        e.Handled = true;
    }

    private void PlayerSelfMarkerDisplayModeItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: PlayerSelfMarkerDisplayMode mode })
        {
            vm.PlayerSelfMarkerDisplayMode = mode;
        }

        e.Handled = true;
    }

    private void ShowPlayerShortServerNameMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.ShowPlayerShortServerName = !vm.ShowPlayerShortServerName;
        }

        e.Handled = true;
    }

    private void ShowPlayerLegionNameMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.ShowPlayerLegionName = !vm.ShowPlayerLegionName;
        }

        e.Handled = true;
    }

    private void TintPlayerNamesByFactionMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.TintPlayerNamesByFaction = !vm.TintPlayerNamesByFaction;
        }

        e.Handled = true;
    }

    private void CompactMainMetricsMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.UseCompactMainMetrics = !vm.UseCompactMainMetrics;
        }
    }

    private void FocusStatusBarMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.ShowFocusStatusBar = !vm.ShowFocusStatusBar;
        }
    }

    private void LanguageItemClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: string code })
        {
            var option = vm.Languages.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.Ordinal));
            if (option is not null)
            {
                vm.SelectedLanguage = option;
            }
        }
    }

    private void RefreshTopmostHeader()
    {
        var vm = ViewModel;
        if (_topmostMenuItem is null || vm is null) return;
        _topmostMenuItem.Header = CreateRowHeader(vm.Localization["Settings_Topmost"], vm.TopmostModeDisplay);
    }

    private void RefreshVisibleRowsHeader()
    {
        var vm = ViewModel;
        if (_visibleRowsMenuItem is null || vm is null) return;
        _visibleRowsMenuItem.Header = CreateRowHeader(vm.Localization["Settings_VisibleRows"], vm.MaxVisibleCombatantRowsDisplay);
    }

    private void RefreshCombatantSortMetricHeader()
    {
        var vm = ViewModel;
        if (_combatantSortMetricMenuItem is null || vm is null) return;
        _combatantSortMetricMenuItem.Header = CreateRowHeader(vm.Localization["Settings_CombatantSortMetric"], vm.CombatantSortMetricDisplay);
    }

    private void RefreshSceneKindHeader()
    {
        var vm = ViewModel;
        if (_sceneKindMenuItem is null || vm is null) return;
        _sceneKindMenuItem.Header = CreateRowHeader(vm.Localization["Settings_SceneKind"], vm.SceneKindDisplay);
    }

    private void RefreshLanguageHeader()
    {
        var vm = ViewModel;
        if (_languageMenuItem is null || vm is null) return;
        _languageMenuItem.Header = CreateRowHeader(vm.Localization["Settings_Language"], vm.LanguageDisplay);
    }

    private void RefreshPlayerNameSettingsHeader()
    {
        var vm = ViewModel;
        if (_playerNameSettingsMenuItem is null || vm is null) return;
        _playerNameSettingsMenuItem.Header = CreateRowHeader(vm.Localization["Settings_PlayerNameSettings"], vm.PlayerNameSettingsDisplay);
    }

    private static MenuItem CreateToggleMenuItem(string label, string value, bool isChecked, EventHandler<RoutedEventArgs> click)
    {
        var item = new MenuItem
        {
            Header = CreateRowHeader(label, value),
            Icon = CreateCheckmark(isChecked),
            StaysOpenOnClick = true
        };
        item.Classes.Add("FlyoutMenuItem");
        item.Classes.Add("SettingsRowItem");
        item.Click += click;
        return item;
    }

    private static Grid CreateRowHeader(string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var labelText = new TextBlock { Text = label };
        labelText.Classes.Add("SettingsRowLabel");
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueText = new TextBlock { Text = value };
        valueText.Classes.Add("SettingsRowValue");
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        return grid;
    }

    private static Avalonia.Controls.Shapes.Path? CreateCheckmark(bool visible)
    {
        if (!visible)
        {
            return null;
        }

        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null || !resources.TryGetResource("check", null, out var resource) || resource is not Avalonia.Media.Geometry geometry)
        {
            return null;
        }

        var path = new Avalonia.Controls.Shapes.Path { Data = geometry };
        path.Classes.Add("Glyph");
        path.Classes.Add("GlyphLg");
        return path;
    }

    private void BattleResetHotkeyRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || ViewModel is not { } vm)
        {
            return;
        }

        border.Focus();
        if (!vm.IsCapturingResetHotkey)
        {
            vm.BeginCaptureBattleResetHotkeyCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void BattleResetHotkeyRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || !vm.IsCapturingResetHotkey)
        {
            return;
        }

        if (e.Key is Key.Escape)
        {
            vm.CancelCaptureBattleResetHotkeyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        var definition = HotkeyDefinition.FromKeyEvent(e.KeyModifiers, e.Key);
        if (definition is null)
        {
            return;
        }

        vm.ApplyCapturedHotkey(definition);
        e.Handled = true;
    }

    private void BattleResetHotkeyRowLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (ViewModel is { IsCapturingResetHotkey: true } vm)
        {
            vm.CancelCaptureBattleResetHotkeyCommand.Execute(null);
        }
    }
}
