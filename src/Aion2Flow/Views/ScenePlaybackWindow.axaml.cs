using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class ScenePlaybackWindow : Window
{
    public new ScenePlaybackViewModel? DataContext { get => (ScenePlaybackViewModel?)base.DataContext; set => base.DataContext = value; }

    public ScenePlaybackWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ScenePlaybackWindow(ScenePlaybackViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override async void OnClosed(EventArgs e)
    {
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
