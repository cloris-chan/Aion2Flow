using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class MainWindow : Window
{
    public new MainViewModel DataContext { get => (MainViewModel)base.DataContext!; set => base.DataContext = value; }

    public MainWindow()
    {
        DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
        DataContext.InitializeAsync().ConfigureAwait(false);
        AvaloniaXamlLoader.Load(this);
        DataContext.BattleHistory.CollectionChanged += OnBattleHistoryCollectionChanged;
        RebuildBattleHistoryMenuItems();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RebuildBattleHistoryMenuItems();
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContext.BattleHistory.CollectionChanged -= OnBattleHistoryCollectionChanged;
        base.OnClosed(e);
        DataContext.DisposeAsync().AsTask().ConfigureAwait(false);
    }

    private void Minimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Exit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void CombatantRowTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Border { DataContext: CombatantRowViewModel combatant })
        {
            DataContext.SelectedCombatant = combatant;
            FlyoutBase.ShowAttachedFlyout(this);
        }
    }

    private void FlyoutClosed(object? sender, EventArgs e)
    {
        DataContext.SelectCombatantCommand.Execute(null);
    }

    private void LanguageMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string languageCode })
        {
            var option = DataContext.Languages.FirstOrDefault(x => string.Equals(x.Code, languageCode, StringComparison.Ordinal));
            if (option is not null)
            {
                DataContext.SelectedLanguage = option;
            }
        }
    }

    private void OnBattleHistoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildBattleHistoryMenuItems();
    }

    private void RebuildBattleHistoryMenuItems()
    {
        if (this.FindControl<Button>("BattleHistoryButton")?.Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        if (DataContext.BattleHistory.Count == 0)
        {
            var placeholder = new MenuItem
            {
                Header = DataContext.Localization["Panel.EmptyHistory"],
                IsEnabled = false
            };
            placeholder.Classes.Add("FlyoutMenuItem");
            placeholder.Classes.Add("FlyoutMenuItemPlaceholder");
            menu.Items.Add(placeholder);
            return;
        }

        foreach (var item in DataContext.BattleHistory)
        {
            var menuItem = new MenuItem
            {
                Header = item.DisplayName,
                Tag = item
            };
            menuItem.Classes.Add("FlyoutMenuItem");
            menuItem.Click += BattleHistoryMenuItemClicked;
            menu.Items.Add(menuItem);
        }
    }

    private void BattleHistoryMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: BattleHistoryItemViewModel item })
        {
            DataContext.SelectedBattleHistory = item;
        }
    }
}
