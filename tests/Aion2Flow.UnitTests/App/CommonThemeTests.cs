using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CommonThemeTests
{
    [Theory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void ScrollbarTrackAndThumbMoveTheViewport(Orientation orientation)
    {
        AvaloniaTestHost.Run(() =>
        {
            var scroller = new ScrollViewer
            {
                Classes = { "AppScrollViewer" },
                Content = new Border { Width = 1200, Height = 1200 },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var window = new Window { Width = 480, Height = 360, Content = scroller };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var scrollbar = scroller.GetTemplateDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == orientation);
                var pageButtons = scrollbar.GetTemplateDescendants().OfType<RepeatButton>();
                pageButtons.Single(button => button.Name == "PART_PageDownButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(scrollbar.Value > 0);
                Assert.Equal(scrollbar.Value, orientation == Orientation.Horizontal ? scroller.Offset.X : scroller.Offset.Y);

                pageButtons.Single(button => button.Name == "PART_PageUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, scrollbar.Value);

                var thumb = scrollbar.GetTemplateDescendants().OfType<Thumb>().Single();
                thumb.RaiseEvent(new VectorEventArgs
                {
                    RoutedEvent = Thumb.DragDeltaEvent,
                    Vector = orientation == Orientation.Horizontal ? new Vector(36, 0) : new Vector(0, 36)
                });
                Assert.True(scrollbar.Value > 0);
                Assert.Equal(scrollbar.Value, orientation == Orientation.Horizontal ? scroller.Offset.X : scroller.Offset.Y);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
