using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Simple;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using Cloris.Aion2Flow.WinDivert;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using MainAppWindow = Cloris.Aion2Flow.Views.MainWindow;

namespace Cloris.Aion2Flow;

internal static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        VelopackApp.Build().Run();

        var serviceProvider = CreateServiceProvider();
        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        try
        {
            AppBuilder
                .Configure(serviceProvider.GetRequiredService<App>)
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            await ShutdownAsync(serviceProvider, mainViewModel).ConfigureAwait(false);
        }
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
        CaptureLog.Sink = static (level, message) => AppLog.Write(MapLogLevel(level), message);
        WinDivertLog.Sink = static (level, message) => AppLog.Write(MapLogLevel(level), $"[WinDivert] {message}");
        RawPacketDump.ConfigureLogDirectory(LogDirectoryResolver.GetDefaultLogDirectory());

        services.AddSingleton<SettingsService>();
        services.AddSingleton<App>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<GameResourceService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<AppUpdateService>();
        services.AddSingleton<EncounterArchiveService>();
        services.AddSingleton<AvaloniaFrameClockService>();
        services.AddSingleton<UiFrameBatchService>();
        services.AddSingleton<IScenePlaybackTickSourceFactory, AvaloniaScenePlaybackTickSourceFactory>();
        services.AddSingleton<CombatantDetailsFlyoutViewModel>();
        services.AddSingleton<ProcessPortDiscoveryService>();
        services.AddSingleton<ProcessForegroundWatcher>();
        services.AddSingleton<WinDivertCaptureService>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<SettingsFlyoutViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainAppWindow>();

        var serviceProvider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(serviceProvider);
        return serviceProvider;
    }

    private static Task ShutdownAsync(ServiceProvider serviceProvider, MainViewModel mainViewModel)
    {
        return Task.Run(async () =>
        {
            try
            {
                await mainViewModel.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    WinDivertRuntime.Shutdown();
                }
                finally
                {
                    await serviceProvider.DisposeAsync().ConfigureAwait(false);
                }
            }
        });
    }

    private static AppLogLevel MapLogLevel(CaptureLogLevel level) => level switch
    {
        CaptureLogLevel.Debug => AppLogLevel.Debug,
        CaptureLogLevel.Info => AppLogLevel.Info,
        CaptureLogLevel.Warning => AppLogLevel.Warning,
        CaptureLogLevel.Error => AppLogLevel.Error,
        _ => AppLogLevel.Info
    };

    private static AppLogLevel MapLogLevel(WinDivertLogLevel level) => level switch
    {
        WinDivertLogLevel.Debug => AppLogLevel.Debug,
        WinDivertLogLevel.Info => AppLogLevel.Info,
        WinDivertLogLevel.Warning => AppLogLevel.Warning,
        WinDivertLogLevel.Error => AppLogLevel.Error,
        _ => AppLogLevel.Info
    };
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
