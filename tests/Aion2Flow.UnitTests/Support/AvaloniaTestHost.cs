using System.Reflection;
using Avalonia;
using Avalonia.Themes.Simple;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class AvaloniaTestHost
{
    private static readonly Lock s_gate = new();
    private static bool s_initialized;

    public static void EnsureInitialized()
    {
        lock (s_gate)
        {
            if (Application.Current is null && !s_initialized)
            {
                typeof(Dispatcher)
                    .GetMethod("ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, null);

                AppBuilder
                    .Configure<TestApplication>()
                    .UsePlatformDetect()
                    .SetupWithoutStarting();

                s_initialized = true;
            }

            if (Application.Current is { } application && !application.Styles.OfType<SimpleTheme>().Any())
                application.Styles.Add(new SimpleTheme());
        }
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new SimpleTheme());
        }
    }
}
