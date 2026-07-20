using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SmoothScrollBehaviorTests
{
    [Fact]
    public void PointerWheel_StartsAnimationWithoutJumpingImmediately()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var content = new Border { Width = 1_000, Height = 1_000 };
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            var window = new Window { Width = 300, Height = 300, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var args = CreateWheelEvent(content, window, new Vector(0, -1));

                content.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Equal(0, viewer.Offset.Y);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.Zero);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.FromMilliseconds(75));
                Assert.True(viewer.Offset.Y > 0);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void PointerWheel_ContentHandlerRegisteredBeforeLoadedKeepsPriority()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var contentHandled = false;
            var content = new Border { Width = 1_000, Height = 1_000 };
            content.AddHandler(
                InputElement.PointerWheelChangedEvent,
                (_, e) =>
                {
                    contentHandled = true;
                    e.Handled = true;
                });
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            var window = new Window { Width = 300, Height = 300, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var args = CreateWheelEvent(content, window, new Vector(0, -1));

                content.RaiseEvent(args);

                Assert.True(contentHandled);
                Assert.True(args.Handled);
                Assert.Equal(default, viewer.Offset);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void ShiftPointerWheel_StartsHorizontalAnimation()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var content = new Border { Width = 1_000, Height = 100 };
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 100,
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var window = new Window { Width = 300, Height = 200, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var args = CreateWheelEvent(content, window, new Vector(0, -1), KeyModifiers.Shift);

                content.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Equal(default, viewer.Offset);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.Zero);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.FromMilliseconds(75));
                Assert.True(viewer.Offset.X > 0);
                Assert.Equal(0, viewer.Offset.Y);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void ExternalOffsetChange_CancelsActiveWheelAnimation()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var content = new Border { Width = 1, Height = 1_000 };
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            var window = new Window { Width = 300, Height = 300, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                content.RaiseEvent(CreateWheelEvent(content, window, new Vector(0, -1)));
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.Zero);

                viewer.Offset = new Vector(0, 300);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.FromMilliseconds(75));

                Assert.Equal(new Vector(0, 300), viewer.Offset);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void WheelAnimation_PreservesOffsetBinding()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var source = new OffsetSource();
            var content = new Border { Width = 1, Height = 1_000 };
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            using var binding = viewer.Bind(ScrollViewer.OffsetProperty, source.GetObservable(OffsetSource.ValueProperty));
            var window = new Window { Width = 300, Height = 300, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                content.RaiseEvent(CreateWheelEvent(content, window, new Vector(0, -1)));
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.Zero);
                SmoothScrollBehavior.AdvanceAnimation(viewer, TimeSpan.FromMilliseconds(75));

                source.Value = new Vector(0, 300);

                Assert.Equal(new Vector(0, 300), viewer.Offset);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void ShiftPointerWheel_IsNotConsumedByPendingVerticalAnimation()
    {
        AvaloniaTestHost.Run(() =>
        {
            SmoothScrollBehavior.Initialize();
            var content = new Border { Width = 1, Height = 1_000 };
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var window = new Window { Width = 300, Height = 300, Content = viewer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                content.RaiseEvent(CreateWheelEvent(content, window, new Vector(0, -1)));
                var horizontalArgs = CreateWheelEvent(content, window, new Vector(0, -1), KeyModifiers.Shift);

                content.RaiseEvent(horizontalArgs);

                Assert.False(horizontalArgs.Handled);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void InterpolateOffset_UsesClampedEaseOutCubicMotion()
    {
        var start = new Vector(10, 20);
        var target = new Vector(110, 220);

        Assert.Equal(start, SmoothScrollBehavior.InterpolateOffset(start, target, -1));
        Assert.Equal(target, SmoothScrollBehavior.InterpolateOffset(start, target, 2));

        var midpoint = SmoothScrollBehavior.InterpolateOffset(start, target, 0.5);
        Assert.Equal(97.5, midpoint.X);
        Assert.Equal(195, midpoint.Y);
    }

    [Fact]
    public void CalculateTarget_MapsVerticalAndHorizontalWheelInput()
    {
        var extent = new Size(1_000, 1_000);
        var viewport = new Size(200, 200);
        var origin = new Vector(200, 200);

        var vertical = SmoothScrollBehavior.CalculateTarget(origin, extent, viewport, new Vector(0, -1), KeyModifiers.None);
        var horizontal = SmoothScrollBehavior.CalculateTarget(origin, extent, viewport, new Vector(-1, 0), KeyModifiers.None);
        var shifted = SmoothScrollBehavior.CalculateTarget(origin, extent, viewport, new Vector(0, -1), KeyModifiers.Shift);

        Assert.Equal(new Vector(200, 256), vertical);
        Assert.Equal(new Vector(256, 200), horizontal);
        Assert.Equal(horizontal, shifted);
    }

    [Fact]
    public void CalculateTarget_PreservesNativeModifierAndFlowDirectionSemantics()
    {
        var extent = new Size(1_000, 1_000);
        var viewport = new Size(200, 200);
        var origin = new Vector(200, 200);

        var combinedModifiers = SmoothScrollBehavior.CalculateTarget(
            origin,
            extent,
            viewport,
            new Vector(0, -1),
            KeyModifiers.Control | KeyModifiers.Shift);
        var rightToLeft = SmoothScrollBehavior.CalculateTarget(
            origin,
            extent,
            viewport,
            new Vector(-1, 0),
            KeyModifiers.None,
            FlowDirection.RightToLeft);

        Assert.Equal(new Vector(200, 256), combinedModifiers);
        Assert.Equal(new Vector(144, 200), rightToLeft);
    }

    [Fact]
    public void CalculateTarget_ClampsAtScrollableBounds()
    {
        var extent = new Size(500, 600);
        var viewport = new Size(200, 200);

        var minimum = SmoothScrollBehavior.CalculateTarget(default, extent, viewport, new Vector(10, 10), KeyModifiers.None);
        var maximum = SmoothScrollBehavior.CalculateTarget(new Vector(300, 400), extent, viewport, new Vector(-10, -10), KeyModifiers.None);

        Assert.Equal(default, minimum);
        Assert.Equal(new Vector(300, 400), maximum);
    }

    private static PointerWheelEventArgs CreateWheelEvent(
        Control source,
        Window root,
        Vector delta,
        KeyModifiers modifiers = KeyModifiers.None)
        => new(
            source,
            new Pointer(1, PointerType.Mouse, true),
            root,
            new Point(100, 100),
            0,
            new PointerPointProperties(),
            modifiers,
            delta);

    private sealed class OffsetSource : AvaloniaObject
    {
        public static readonly StyledProperty<Vector> ValueProperty =
            AvaloniaProperty.Register<OffsetSource, Vector>(nameof(Value));

        public Vector Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
