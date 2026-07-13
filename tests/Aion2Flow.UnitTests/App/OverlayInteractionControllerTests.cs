using System.Xml.Linq;
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
    public void OverlayTheme_SeparatesBackgroundAndForegroundClickThroughOpacity()
    {
        var root = FindRepositoryRoot();
        var themePath = Path.Combine(root, "src", "Aion2Flow", "Styles", "OverlayTheme.axaml");
        var viewPath = Path.Combine(root, "src", "Aion2Flow", "Views", "MainWindow.axaml");
        var theme = File.ReadAllText(themePath);
        var view = File.ReadAllText(viewPath);
        var themeDocument = XDocument.Load(themePath);
        var viewDocument = XDocument.Load(viewPath);
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var foregroundOpacityStyle = themeDocument
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value.Contains("click-through.cursor-over .MainHudForeground", StringComparison.Ordinal) == true);
        var foregroundOpacity = foregroundOpacityStyle
            .Elements(avalonia + "Setter")
            .Single(element => element.Attribute("Property")?.Value == "Opacity")
            .Attribute("Value")?.Value;
        var pinSlot = viewDocument
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value == "OverlayPinSlot");

        Assert.Contains("Grid.MainHudShell.click-through Border.MainHudHeaderBackdrop", theme, StringComparison.Ordinal);
        Assert.Contains("Grid.MainHudShell.click-through.cursor-over Border.MainHudHeaderBackdrop", theme, StringComparison.Ordinal);
        Assert.Contains("Grid.MainHudShell.click-through.cursor-over .MainHudForeground", theme, StringComparison.Ordinal);
        Assert.Equal("0.4", foregroundOpacity);
        Assert.Contains("Classes=\"MainHudHeaderBackdrop\"", view, StringComparison.Ordinal);
        Assert.Contains("Classes=\"MainHudFooter MainHudForeground\"", view, StringComparison.Ordinal);
        Assert.Contains("TitleBarActions", pinSlot.Parent?.Attribute("Classes")?.Value, StringComparison.Ordinal);

        var bossProgressBar = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Controls", "BossFocusProgressBar.cs"));
        var questHighlight = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Controls", "QuestSelectionHighlight.cs"));
        Assert.Contains("lease.CurrentOpacity", bossProgressBar, StringComparison.Ordinal);
        Assert.Contains("lease.CurrentOpacity", questHighlight, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayCursorTracking_CachesEventDrivenWindowGeometry()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "MainWindow.axaml.cs"));
        var mainWindowView = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "MainWindow.axaml"));
        var pinWindow = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "OverlayPinWindow.axaml.cs"));
        var nativeMethods = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "NativeMethods.txt"));

        Assert.DoesNotContain("GetWindowRect", nativeMethods, StringComparison.Ordinal);
        Assert.DoesNotContain("PInvoke.GetWindowRect", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PositionChanged += OnWindowPositionChanged", mainWindow, StringComparison.Ordinal);
        Assert.Contains("change.Property == ClientSizeProperty", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ScalingChanged += OnWindowScalingChanged", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TryGetCursorPosition", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MainHudShell.PointToScreen", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OverlayPinSlot.PointToScreen", mainWindow, StringComparison.Ordinal);
        Assert.Contains("pinWindow.SetScreenBounds", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("pinWindow.ClientSize.Width", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PointerEntered=\"MainHudShellPointerEntered\"", mainWindowView, StringComparison.Ordinal);
        Assert.Contains("if (!_windowInputState.ShouldPollCursor())", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TryApplyWindowInputState(OverlayWindowInputState.ClickThroughArmed)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TryApplyWindowInputState(OverlayWindowInputState.ClickThroughActive)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCursorInsideOverlay", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ScalingChanged += OnScalingChanged", pinWindow, StringComparison.Ordinal);
        Assert.Contains("PinButton.AddHandler(PointerCaptureLostEvent", pinWindow, StringComparison.Ordinal);
        Assert.Contains("AddHandler(PointerReleasedEvent, PinButtonPointerReleasedPreview, RoutingStrategies.Tunnel", pinWindow, StringComparison.Ordinal);
        Assert.Contains("AddHandler(PointerReleasedEvent, PinButtonPointerReleased, RoutingStrategies.Bubble", pinWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Pointer.Capture(this)", pinWindow, StringComparison.Ordinal);
        Assert.Contains("ScheduleNativeStyleRefresh", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesGeneratedXamlInitializationForNamedControls()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "MainWindow.axaml.cs"));

        Assert.Contains("InitializeComponent();", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("AvaloniaXamlLoader.Load(this);", mainWindow, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Aion2Flow repository root was not found.");
    }
}
