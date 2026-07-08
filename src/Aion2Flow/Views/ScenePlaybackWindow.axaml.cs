using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class ScenePlaybackWindow : Window
{
    private readonly AvaloniaFrameClockService _frameClock;
    private readonly UiScaleService _uiScale;
    private bool _frameClockAttached;

    public new ScenePlaybackViewModel? DataContext { get => (ScenePlaybackViewModel?)base.DataContext; set => base.DataContext = value; }

    public ScenePlaybackWindow()
    {
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        _uiScale = Ioc.Default.GetRequiredService<UiScaleService>();
        AvaloniaXamlLoader.Load(this);
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
}
