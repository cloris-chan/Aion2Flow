using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Themes.Simple;
using Avalonia.Threading;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class AvaloniaTestHost
{
    private static readonly Lock s_gate = new();
    private static readonly BlockingCollection<WorkItem> s_queue = [];
    private static Thread? s_thread;
    private static ExceptionDispatchInfo? s_initializationError;
    private static int s_threadId;

    public static void EnsureInitialized() => EnsureStarted();

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureStarted();

        if (Environment.CurrentManagedThreadId == s_threadId)
        {
            action();
            return;
        }

        var workItem = new WorkItem(action);
        s_queue.Add(workItem);
        workItem.Wait();
    }

    private static void EnsureStarted()
    {
        lock (s_gate)
        {
            if (s_thread is not null)
            {
                s_initializationError?.Throw();
                return;
            }

            using var started = new ManualResetEventSlim();
            s_thread = new Thread(() => RunLoop(started))
            {
                IsBackground = true,
                Name = "Aion2Flow Avalonia tests"
            };
            s_thread.Start();
            started.Wait();
            s_initializationError?.Throw();
        }
    }

    private static void RunLoop(ManualResetEventSlim started)
    {
        try
        {
            s_threadId = Environment.CurrentManagedThreadId;
            ResetDispatcher();
            AppBuilder
                .Configure<TestApplication>()
                .UsePlatformDetect()
                .SetupWithoutStarting();

            if (Application.Current is { } application && !application.Styles.OfType<SimpleTheme>().Any())
                application.Styles.Add(new SimpleTheme());
        }
        catch (Exception ex)
        {
            s_initializationError = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            started.Set();
        }

        if (s_initializationError is not null)
            return;

        foreach (var workItem in s_queue.GetConsumingEnumerable())
            workItem.Execute();
    }

    private static void ResetDispatcher()
        => typeof(Dispatcher)
            .GetMethod("ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, null);

    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new SimpleTheme());
        }
    }

    private sealed class WorkItem(Action action)
    {
        private readonly ManualResetEventSlim _completed = new();
        private ExceptionDispatchInfo? _error;

        public void Execute()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                _completed.Set();
            }
        }

        public void Wait()
        {
            _completed.Wait();
            _completed.Dispose();
            _error?.Throw();
        }
    }
}
