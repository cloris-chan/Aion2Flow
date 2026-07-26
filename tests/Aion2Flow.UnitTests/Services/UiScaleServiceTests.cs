using Avalonia.Controls;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Cloris.Aion2Flow.Tests.Services;

[Collection(AvaloniaTestCollection.Name)]
public sealed class UiScaleServiceTests
{
    [Fact]
    public void UiServicesAreDisposedOnUiThreadBeforeBackgroundProviderShutdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AvaloniaTestHost.Run(() =>
        {
            var settings = new SettingsService(Path.Combine(Path.GetTempPath(), $"aion2flow-ui-scale-{Guid.NewGuid():N}.json"));
            var originalContent = new Border();
            var window = new Window
            {
                Width = 300,
                Height = 200,
                Content = originalContent
            };
            using var provider = new ServiceCollection()
                .AddSingleton(settings)
                .AddSingleton<UiScaleService>()
                .BuildServiceProvider();

            window.Show();
            try
            {
                var uiScale = provider.GetRequiredService<UiScaleService>();
                uiScale.RegisterWindow(window);

                var scaleHost = Assert.IsType<LayoutTransformControl>(window.Content);
                Assert.Same(originalContent, scaleHost.Child);

                Program.DisposeUiThreadServices(provider);

                Assert.Same(originalContent, window.Content);
                var exception = Record.Exception(
                    () => Task.Run(provider.Dispose, cancellationToken).GetAwaiter().GetResult());
                Assert.Null(exception);
            }
            finally
            {
                if (window.IsVisible)
                    window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
