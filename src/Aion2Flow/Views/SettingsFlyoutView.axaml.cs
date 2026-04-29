using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class SettingsFlyoutView : UserControl
{
    private MenuItem? _topmostMenuItem;
    private MenuItem? _visibleRowsMenuItem;
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
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLocalizationLanguageChanged;
        }

        _viewModel = ViewModel;
        _localization = _viewModel?.Localization;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Languages.CollectionChanged += OnLanguagesCollectionChanged;
        }
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLocalizationLanguageChanged;
        }

        RebuildTopmostMenuItems();
        RebuildVisibleRowsMenuItems();
        RebuildLanguageMenuItems();
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RebuildTopmostMenuItems();
        RebuildVisibleRowsMenuItems();
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

    private void RefreshLanguageHeader()
    {
        var vm = ViewModel;
        if (_languageMenuItem is null || vm is null) return;
        _languageMenuItem.Header = CreateRowHeader(vm.Localization["Settings_Language"], vm.LanguageDisplay);
    }

    private static Grid CreateRowHeader(string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
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
}
