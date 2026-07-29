using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class OverlayLayoutTests
{
    [Fact]
    public void HeaderVisibilityPreference_PreservesLayoutInClickThroughMode()
    {
        AvaloniaTestHost.Run(() =>
        {
            var style = new StyleInclude(new Uri("avares://Aion2Flow/"))
            {
                Source = new Uri("avares://Aion2Flow/Styles/OverlayTheme.axaml")
            };
            Application.Current!.Styles.Add(style);
            var header = new Border();
            header.Classes.Add("MainHudHeader");
            var pinSlot = new Border
            {
                Width = 26,
                Height = 26
            };
            var footer = new Border
            {
                Height = 28,
                Child = pinSlot
            };
            Grid.SetRow(footer, 1);
            var shell = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Children = { header, footer }
            };
            shell.Classes.Add("MainHudShell");
            shell.Classes.Add("hide-header-when-click-through");
            var window = new Window
            {
                Width = 300,
                SizeToContent = SizeToContent.Height,
                Content = shell
            };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(header.IsVisible);
                Assert.Equal(1d, header.Opacity);
                var visibleBounds = header.Bounds;
                var visibleWindowSize = window.ClientSize;
                var visibleShellBounds = shell.Bounds;
                var visibleFooterBounds = footer.Bounds;
                var visiblePinBounds = pinSlot.Bounds;
                var visiblePinScreenOrigin = pinSlot.PointToScreen(default);

                shell.Classes.Add("click-through");
                Dispatcher.UIThread.RunJobs();
                Assert.True(header.IsVisible);
                Assert.Equal(0d, header.Opacity);
                Assert.Equal(visibleBounds, header.Bounds);
                Assert.Equal(visibleWindowSize, window.ClientSize);
                Assert.Equal(visibleShellBounds, shell.Bounds);
                Assert.Equal(visibleFooterBounds, footer.Bounds);
                Assert.Equal(visiblePinBounds, pinSlot.Bounds);
                Assert.Equal(visiblePinScreenOrigin, pinSlot.PointToScreen(default));

                shell.Classes.Remove("click-through");
                Dispatcher.UIThread.RunJobs();
                Assert.True(header.IsVisible);
                Assert.Equal(1d, header.Opacity);
                Assert.Equal(visibleBounds, header.Bounds);
                Assert.Equal(visibleWindowSize, window.ClientSize);
                Assert.Equal(visibleShellBounds, shell.Bounds);
                Assert.Equal(visibleFooterBounds, footer.Bounds);
                Assert.Equal(visiblePinBounds, pinSlot.Bounds);
                Assert.Equal(visiblePinScreenOrigin, pinSlot.PointToScreen(default));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Application.Current.Styles.Remove(style);
            }
        });
    }

    [Fact]
    public void DurationBlock_UsesTheBottomBarMetricStyle()
    {
        AvaloniaTestHost.Run(() =>
        {
            var style = new StyleInclude(new Uri("avares://Aion2Flow/"))
            {
                Source = new Uri("avares://Aion2Flow/Styles/OverlayTheme.axaml")
            };
            Application.Current!.Styles.Add(style);
            var timer = new DurationBlock();
            timer.Classes.Add("BottomBarMetric");
            var window = new Window { Content = timer };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(
                    FontWeight.SemiBold,
                    timer.DecimalSecondsBlockForDiagnostics.FontWeight);
                Assert.NotNull(
                    timer.DecimalSecondsBlockForDiagnostics.Foreground);
                Assert.Equal(
                    FontWeight.SemiBold,
                    timer.MinutesBlockForDiagnostics.FontWeight);
                Assert.Equal(
                    FontWeight.SemiBold,
                    timer.SecondsBlockForDiagnostics.FontWeight);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Application.Current.Styles.Remove(style);
            }
        });
    }
}
