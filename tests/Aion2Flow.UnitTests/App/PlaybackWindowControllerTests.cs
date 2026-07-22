using Avalonia.Controls;
using Avalonia.Threading;
using Cloris.Aion2Flow.Views;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PlaybackWindowControllerTests
{
    [Fact]
    public void ExistingWindowIsActivatedAndControllerClearsAfterClose()
    {
        AvaloniaTestHost.Run(() =>
        {
            var owner = new Window();
            var playback = new Window();
            var controller = new PlaybackWindowController();
            owner.Show();

            try
            {
                controller.Show(playback, owner);

                playback.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.True(controller.TryActivate());
                Assert.Equal(WindowState.Normal, playback.WindowState);

                playback.Close();
                Dispatcher.UIThread.RunJobs();

                Assert.False(controller.TryActivate());
            }
            finally
            {
                if (playback.IsVisible)
                    playback.Close();
                if (owner.IsVisible)
                    owner.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void CloseClosesTrackedWindow()
    {
        AvaloniaTestHost.Run(() =>
        {
            var owner = new Window();
            var playback = new Window();
            var controller = new PlaybackWindowController();
            owner.Show();

            try
            {
                controller.Show(playback, owner);
                controller.Close();

                Assert.False(controller.TryActivate());
                Assert.False(playback.IsVisible);
            }
            finally
            {
                if (playback.IsVisible)
                    playback.Close();
                if (owner.IsVisible)
                    owner.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
