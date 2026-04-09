using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Cloris.Aion2Flow.Services;

public sealed class ProcessForegroundWatcher : IDisposable
{
    private static ProcessForegroundWatcher? _instance;

    private readonly ProcessPortDiscoveryService _processPortDiscoveryService;
    private readonly UnhookWinEventSafeHandle _safeHandle;
    private bool _isDisposed;

    public event Action<bool>? ForegroundChanged;

    public unsafe ProcessForegroundWatcher(ProcessPortDiscoveryService processPortDiscoveryService)
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        _processPortDiscoveryService = processPortDiscoveryService;
        _safeHandle = PInvoke.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, &WinEventCallback, 0, 0, 0);
        _instance = this;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void WinEventCallback(HWINEVENTHOOK hWinEventHook, uint eventType, HWND hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == HWND.Null)
            return;

        if (PInvoke.GetWindowThreadProcessId(hwnd, out var pid) == 0)
            return;

        if (_instance is null)
            return;

        if (_instance._processPortDiscoveryService.ProcessIds.Contains(pid))
            _instance.ForegroundChanged?.Invoke(true);
        else
            _instance?.ForegroundChanged?.Invoke(false);
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
            _instance = null;
        }

        _safeHandle.Dispose();
    }
}
