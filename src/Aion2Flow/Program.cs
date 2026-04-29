using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Simple;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

using MainAppWindow = Cloris.Aion2Flow.Views.MainWindow;

namespace Cloris.Aion2Flow;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var serviceProvider = CreateServiceProvider();
        AppBuilder
            .Configure(() => serviceProvider.GetRequiredService<App>())
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);

        serviceProvider.GetRequiredService<AppLogWriter>().Dispose();
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        var logWriter = new AppLogWriter(
#if DEBUG
            AppLogLevel.Debug
#else
            AppLogLevel.Info
#endif
        );
        services.AddSingleton(logWriter);
        AppLog.Initialize(logWriter);

        services.AddSingleton<SettingsService>();
        services.AddSingleton<App>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<GameResourceService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<AppUpdateService>();
        services.AddSingleton<BattleArchiveService>();
        services.AddSingleton<CombatMetricsStore>();
        services.AddSingleton<CombatMetricsEngine>();
        services.AddSingleton<CombatantDetailsFlyoutViewModel>();
        services.AddSingleton<ProcessPortDiscoveryService>();
        services.AddSingleton<ProcessForegroundWatcher>();
        services.AddSingleton<WinDivertCaptureService>();
        services.AddSingleton<SettingsFlyoutViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainAppWindow>();

        var serviceProvider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(serviceProvider);
        return serviceProvider;
    }
}

file sealed class App(IServiceProvider serviceProvider) : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new SimpleTheme());
        Resources.MergedDictionaries.Add(new IconGeometries());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += OnDesktopExit;
            desktop.MainWindow = serviceProvider.GetRequiredService<MainAppWindow>();
        }

        Task.Run(() => serviceProvider.GetRequiredService<AppUpdateService>().Start());
        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        serviceProvider.GetRequiredService<AppUpdateService>()?.PreparePendingUpdateForShutdown();
    }
}
