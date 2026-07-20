using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class AnimatedItemsViewTests
{
    private const double ItemHeight = 24;
    private const double ItemSpacing = 2;

    [Fact]
    public void WindowLayout_RealizesOnlyViewportAndOneOverscanRow()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var realized = GetRealizedContainers(view);

                Assert.IsAssignableFrom<Panel>(view);
                Assert.IsNotAssignableFrom<ListBox>(view);
                Assert.Equal(6, realized.Length);
                Assert.Equal(rows.Take(6), realized.Select(static container => container.Content));
                Assert.Empty(view.GetVisualDescendants().OfType<ListBoxItem>());
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void ScrollIntoView_ReusesContainersWithoutStaleContentSelectionOrAnimation()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.SelectedItem = rows[0];
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var initialContainers = GetRealizedContainers(view);
                var initialContent = initialContainers.Select(static container => container.Content).ToArray();

                var last = rows[^1];
                view.SelectedItem = last;
                view.ScrollIntoView(last);
                Dispatcher.UIThread.RunJobs();

                var finalContainers = GetRealizedContainers(view);

                Assert.Equal(6, finalContainers.Length);
                Assert.Contains(finalContainers, container => initialContainers.Any(initial => ReferenceEquals(initial, container)));
                Assert.DoesNotContain(finalContainers, container => initialContent.Any(item => ReferenceEquals(item, container.Content)));
                Assert.Single(finalContainers, container => container.IsSelected);
                Assert.Contains(finalContainers, container => ReferenceEquals(container.Content, last) && container.IsSelected);
                Assert.All(finalContainers, static container =>
                {
                    Assert.Equal(1, container.Opacity);
                    var transform = Assert.IsType<TranslateTransform>(container.RenderTransform);
                    Assert.Equal(0, transform.X);
                    Assert.Equal(0, transform.Y);
                });
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void FractionalScroll_ReusesTheFullViewportAndOverscanWindow()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.True(view.ScrollByRows(10.5));
                Dispatcher.UIThread.RunJobs();
                var initialContainers = GetRealizedContainers(view);
                Assert.Equal(8, initialContainers.Length);

                Assert.True(view.ScrollByRows(90));
                Dispatcher.UIThread.RunJobs();
                var finalContainers = GetRealizedContainers(view);
                Assert.Equal(8, finalContainers.Length);
                Assert.All(finalContainers, container => Assert.Contains(initialContainers, initial => ReferenceEquals(initial, container)));
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void ScrollIntoView_AnimatesOverlappingRowsToTheirNewPositions()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.MoveDuration = TimeSpan.FromMilliseconds(200);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var movingContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, rows[1]));
                Assert.Equal(ItemHeight + ItemSpacing, movingContainer.Bounds.Y);

                view.ScrollIntoView(rows[5]);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(0, movingContainer.Bounds.Y);
                var transform = Assert.IsType<TranslateTransform>(movingContainer.RenderTransform);
                Assert.Equal(0, transform.GetBaseValue(TranslateTransform.YProperty).Value);

                Thread.Sleep(60);
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(transform.Y, 0.1, ItemHeight + ItemSpacing - 0.1);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void RapidAnimatedScroll_RecyclesAnOutgoingRowOnlyAfterItLeavesTheViewport()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.MoveDuration = TimeSpan.FromMilliseconds(200);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var outgoingContainer = Assert.Single(
                    GetRealizedContainers(view),
                    container => ReferenceEquals(container.Content, rows[0]));
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());

                Assert.True(view.ScrollByRowsAnimated(1));
                Assert.True(view.ScrollByRowsAnimated(1));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(20));

                Assert.Contains(outgoingContainer, view.Children);
                Assert.True(ItemHeight - scrollBar.Value > 0);
                Assert.True(GetRealizedContainers(view).Length <= 8);

                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(200));

                Assert.DoesNotContain(
                    view.Children.OfType<AnimatedItemsViewItem>(),
                    container => ReferenceEquals(container.Content, rows[0]));
                Assert.True(ItemHeight - scrollBar.Value <= 0);
                Assert.True(GetRealizedContainers(view).Length <= 8);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void RepeatedAnimatedScrollInputContinuesFromThePreviousFrame()
    {
        AvaloniaTestHost.Run(() =>
        {
            var view = CreateView(CreateCollection(128), maxVisibleItems: 5);
            view.MoveDuration = TimeSpan.FromMilliseconds(200);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());

                Assert.True(view.ScrollByRowsAnimated(1));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(20));
                var offsetBeforeRepeatedInput = scrollBar.Value;

                Assert.True(view.ScrollByRowsAnimated(1));
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(36));

                Assert.True(
                    scrollBar.Value > offsetBeforeRepeatedInput,
                    "Repeated wheel input reset animation progress instead of continuing from the previous frame.");
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void CollectionMoveDuringAnimatedScrollRebasesThePendingTarget()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var movedRow = rows[4];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.MoveDuration = TimeSpan.FromMilliseconds(200);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());

                Assert.True(view.ScrollByRowsAnimated(2));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(20));

                using (rows.SuspendNotifications())
                {
                    rows.Remove(movedRow.Id);
                    rows.Insert(0, movedRow);
                }
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(200));

                Assert.Equal(3 * (ItemHeight + ItemSpacing), scrollBar.Value, precision: 6);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void ZeroMoveDurationCompletesAnActiveScrollWithAFiniteOffset()
    {
        AvaloniaTestHost.Run(() =>
        {
            var view = CreateView(CreateCollection(128), maxVisibleItems: 5);
            view.MoveDuration = TimeSpan.FromMilliseconds(200);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());

                Assert.True(view.ScrollByRowsAnimated(2));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                view.MoveDuration = TimeSpan.Zero;
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(1));

                Assert.True(double.IsFinite(scrollBar.Value));
                Assert.Equal(2 * (ItemHeight + ItemSpacing), scrollBar.Value, precision: 6);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void CollectionMove_PreservesRealizedOrderAndReferenceSelection()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var selected = rows[4];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.SelectedItem = selected;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                using (rows.SuspendNotifications())
                {
                    rows.Remove(selected.Id);
                    rows.Insert(0, selected);
                }
                Dispatcher.UIThread.RunJobs();

                var realized = GetRealizedContainers(view);
                Assert.Same(selected, view.SelectedItem);
                Assert.Same(selected, realized[0].Content);
                Assert.True(realized[0].IsSelected);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void CollectionMove_DoesNotRestoreSelectionThatWasReplacedInTheSameDispatcherCycle()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var previousSelection = rows[0];
            var selected = rows[4];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.SelectedItem = previousSelection;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                view.SelectedItem = selected;
                var moved = rows[0];
                using (rows.SuspendNotifications())
                {
                    rows.Remove(moved.Id);
                    rows.Insert(7, moved);
                }
                Dispatcher.UIThread.RunJobs();

                Assert.Same(selected, view.SelectedItem);
                Assert.Contains(GetRealizedContainers(view), container => ReferenceEquals(container.Content, selected) && container.IsSelected);
                Assert.DoesNotContain(GetRealizedContainers(view), container => ReferenceEquals(container.Content, previousSelection) && container.IsSelected);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void Remove_AnimatesTheVisibleContainerBeforeReturningItToThePool()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var removedItem = rows[0];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.AddRemoveDuration = TimeSpan.FromMilliseconds(200);
            view.AddRemoveOffset = 17;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var removedContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, removedItem));

                using (rows.SuspendNotifications())
                {
                    rows.Remove(removedItem.Id);
                    rows.Sort(static (left, right) => left.Id.CompareTo(right.Id));
                }
                Dispatcher.UIThread.RunJobs();

                Assert.Contains(removedContainer, view.Children);
                Assert.Same(removedItem, removedContainer.Content);
                Assert.False(removedContainer.IsHitTestVisible);
                var transform = Assert.IsType<TranslateTransform>(removedContainer.RenderTransform);
                AssertDepartureAnimates(view, removedContainer, transform, expectedOffset: 17);

                view.ScrollIntoView(rows[^1]);
                Dispatcher.UIThread.RunJobs();

                Assert.Contains(removedContainer, view.Children);
                WaitForDepartureCompletion(view, removedContainer);
                Assert.DoesNotContain(view.Children.OfType<AnimatedItemsViewItem>(), container => ReferenceEquals(container.Content, removedItem));
                Assert.Equal(6, GetRealizedContainers(view).Length);

                view.AddRemoveDuration = TimeSpan.FromMilliseconds(10);
                var timedRemoval = rows[^1];
                using (rows.SuspendNotifications())
                {
                    rows.Remove(timedRemoval.Id);
                    rows.Sort(static (left, right) => left.Id.CompareTo(right.Id));
                }
                Dispatcher.UIThread.RunJobs();
                var timedContainer = Assert.Single(view.Children.OfType<AnimatedItemsViewItem>(), container => ReferenceEquals(container.Content, timedRemoval));
                WaitForDepartureCompletion(view, timedContainer);
                Assert.DoesNotContain(view.Children.OfType<AnimatedItemsViewItem>(), container => ReferenceEquals(container.Content, timedRemoval));
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void ResetBatchRemove_AnimatesTheVisibleContainerBeforeReturningItToThePool()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            rows.ResetThreshold = 1;
            var removedItem = rows[0];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.AddRemoveDuration = TimeSpan.FromMilliseconds(200);
            view.AddRemoveOffset = 17;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var removedContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, removedItem));

                using (rows.SuspendNotifications())
                {
                    rows.Remove(removedItem.Id);
                    rows.Sort(static (left, right) => right.Id.CompareTo(left.Id));
                }
                Dispatcher.UIThread.RunJobs();

                Assert.Contains(removedContainer, view.Children);
                Assert.Same(removedItem, removedContainer.Content);
                Assert.False(removedContainer.IsHitTestVisible);
                var transform = Assert.IsType<TranslateTransform>(removedContainer.RenderTransform);

                Thread.Sleep(250);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(removedContainer, view.Children);

                AssertDepartureAnimates(view, removedContainer, transform, expectedOffset: 17);
                WaitForDepartureCompletion(view, removedContainer);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void Remove_OfAnOverscanRowDoesNotCreateAVisibleDeparture()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var removedItem = rows[5];
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var overscanContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, removedItem));

                using (rows.SuspendNotifications())
                    rows.Remove(removedItem.Id);
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(view.Children.OfType<AnimatedItemsViewItem>(), container => ReferenceEquals(container.Content, removedItem));
                Assert.True(overscanContainer.IsHitTestVisible);
                Assert.Equal(6, GetRealizedContainers(view).Length);
                Assert.Equal(GetViewportHeight(5), view.DesiredSize.Height);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void Remove_AfterPendingScrollUsesCurrentVirtualGeometry()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var removedItem = rows[0];
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var previousContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, removedItem));

                Assert.True(view.ScrollByRows(1));
                using (rows.SuspendNotifications())
                    rows.Remove(removedItem.Id);
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(view.Children.OfType<AnimatedItemsViewItem>(), container => ReferenceEquals(container.Content, removedItem));
                Assert.True(previousContainer.IsHitTestVisible);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void RemoveLastItem_ShowsTheEmptyTemplateAfterTheDepartureCompletes()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(1);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.AddRemoveDuration = TimeSpan.FromMilliseconds(1);
            view.EmptyTemplate = new FuncDataTemplate<object>(static (_, _) => new Border { Height = 32 });
            var window = CreateSizeToContentWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                using (rows.SuspendNotifications())
                    rows.Remove(rows[0].Id);
                Dispatcher.UIThread.RunJobs();
                for (var attempt = 0; attempt < 20 && view.DesiredSize.Height != 32; attempt++)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal(32, view.DesiredSize.Height);
                Assert.Equal(32, window.ClientSize.Height);
                Assert.IsType<Border>(Assert.Single(view.Children));
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(128, 50)]
    public void CombatantSort_ReconcilesTheWindowAndPreservesReferenceSelection(int rowCount, int resetThreshold)
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(rowCount);
            rows.ResetThreshold = resetThreshold;
            var selected = rows[4];
            var view = CreateView(rows, maxVisibleItems: 5);
            view.SelectedItem = selected;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                rows.Sort(static (left, right) => right.Id.CompareTo(left.Id));
                Dispatcher.UIThread.RunJobs();

                var realized = GetRealizedContainers(view);
                Assert.Equal(6, realized.Length);
                Assert.Equal(rowCount - 1, Assert.IsType<ReferenceRow>(realized[0].Content).Id);
                Assert.Same(selected, view.SelectedItem);

                view.ScrollIntoView(selected);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(GetRealizedContainers(view), container => ReferenceEquals(container.Content, selected) && container.IsSelected);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void InsertAndRemove_UpdateSizeToContentHeightInOneLayoutCycle()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(1);
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateSizeToContentWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AssertLayoutHeight(view, window, GetViewportHeight(1));

                using (rows.SuspendNotifications())
                    rows.Insert(0, new ReferenceRow(100));
                Dispatcher.UIThread.RunJobs();
                AssertLayoutHeight(view, window, GetViewportHeight(2));

                using (rows.SuspendNotifications())
                    rows.Remove(100);
                Dispatcher.UIThread.RunJobs();
                AssertLayoutHeight(view, window, GetViewportHeight(1));
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void MaxVisibleItems_UpdatesDesiredHeightAndKeepsRealizationBounded()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(GetViewportHeight(5), view.DesiredSize.Height);
                Assert.Equal(6, GetRealizedContainers(view).Length);

                view.MaxVisibleItems = 10;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(GetViewportHeight(10), view.DesiredSize.Height);
                Assert.Equal(11, GetRealizedContainers(view).Length);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void VerticalScrollBar_AutoReflectsViewportAndOffset()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());
                Assert.True(scrollBar.IsVisible);
                Assert.Equal(GetViewportHeight(5), scrollBar.ViewportSize);
                Assert.Equal((32 * (ItemHeight + ItemSpacing)) - ItemSpacing - GetViewportHeight(5), scrollBar.Maximum);

                Assert.True(view.ScrollByRows(2.5));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2.5 * (ItemHeight + ItemSpacing), scrollBar.Value);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void VerticalScrollBar_VisibleWithoutOverflowAndDisabledPreventsScrolling()
    {
        AvaloniaTestHost.Run(() =>
        {
            var view = CreateView(CreateCollection(1), maxVisibleItems: 5);
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());
                Assert.True(scrollBar.IsVisible);
                Assert.Equal(0, scrollBar.Maximum);

                view.ItemsSource = CreateCollection(32);
                view.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.ScrollByRows(2));
                Dispatcher.UIThread.RunJobs();

                view.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(view.Children.OfType<ScrollBar>());
                Assert.False(view.ScrollByRows(1));
                Assert.Equal(0, GetRealizedContainers(view)[0].Bounds.Y);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void FollowTailTracksAppendsUntilTheUserScrollsAwayAndResumesAtTheEnd()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(32);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.FollowTail = true;
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);

                rows.Add(new ReferenceRow(rows.Count));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);

                Assert.True(view.ScrollByRows(-2));
                Dispatcher.UIThread.RunJobs();
                var detachedOffset = scrollBar.Value;
                Assert.True(detachedOffset < scrollBar.Maximum);

                rows.Add(new ReferenceRow(rows.Count));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(detachedOffset, scrollBar.Value, precision: 6);

                Assert.True(view.ScrollByRowsAnimated(10_000));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                rows.Add(new ReferenceRow(rows.Count));
                view.AdvanceScrollAnimation(view.MoveDuration);
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);

                rows.Add(new ReferenceRow(rows.Count));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);

                Assert.True(view.ScrollByRows(-2));
                rows.Clear();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, scrollBar.Value);
                Assert.Equal(0, scrollBar.Maximum);

                using (rows.SuspendNotifications())
                {
                    for (var index = 0; index < 32; index++)
                        rows.Add(new ReferenceRow(1_000 + index));
                }
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);

                view.ItemsSource = CreateCollection(48);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 6);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void FollowTailAnimationRebasesWhenTheViewportExpands()
    {
        AvaloniaTestHost.Run(() =>
        {
            var view = CreateView(CreateCollection(128), maxVisibleItems: 10);
            view.FollowTail = true;
            view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                view.InvalidateMeasure();
                view.Measure(new Size(640, 80));
                view.Arrange(new Rect(0, 0, 640, 80));
                var scrollBar = Assert.Single(view.Children.OfType<ScrollBar>());

                Assert.True(view.ScrollByRows(-5));
                Assert.True(view.ScrollByRowsAnimated(10_000));
                view.AdvanceScrollAnimation(TimeSpan.Zero);
                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(50));

                view.InvalidateMeasure();
                view.Measure(new Size(640, 200));
                view.Arrange(new Rect(0, 0, 640, 200));
                var expandedViewportMaximum = scrollBar.Maximum;
                Assert.Equal(expandedViewportMaximum, scrollBar.Value, precision: 6);

                view.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(66));

                Assert.Equal(expandedViewportMaximum, scrollBar.Value, precision: 6);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void SelectionDisabled_IgnoresContainerSelectionRequests()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(2);
            var view = CreateView(rows, maxVisibleItems: 5);
            view.SelectedItem = rows[0];
            view.IsSelectionEnabled = false;

            view.SelectItem(rows[1]);

            Assert.Same(rows[0], view.SelectedItem);
            Assert.DoesNotContain(GetRealizedContainers(view), static container => container.IsSelected);
        });
    }

    [Fact]
    public void InsertAboveViewport_PreservesTheVisibleScrollAnchor()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(128);
            var view = CreateView(rows, maxVisibleItems: 5);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                view.ScrollIntoView(rows[64]);
                Dispatcher.UIThread.RunJobs();
                var anchor = GetRealizedContainers(view).First(static container => container.Bounds.Y >= -ItemSpacing);
                var anchorItem = anchor.Content;
                var anchorTop = anchor.Bounds.Y;

                using (rows.SuspendNotifications())
                    rows.Insert(0, new ReferenceRow(1000));
                Dispatcher.UIThread.RunJobs();

                var anchoredContainer = Assert.Single(GetRealizedContainers(view), container => ReferenceEquals(container.Content, anchorItem));
                Assert.Equal(anchorTop, anchoredContainer.Bounds.Y, precision: 6);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void OverlayTheme_UsesCustomContainersAndShowsTheEmptyTemplate()
    {
        AvaloniaTestHost.Run(() =>
        {
            var style = new StyleInclude(new Uri("avares://Aion2Flow/"))
            {
                Source = new Uri("avares://Aion2Flow/Styles/OverlayTheme.axaml")
            };
            Application.Current!.Styles.Add(style);
            var emptyTemplate = new FuncDataTemplate<object>(static (_, _) => new Border { Height = 32 });
            var view = CreateView(CreateCollection(0), maxVisibleItems: 5);
            view.EmptyTemplate = emptyTemplate;
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(32, view.DesiredSize.Height);
                Assert.IsType<Border>(Assert.Single(view.Children));

                view.ItemsSource = CreateCollection(1);
                Dispatcher.UIThread.RunJobs();

                Assert.IsType<AnimatedItemsViewItem>(Assert.Single(view.Children));
                Assert.Empty(view.GetVisualDescendants().OfType<ListBoxItem>());
            }
            finally
            {
                Close(window);
                Application.Current.Styles.Remove(style);
            }
        });
    }

    [Fact]
    public void PrebuiltItems_AssignmentAndLayoutAllocateLessThanTwoMegabytes()
    {
        AvaloniaTestHost.Run(() =>
        {
            var rows = CreateCollection(4_096);
            var view = CreateView(CreateCollection(0), maxVisibleItems: 10);
            var window = CreateFixedWindow(view);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                view.ItemsSource = rows;
                view.Measure(new Size(640, 500));
                view.Arrange(new Rect(0, 0, 640, GetViewportHeight(10)));
                Dispatcher.UIThread.RunJobs();
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

                Assert.True(allocatedBytes < 2 * 1024 * 1024, $"Allocated {allocatedBytes:N0} bytes.");
                Assert.Equal(11, GetRealizedContainers(view).Length);
                GC.KeepAlive(rows);
            }
            finally
            {
                Close(window);
            }
        });
    }

    private static AnimatedItemsView CreateView(KeyedObservableCollection<int, ReferenceRow> rows, int maxVisibleItems)
        => new()
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<ReferenceRow>(static (_, _) => new Border()),
            ItemHeight = ItemHeight,
            ItemSpacing = ItemSpacing,
            MaxVisibleItems = maxVisibleItems,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };

    private static Window CreateFixedWindow(Control content)
        => new()
        {
            Width = 640,
            Height = 500,
            Content = content
        };

    private static Window CreateSizeToContentWindow(Control content)
        => new()
        {
            Width = 640,
            SizeToContent = SizeToContent.Height,
            Content = content
        };

    private static AnimatedItemsViewItem[] GetRealizedContainers(AnimatedItemsView view)
        => view.Children.OfType<AnimatedItemsViewItem>().OrderBy(static container => container.Bounds.Y).ToArray();

    private static ReferenceRow[] CreateRows(int count)
        => Enumerable.Range(0, count).Select(static index => new ReferenceRow(index)).ToArray();

    private static KeyedObservableCollection<int, ReferenceRow> CreateCollection(int count)
        => new(static row => row.Id, CreateRows(count));

    private static double GetViewportHeight(int visibleCount)
        => (visibleCount * ItemHeight) + ((visibleCount - 1) * ItemSpacing);

    private static void AssertLayoutHeight(AnimatedItemsView view, Window window, double expected)
    {
        Assert.Equal(expected, view.DesiredSize.Height);
        Assert.Equal(expected, view.Bounds.Height);
        Assert.Equal(expected, window.ClientSize.Height);
    }

    private static void AssertDepartureAnimates(AnimatedItemsView view, AnimatedItemsViewItem container, TranslateTransform transform, double expectedOffset)
    {
        var observedIntermediateFrame = false;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (!view.Children.Contains(container))
                break;

            if (container.Opacity is > 0.05 and < 0.95 && transform.X > 0.05 && transform.X < expectedOffset - 0.05)
            {
                observedIntermediateFrame = true;
                break;
            }

            Thread.Sleep(16);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(observedIntermediateFrame, $"Departure did not expose an intermediate frame. Opacity={container.Opacity}, X={transform.X}.");
    }

    private static void WaitForDepartureCompletion(AnimatedItemsView view, AnimatedItemsViewItem container)
    {
        for (var attempt = 0; attempt < 80 && view.Children.Contains(container); attempt++)
        {
            Thread.Sleep(16);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.DoesNotContain(container, view.Children);
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class ReferenceRow(int id)
    {
        public int Id { get; } = id;
    }
}
