using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Cloris.Aion2Flow.Services.Hotkeys;

internal interface IGlobalHotkeyPlatform
{
    bool Register(nint hwnd, int id, HotkeyDefinition definition);

    bool Unregister(nint hwnd, int id);
}

internal sealed class Win32GlobalHotkeyPlatform : IGlobalHotkeyPlatform
{
    public static Win32GlobalHotkeyPlatform Instance { get; } = new();

    private Win32GlobalHotkeyPlatform()
    {
    }

    public bool Register(nint hwnd, int id, HotkeyDefinition definition) => PInvoke.RegisterHotKey(new HWND(hwnd), id, ToNativeModifiers(definition.Modifiers), definition.VirtualKey);

    public bool Unregister(nint hwnd, int id) => PInvoke.UnregisterHotKey(new HWND(hwnd), id);

    internal static HOT_KEY_MODIFIERS ToNativeModifiers(HotkeyModifiers modifiers)
        => (HOT_KEY_MODIFIERS)modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
}
