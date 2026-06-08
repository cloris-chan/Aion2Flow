using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class ScenePlaybackWindow : Window
{
    private readonly AvaloniaFrameClockService _frameClock;
    private bool _frameClockAttached;

    public new ScenePlaybackViewModel? DataContext { get => (ScenePlaybackViewModel?)base.DataContext; set => base.DataContext = value; }

    public ScenePlaybackWindow()
    {
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        AvaloniaXamlLoader.Load(this);
    }

    public ScenePlaybackWindow(ScenePlaybackViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
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
}
