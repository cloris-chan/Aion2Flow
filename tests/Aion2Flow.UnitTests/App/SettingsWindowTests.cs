using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using Cloris.Aion2Flow.Views;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Tests.App;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SettingsWindowTests
{
    [Fact]
    public void OwnedSettingsWindowOpensAtDisplayAndAppliesChangesBeforeClosing()
    {
        AvaloniaTestHost.Run(() =>
        {
            var (localization, _) = ViewTestServices.Get();
            var language = Ioc.Default.GetRequiredService<LanguageService>();
            var initialLanguage = language.CurrentLanguage;
            var filePath = Path.Combine(Path.GetTempPath(), "Aion2Flow.Tests", $"{Guid.NewGuid():N}.json");
            var settings = new SettingsService(filePath);
            settings.Update(value =>
            {
                value.Language = LanguageService.TraditionalChinese;
                value.MaxVisibleCombatantRows = 9;
                value.TopmostMode = TopmostMode.Always;
            });
            using var clientSettings = new ClientSettingsService(settings, language);
            using var resources = new GameResourceService(clientSettings);
            using var playerNames = new PlayerNameDisplayService(settings, localization);
            using var uiScale = new UiScaleService(settings);
            using var foreground = new ProcessForegroundWatcher();
            var model = new SettingsViewModel(localization, language, settings, playerNames, uiScale,
                new AppUpdateService(), foreground, new GlobalHotkeyService(), clientSettings);

            var owner = new Window();
            var controller = new OwnedWindowController();
            try
            {
                owner.Show();
                var window = new SettingsWindow(model, resources, settings);
                controller.Show(window, owner);
                Dispatcher.UIThread.RunJobs();

                Assert.Same(owner, window.Owner);
                Assert.False(window.IsDialog);
                Assert.True(controller.TryActivate());

                var tabs = window.FindControl<TabControl>("CategoryTabs")!;
                Assert.Equal(
                    ["浮窗與顯示", "戰鬥統計", "技能監控", "快捷鍵", "客戶端與語言", "版本與更新"],
                    tabs.Items.OfType<TabItem>().Select(tab => tab.Header));
                Assert.Equal(0, tabs.SelectedIndex);

                var topmost = window.FindControl<ComboBox>("TopmostChoice")!;
                Assert.Equal(TopmostMode.Always, topmost.SelectedValue);
                Assert.Equal(9, window.FindControl<ComboBox>("VisibleRowsChoice")!.SelectedItem);
                topmost.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(topmost.GetTemplateDescendants().OfType<Popup>().Single().IsOpen);
                var topmostItems = topmost.Items.OfType<ComboBoxItem>();
                Assert.Equal(["僅遊戲前台時", "始終", "從不"], topmostItems.Select(item => item.Content));
                Assert.All(topmostItems, item => Assert.Same(topmost.ItemContainerTheme, item.Theme));
                topmost.IsDropDownOpen = false;
                topmost.SelectedValue = TopmostMode.Never;
                window.FindControl<CheckBox>("HideHeaderToggle")!.IsChecked = true;
                Assert.Equal(TopmostMode.Never, model.TopmostMode);
                Assert.True(model.HideHeaderWhenClickThrough);

                tabs.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();
                var monitor = window.FindControl<SkillMonitorSettingsView>("MonitorConfiguration")!;
                Assert.NotEmpty(Assert.IsType<SkillMonitorSettingsViewModel>(monitor.DataContext).Groups);

                tabs.SelectedIndex = 3;
                var editor = window.FindControl<Border>("BattleResetHotkeyEditor")!;
                editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(GlobalHotkeyAction.BattleReset, model.CapturingHotkeyAction);

                tabs.SelectedIndex = 4;
                Assert.Null(model.CapturingHotkeyAction);
                var client = window.FindControl<ComboBox>("ClientChoice")!;
                client.SelectedValue = ClientVersion.Steam;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ClientVersion.Steam, clientSettings.CurrentProfile.Version);

                var resourceLanguage = window.FindControl<ComboBox>("ResourceLanguageChoice")!;
                Assert.Equal(clientSettings.ResourceLanguages, resourceLanguage.Items.OfType<LanguageOption>().Select(option => option.Code));
                resourceLanguage.SelectedItem = model.ResourceLanguages.Single(option => option.Code == LanguageService.English);
                window.FindControl<ComboBox>("LanguageChoice")!.SelectedItem = model.Languages.Single(option => option.Code == "ja-JP");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("設定", window.Title);
                Assert.Equal("オーバーレイと表示", Assert.IsType<TabItem>(tabs.Items[0]).Header);

                var saved = new SettingsService(filePath).Current;
                Assert.True(saved.HideHeaderWhenClickThrough);
                Assert.Equal(TopmostMode.Never, saved.TopmostMode);
                Assert.Equal(ClientVersion.Steam, saved.ClientVersion);
                Assert.Equal(LanguageService.English, saved.ResourceLanguage);
                Assert.Equal("ja-JP", saved.Language);

                tabs.SelectedIndex = 3;
                editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(GlobalHotkeyAction.BattleReset, model.CapturingHotkeyAction);
                window.Close();
                Assert.Null(model.CapturingHotkeyAction);
                Assert.False(controller.TryActivate());
                var reopened = new SettingsWindow(model, resources, settings);
                controller.Show(reopened, owner);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, reopened.FindControl<TabControl>("CategoryTabs")!.SelectedIndex);
                owner.Close();
                Assert.False(reopened.IsVisible);
            }
            finally
            {
                owner.Close();
                language.SetLanguage(initialLanguage);
                Dispatcher.UIThread.RunJobs();
                File.Delete(filePath);
            }
        });
    }
}
