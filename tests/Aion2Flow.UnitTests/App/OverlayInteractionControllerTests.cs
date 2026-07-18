using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Overlay;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.Views;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class OverlayInteractionControllerTests
{
    [Fact]
    public void Cycle_FollowsInteractiveClickThroughHiddenOrder()
    {
        var controller = new OverlayInteractionController();
        var observed = new List<OverlayInteractionMode>();
        controller.ModeChanged += observed.Add;

        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);

        controller.Cycle();
        controller.Cycle();
        controller.Cycle();

        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);
        Assert.Equal(
            [
                OverlayInteractionMode.ClickThrough,
                OverlayInteractionMode.Hidden,
                OverlayInteractionMode.Interactive
            ],
            observed);
    }

    [Theory]
    [InlineData(OverlayInteractionMode.Interactive, false, (int)OverlayWindowInputState.Interactive, false, false)]
    [InlineData(OverlayInteractionMode.ClickThrough, false, (int)OverlayWindowInputState.ClickThroughArmed, false, false)]
    [InlineData(OverlayInteractionMode.ClickThrough, true, (int)OverlayWindowInputState.ClickThroughActive, true, true)]
    [InlineData(OverlayInteractionMode.Hidden, false, (int)OverlayWindowInputState.Hidden, true, false)]
    [InlineData(OverlayInteractionMode.Hidden, true, (int)OverlayWindowInputState.Hidden, true, false)]
    public void WindowInputState_MapsModeAndPointerPresence(
        OverlayInteractionMode mode,
        bool isPointerInside,
        int expected,
        bool requiresInputTransparency,
        bool shouldPollCursor)
    {
        var state = OverlayWindowInputStateLogic.EnterMode(mode, isPointerInside);

        Assert.Equal((OverlayWindowInputState)expected, state);
        Assert.Equal(requiresInputTransparency, state.RequiresInputTransparency());
        Assert.Equal(shouldPollCursor, state.ShouldPollCursor());
    }

    [Theory]
    [InlineData(100, 100, 103, 103, 4, 4, false)]
    [InlineData(100, 100, 104, 100, 4, 4, true)]
    [InlineData(100, 100, 100, 104, 4, 4, true)]
    [InlineData(100, 100, 96, 100, 4, 4, true)]
    public void OverlayPinDrag_UsesCenteredSystemDragThreshold(
        int startX,
        int startY,
        int currentX,
        int currentY,
        int thresholdWidth,
        int thresholdHeight,
        bool expected)
    {
        Assert.Equal(
            expected,
            OverlayPinWindow.HasExceededDragThreshold(
                new Point(startX, startY),
                new Point(currentX, currentY),
                new Size(thresholdWidth, thresholdHeight)));
    }

    [Theory]
    [InlineData(100, 200, 400, 500, 420, 530, 120, 230)]
    [InlineData(-800, -400, -1000, -500, -1030, -480, -830, -380)]
    public void OverlayPinDrag_MapsPhysicalPointerDeltaToOwnerPosition(
        int ownerX,
        int ownerY,
        int pointerStartX,
        int pointerStartY,
        int pointerCurrentX,
        int pointerCurrentY,
        int expectedX,
        int expectedY)
    {
        Assert.Equal(
            new PixelPoint(expectedX, expectedY),
            OverlayPinWindow.CalculateOwnerPosition(
                new PixelPoint(ownerX, ownerY),
                new PixelPoint(pointerStartX, pointerStartY),
                new PixelPoint(pointerCurrentX, pointerCurrentY)));
    }

    [Fact]
    public void NativeWindowStyles_ToggleInputTransparencyAndNoActivateOnRealHwnd()
    {
        AvaloniaTestHost.Run(() =>
        {
            var window = new Window
            {
                Width = 120,
                Height = 80,
                ShowActivated = false,
                ShowInTaskbar = false
            };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.NotEqual(0, window.TryGetPlatformHandle()?.Handle ?? 0);
                Assert.True(NativeOverlayWindowStyles.TryGetInputTransparentStyles(window, out var wasLayered, out _));
                Assert.True(NativeOverlayWindowStyles.SetInputTransparent(window, true));
                Assert.True(NativeOverlayWindowStyles.TryGetInputTransparentStyles(window, out var isLayered, out var isTransparent));
                Assert.True(isLayered);
                Assert.True(isTransparent);
                Assert.True(NativeOverlayWindowStyles.SetNoActivate(window, true));
                Assert.True(NativeOverlayWindowStyles.SetInputTransparent(window, false));
                Assert.True(NativeOverlayWindowStyles.TryGetInputTransparentStyles(window, out isLayered, out isTransparent));
                Assert.Equal(wasLayered, isLayered);
                Assert.False(isTransparent);
                Assert.True(NativeOverlayWindowStyles.SetNoActivate(window, false));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void NativeWindowStyles_ReapplyInputTransparencyAfterWindowRestore()
    {
        AvaloniaTestHost.Run(() =>
        {
            var window = new Window
            {
                Width = 120,
                Height = 80,
                ShowActivated = false,
                ShowInTaskbar = false
            };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(NativeOverlayWindowStyles.SetInputTransparent(window, true));

                window.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                window.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();

                Assert.True(NativeOverlayWindowStyles.SetInputTransparent(window, true));
                Assert.True(NativeOverlayWindowStyles.TryGetInputTransparentStyles(window, out var isLayered, out var isTransparent));
                Assert.True(isLayered);
                Assert.True(isTransparent);
                Assert.True(NativeOverlayWindowStyles.SetInputTransparent(window, false));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void OverlayPinWindow_AppliesExactScreenBoundsAtEverySupportedScale(int scalePercent)
    {
        AvaloniaTestHost.Run(() =>
        {
            var settings = new SettingsService(Path.Combine(Path.GetTempPath(), $"aion2flow-overlay-{Guid.NewGuid():N}.json"));
            settings.Update(value => value.UiScalePercent = scalePercent);
            using var uiScale = new UiScaleService(settings);
            using var localization = new LocalizationService(new LanguageService());
            var window = new OverlayPinWindow(new OverlayInteractionController(), localization, uiScale);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.True(window.ApplyNativeWindowStyle());
                var targetSize = (int)Math.Round(26d * scalePercent / 100d * window.RenderScaling);
                Assert.True(window.SetScreenBounds(new PixelRect(100, 100, targetSize, targetSize)));
                Dispatcher.UIThread.RunJobs();

                Assert.NotEqual(0, window.TryGetPlatformHandle()?.Handle ?? 0);
                Assert.Equal(new PixelPoint(100, 100), window.Position);
                Assert.Equal(targetSize / window.RenderScaling, window.ClientSize.Width, 6);
                Assert.Equal(targetSize / window.RenderScaling, window.ClientSize.Height, 6);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

}
