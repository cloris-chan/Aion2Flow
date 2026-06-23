using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class ScenePlaybackWindow : Window
{
    private const double DefaultDetailsWidth = 720;
    private const double MinimumDetailsWidth = 620;
    private const double DetailsSplitterWidth = 6;
    private const double OpenColumnSpacing = 10;

    private readonly AvaloniaFrameClockService _frameClock;
    private readonly UiScaleService _uiScale;
    private Grid? _rootLayout;
    private ColumnDefinition? _detailsSplitterColumn;
    private ColumnDefinition? _detailsPanelColumn;
    private ScenePlaybackViewModel? _observedViewModel;
    private double _detailsWidth = DefaultDetailsWidth;
    private bool _frameClockAttached;

    public new ScenePlaybackViewModel? DataContext { get => (ScenePlaybackViewModel?)base.DataContext; set => base.DataContext = value; }

    public ScenePlaybackWindow()
    {
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        _uiScale = Ioc.Default.GetRequiredService<UiScaleService>();
        AvaloniaXamlLoader.Load(this);
        _rootLayout = this.FindControl<Grid>("RootLayout");
        if (_rootLayout is { ColumnDefinitions.Count: >= 3 })
        {
            _detailsSplitterColumn = _rootLayout.ColumnDefinitions[1];
            _detailsPanelColumn = _rootLayout.ColumnDefinitions[2];
        }
        UpdateDetailsColumns();
    }

    public ScenePlaybackWindow(ScenePlaybackViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _uiScale.RegisterWindow(this);
        if (_frameClockAttached)
            return;

        _frameClockAttached = true;
        _frameClock.Attach(this);
    }

    protected override async void OnClosed(EventArgs e)
    {
        UnsubscribeDetailsVisibility();
        if (_frameClockAttached)
        {
            _frameClock.Detach(this);
            _frameClockAttached = false;
        }

        base.OnClosed(e);
        var dataContext = DataContext;
        DataContext = null;
        if (dataContext is not null)
            await dataContext.DisposeAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnsubscribeDetailsVisibility();
        SubscribeDetailsVisibility(DataContext);
        UpdateDetailsColumns();
    }

    private void TimelineSeekRequested(object? sender, PlaybackSeekRequestedEventArgs e)
    {
        DataContext?.RequestSeek(e.PositionMilliseconds);
    }

    private void CombatantRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PlaybackCombatantRowViewModel combatant })
            DataContext?.SelectCombatant(combatant);
    }

    private void CombatantExpandClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlaybackCombatantRowViewModel combatant })
        {
            DataContext?.ToggleCombatantExpansion(combatant);
            e.Handled = true;
        }
    }

    private void SubscribeDetailsVisibility(ScenePlaybackViewModel? viewModel)
    {
        if (viewModel is null)
            return;

        _observedViewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnsubscribeDetailsVisibility()
    {
        if (_observedViewModel is null)
            return;

        _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenePlaybackViewModel.IsCombatantDetailsVisible))
            UpdateDetailsColumns();
    }

    private void UpdateDetailsColumns()
    {
        if (_rootLayout is null || _detailsSplitterColumn is null || _detailsPanelColumn is null)
            return;

        CaptureDetailsWidth();
        if (DataContext?.IsCombatantDetailsVisible == true)
        {
            _detailsSplitterColumn.Width = new GridLength(DetailsSplitterWidth);
            _detailsPanelColumn.MinWidth = MinimumDetailsWidth;
            _detailsPanelColumn.Width = new GridLength(Math.Max(MinimumDetailsWidth, _detailsWidth));
            _rootLayout.ColumnSpacing = OpenColumnSpacing;
            Dispatcher.UIThread.Post(CaptureDetailsWidth, DispatcherPriority.Background);
            return;
        }

        _detailsPanelColumn.MinWidth = 0;
        _detailsPanelColumn.Width = new GridLength(0);
        _detailsSplitterColumn.Width = new GridLength(0);
        _rootLayout.ColumnSpacing = 0;
    }

    private void CaptureDetailsWidth()
    {
        if (_detailsPanelColumn is null)
            return;

        if (_detailsPanelColumn.ActualWidth >= MinimumDetailsWidth)
            _detailsWidth = _detailsPanelColumn.ActualWidth;
    }
}
