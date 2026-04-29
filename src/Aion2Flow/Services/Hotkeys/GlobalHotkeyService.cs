using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Cloris.Aion2Flow.Services.Hotkeys;

public sealed class GlobalHotkeyService
{
    public const uint WmHotkey = 0x0312;
    private const int ResetHotkeyId = 0xA101;

    private readonly Lock _gate = new();
    private nint _hwnd;
    private bool _registered;
    private HotkeyDefinition? _pending;

    public event Action? Triggered;

    public void AttachWindow(nint hwnd)
    {
        lock (_gate)
        {
            _hwnd = hwnd;
            if (_pending is not null)
            {
                ApplyLocked(_pending);
            }
        }
    }

    public void SetHotkey(HotkeyDefinition? definition)
    {
        lock (_gate)
        {
            _pending = definition;
            ApplyLocked(definition);
        }
    }

    public void HandleWindowMessage(uint msg, nint wParam)
    {
        if (msg != WmHotkey || (int)wParam != ResetHotkeyId)
        {
            return;
        }

        var handler = Triggered;
        if (handler is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => handler.Invoke());
    }

    private void ApplyLocked(HotkeyDefinition? definition)
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (_registered)
        {
            PInvoke.UnregisterHotKey(new HWND(_hwnd), ResetHotkeyId);
            _registered = false;
        }

        if (definition is null)
        {
            return;
        }

        var ok = PInvoke.RegisterHotKey(
            new HWND(_hwnd),
            ResetHotkeyId,
            (HOT_KEY_MODIFIERS)definition.Modifiers,
            definition.VirtualKey);

        if (!ok)
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to register global hotkey {definition.Display}");
        }

        _registered = ok;
    }
}
