using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class PlaybackEventWindowView : UserControl
{
    public PlaybackEventWindowView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void EventRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PlaybackEventRowViewModel row } &&
            DataContext is ScenePlaybackViewModel viewModel)
        {
            viewModel.RequestSeek(row.PositionMilliseconds);
        }
    }
}
