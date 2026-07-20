using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Views;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PlaybackEventWindowLayoutTests
{
    [Fact]
    public void EventWindowUsesTailFollowingAnimatedVirtualization()
    {
        AvaloniaTestHost.Run(() =>
        {
            ViewTestServices.Get();
            var view = new PlaybackEventWindowView();
            var events = view.FindControl<AnimatedItemsView>("EventRows");

            Assert.NotNull(events);
            Assert.True(events.FollowTail);
            Assert.False(events.IsSelectionEnabled);
            Assert.Equal(28, events.ItemHeight);
            Assert.Equal(32, events.MaxVisibleItems);
            Assert.Equal(ScrollBarVisibility.Visible, events.VerticalScrollBarVisibility);
            Assert.Empty(view.GetLogicalDescendants().OfType<ListBox>());
        });
    }
}
