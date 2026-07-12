using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PlaybackEventListVirtualizationTests
{
    [Fact]
    public void EventList_RealizesOnlyViewportRows()
    {
        AvaloniaTestHost.EnsureInitialized();
        const int itemCount = 96;
        var list = new ListBox
        {
            ItemsSource = Enumerable.Range(0, itemCount).ToArray(),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.5 }),
            ItemTemplate = new FuncDataTemplate<int>((_, _) => new Border { Height = 24 }),
        };
        var window = new Window
        {
            Width = 640,
            Height = 220,
            Content = list,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var panel = Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
            var realizedCount = list.GetRealizedContainers().Count();
            Assert.InRange(realizedCount, 1, itemCount - 1);
            Assert.Equal(0.5, panel.CacheLength);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
