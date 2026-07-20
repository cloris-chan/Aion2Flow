using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SettingsFlyoutLayoutTests
{
    [Fact]
    public void SettingsSubMenuPopup_TouchesItsAnchorWithoutAHorizontalGap()
    {
        AvaloniaTestHost.Run(() =>
        {
            var style = new StyleInclude(new Uri("avares://Aion2Flow/"))
            {
                Source = new Uri("avares://Aion2Flow/Styles/OverlayTheme.axaml")
            };
            Application.Current!.Styles.Add(style);
            var parent = new MenuItem { Header = "Parent" };
            parent.Classes.Add("FlyoutMenuItem");
            parent.Classes.Add("FlyoutPanelRow");
            parent.Classes.Add("SettingsRowItem");
            parent.Items.Add(new MenuItem { Header = "Child" });
            var menu = new Menu { Items = { parent } };
            menu.Classes.Add("SettingsMenu");
            var window = new Window { Content = menu };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                parent.ApplyTemplate();

                var popup = Assert.Single(parent.GetTemplateDescendants().OfType<Popup>());
                Assert.Equal(PlacementMode.RightEdgeAlignedTop, popup.Placement);
                Assert.Equal(0, popup.HorizontalOffset);
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
