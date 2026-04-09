using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Simple;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Battle.Archive;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using Cloris.Aion2Flow.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

using MainAppWindow = Cloris.Aion2Flow.Views.MainWindow;

namespace Cloris.Aion2Flow;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var serviceProvider = CreateServiceProvider();
        AppBuilder
            .Configure(() => serviceProvider.GetRequiredService<App>())
           .UsePlatformDetect()
           .StartWithClassicDesktopLifetime(args);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<App>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<GameResourceService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<BattleArchiveService>();
        services.AddSingleton<CombatMetricsStore>();
        services.AddSingleton<CombatMetricsEngine>();
        services.AddSingleton<CombatantSkillDetailsFlyoutViewModel>();
        services.AddSingleton<ProcessPortDiscoveryService>();
        services.AddSingleton<ProcessForegroundWatcher>();
        services.AddSingleton<WinDivertCaptureService>();
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
        Styles.Add(new SimpleTheme());
        Resources.MergedDictionaries.Add(new IconGeometries());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = serviceProvider.GetRequiredService<MainAppWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
