using System.Reflection;
using Avalonia;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class AvaloniaTestHost
{
    private static readonly Lock s_gate = new();
    private static bool s_initialized;

    public static void EnsureInitialized()
    {
        if (Application.Current is not null || s_initialized)
            return;

        lock (s_gate)
        {
            if (Application.Current is not null || s_initialized)
                return;

            typeof(Dispatcher)
                .GetMethod("ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);

            AppBuilder
                .Configure<TestApplication>()
                .UsePlatformDetect()
                .SetupWithoutStarting();

            s_initialized = true;
        }
    }

    private sealed class TestApplication : Application;
}
