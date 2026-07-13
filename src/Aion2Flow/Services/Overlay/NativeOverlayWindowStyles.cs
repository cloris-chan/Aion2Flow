using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Cloris.Aion2Flow.Services.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Cloris.Aion2Flow.Services.Overlay;

internal static class NativeOverlayWindowStyles
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x00000008;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int InputTransparentStyles = WsExLayered | WsExTransparent;
    private static readonly HWND TopmostBand = new(-1);
    private static readonly HWND NonTopmostBand = new(-2);
    private static readonly ConditionalWeakTable<Window, InputTransparencyState> InputTransparencyStates = new();

    public static bool SetInputTransparent(Window window, bool enabled)
    {
        if (!TryGetExtendedStyles(window, out var handle, out var hwnd, out var current))
        {
            return false;
        }

        var state = InputTransparencyStates.GetValue(window, static _ => new InputTransparencyState());
        if (state.Handle != handle)
        {
            state.Reset(handle);
        }

        var preserveLayered = state.PreserveLayered;
        if (enabled && !state.IsEnabled)
        {
            preserveLayered = (current & WsExLayered) != 0;
        }

        var updated = enabled
            ? current | InputTransparentStyles
            : current & ~WsExTransparent;
        if (!enabled && state.IsEnabled && !preserveLayered)
        {
            updated &= ~WsExLayered;
        }

        if (!TryApplyWindowStyles(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, current, updated, InputTransparentStyles, out var applied))
        {
            return false;
        }

        var isTransparent = (applied & WsExTransparent) != 0;
        var isLayered = (applied & WsExLayered) != 0;
        if (enabled ? !isTransparent || !isLayered : isTransparent || (state.IsEnabled && isLayered != preserveLayered))
        {
            return false;
        }

        state.IsEnabled = enabled;
        state.PreserveLayered = enabled && preserveLayered;
        return true;
    }

    public static bool SetNoActivate(Window window, bool enabled) =>
        SetWindowStyle(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, WsExNoActivate, enabled);

    public static bool SetPopupStyle(Window window, bool enabled) =>
        SetWindowStyle(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, WsPopup, enabled);

    public static bool SetTopmostBand(Window window, bool enabled)
    {
        if (!TryGetExtendedStyles(window, out _, out var hwnd, out _))
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        if (!PInvoke.SetWindowPos(
            hwnd,
            enabled ? TopmostBand : NonTopmostBand, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
        {
            var error = Marshal.GetLastPInvokeError();
            AppLog.Write(AppLogLevel.Warning, $"Failed to update overlay topmost band: Win32 error {error}");
            return false;
        }

        var styles = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return ((styles & WsExTopmost) != 0) == enabled;
    }

    public static bool IsImmediatelyAbove(Window window, Window reference)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        var referenceHandle = reference.TryGetPlatformHandle()?.Handle ?? 0;
        return handle != 0 && referenceHandle != 0 && PInvoke.GetWindow(new HWND(referenceHandle), GET_WINDOW_CMD.GW_HWNDPREV) == new HWND(handle);
    }

    public static bool TryGetCursorPosition(out PixelPoint position)
    {
        if (!PInvoke.GetCursorPos(out var cursor))
        {
            position = default;
            return false;
        }

        position = new PixelPoint(cursor.X, cursor.Y);
        return true;
    }

    public static bool SetScreenBounds(Window window, PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Overlay window bounds must have a positive size.");
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        if (PInvoke.SetWindowPos(
            new HWND(handle),
            HWND.Null,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        AppLog.Write(AppLogLevel.Warning, $"Failed to update overlay window bounds: Win32 error {error}");
        return false;
    }

    public static bool TryGetInputTransparentStyles(Window window, out bool isLayered, out bool isTransparent)
    {
        if (!TryGetExtendedStyles(window, out _, out _, out var styles))
        {
            isLayered = false;
            isTransparent = false;
            return false;
        }

        isLayered = (styles & WsExLayered) != 0;
        isTransparent = (styles & WsExTransparent) != 0;
        return true;
    }

    public static bool TryGetTopmostBand(Window window, out bool isTopmost)
    {
        if (!TryGetExtendedStyles(window, out _, out _, out var styles))
        {
            isTopmost = false;
            return false;
        }

        isTopmost = (styles & WsExTopmost) != 0;
        return true;
    }

    private static bool SetWindowStyle(Window window, WINDOW_LONG_PTR_INDEX index, int flag, bool enabled)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            return false;
        }

        var hwnd = new HWND(handle);
        var current = PInvoke.GetWindowLong(hwnd, index);
        var updated = enabled
            ? current | flag
            : current & ~flag;
        return TryApplyWindowStyles(hwnd, index, current, updated, flag, out var applied)
            && ((applied & flag) != 0) == enabled;
    }

    private static bool TryGetExtendedStyles(Window window, out nint handle, out HWND hwnd, out int styles)
    {
        handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            hwnd = HWND.Null;
            styles = 0;
            return false;
        }

        hwnd = new HWND(handle);
        styles = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return true;
    }

    private static bool TryApplyWindowStyles(HWND hwnd, WINDOW_LONG_PTR_INDEX index, int current, int updated, int changedMask, out int applied)
    {
        applied = current;
        if (updated == current)
        {
            return true;
        }

        Marshal.SetLastPInvokeError(0);
        var previous = PInvoke.SetWindowLong(hwnd, index, updated);
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0)
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to update overlay window styles 0x{(uint)changedMask:X8}: Win32 error {error}");
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        if (!PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED |
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        {
            error = Marshal.GetLastPInvokeError();
            AppLog.Write(AppLogLevel.Warning, $"Failed to refresh overlay window styles 0x{(uint)changedMask:X8}: Win32 error {error}");
            RollbackWindowStyles(hwnd, index, current, changedMask);
            applied = PInvoke.GetWindowLong(hwnd, index);
            return false;
        }

        applied = PInvoke.GetWindowLong(hwnd, index);
        return true;
    }

    private static void RollbackWindowStyles(HWND hwnd, WINDOW_LONG_PTR_INDEX index, int previous, int changedMask)
    {
        Marshal.SetLastPInvokeError(0);
        var replaced = PInvoke.SetWindowLong(hwnd, index, previous);
        var error = Marshal.GetLastPInvokeError();
        if (replaced == 0 && error != 0)
        {
            AppLog.Write(AppLogLevel.Error, $"Failed to roll back overlay window styles 0x{(uint)changedMask:X8}: Win32 error {error}");
            return;
        }

        Marshal.SetLastPInvokeError(0);
        if (!PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED |
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        {
            error = Marshal.GetLastPInvokeError();
            AppLog.Write(AppLogLevel.Error, $"Failed to refresh rolled-back overlay window styles 0x{(uint)changedMask:X8}: Win32 error {error}");
        }
    }

    private sealed class InputTransparencyState
    {
        public nint Handle { get; private set; }

        public bool IsEnabled { get; set; }

        public bool PreserveLayered { get; set; }

        public void Reset(nint handle)
        {
            Handle = handle;
            IsEnabled = false;
            PreserveLayered = false;
        }
    }
}
