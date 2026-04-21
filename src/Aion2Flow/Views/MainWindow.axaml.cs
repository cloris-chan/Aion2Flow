using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void Minimize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Exit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBarDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CombatantRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: CombatantRowViewModel combatant })
        {
            DataContext.SelectedCombatant = combatant;
            if (TryGetCombatantDetailsFlyout(out var flyout, out var flyoutView))
            {
                ConfigureCombatantDetailsFlyout(flyout, flyoutView);
                flyout.ShowAt(this);
            }
        }
    }

    private void FlyoutClosed(object? sender, EventArgs e)
    {
        DataContext.SelectCombatantCommand.Execute(null);
    }

    private void LanguageMenuItemClicked(object? sender, RoutedEventArgs e)
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

    private void BattleHistoryMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: BattleHistoryItemViewModel item })
        {
            DataContext.SelectedBattleHistory = item;
        }
    }

    private void ConfigureCombatantDetailsFlyout(Flyout flyout, CombatantDetailsFlyoutView flyoutView)
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            return;
        }

        var topLeft = this.PointToScreen(new Point(0, 0));
        var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
        var workArea = screen.WorkingArea;

        var leftSpace = Math.Max(0, topLeft.X - workArea.X);
        var rightSpace = Math.Max(0, workArea.Right - bottomRight.X);
        var topSpace = Math.Max(0, topLeft.Y - workArea.Y);
        var bottomSpace = Math.Max(0, workArea.Bottom - bottomRight.Y);

        var placeRight = rightSpace >= leftSpace;
        var alignTop = bottomSpace >= topSpace;

        flyout.Placement = (placeRight, alignTop) switch
        {
            (true, true) => PlacementMode.RightEdgeAlignedTop,
            (true, false) => PlacementMode.RightEdgeAlignedBottom,
            (false, true) => PlacementMode.LeftEdgeAlignedTop,
            _ => PlacementMode.LeftEdgeAlignedBottom
        };

        var renderScale = RenderScaling <= 0 ? 1d : RenderScaling;
        var availableWidth = Math.Max(0d, (placeRight ? rightSpace : leftSpace) / renderScale - 16d);
        var availableHeight = Math.Max(
            0d,
            (alignTop ? workArea.Bottom - topLeft.Y : bottomRight.Y - workArea.Y) / renderScale - 16d);

        flyoutView.ConfigureViewport(availableWidth, availableHeight);
    }

    private bool TryGetCombatantDetailsFlyout(out Flyout flyout, out CombatantDetailsFlyoutView flyoutView)
    {
        if (GetValue(FlyoutBase.AttachedFlyoutProperty) is Flyout { Content: CombatantDetailsFlyoutView content } attachedFlyout)
        {
            flyout = attachedFlyout;
            flyoutView = content;
            return true;
        }

        flyout = null!;
        flyoutView = null!;
        return false;
    }
}
