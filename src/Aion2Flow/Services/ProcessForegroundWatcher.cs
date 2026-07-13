using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Services.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Cloris.Aion2Flow.Services;

public sealed class ProcessForegroundWatcher : IDisposable
{
    private static ProcessForegroundWatcher? _instance;

    private readonly UnhookWinEventSafeHandle _safeHandle;
    private bool _isDisposed;

    public event Action? ForegroundChanged;

    public unsafe ProcessForegroundWatcher()
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        Marshal.SetLastPInvokeError(0);
        _safeHandle = PInvoke.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, &WinEventCallback, 0, 0, 0);
        if (_safeHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to register the foreground-window event hook.");
        }

        Volatile.Write(ref _instance, this);
    }

    public bool IsTargetProcessForeground() => IsTargetProcessWindow(PInvoke.GetForegroundWindow());

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void WinEventCallback(HWINEVENTHOOK hWinEventHook, uint eventType, HWND hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            Volatile.Read(ref _instance)?.ForegroundChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Foreground watcher callback failed: {ex}");
        }
    }

    private static bool IsTargetProcessWindow(HWND hwnd)
    {
        if (hwnd == HWND.Null || PInvoke.GetWindowThreadProcessId(hwnd, out var pid) == 0)
            return false;

        if (pid > int.MaxValue)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Aion2ProcessIdentity.MatchesExecutableName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (ReferenceEquals(_instance, this))
        {
            Volatile.Write(ref _instance, null);
        }

        _safeHandle.Dispose();
    }
}
