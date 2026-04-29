using System.Text;
using Avalonia.Input;

namespace Cloris.Aion2Flow.Services.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public string Display => Format(Modifiers, VirtualKey);

    public static HotkeyDefinition? FromKeyEvent(KeyModifiers keyModifiers, Key key)
    {
        var vk = AvaloniaKeyToVirtualKey(key);
        if (vk is null)
        {
            return null;
        }

        var mods = HotkeyModifiers.None;
        if ((keyModifiers & KeyModifiers.Control) != 0) mods |= HotkeyModifiers.Control;
        if ((keyModifiers & KeyModifiers.Alt) != 0) mods |= HotkeyModifiers.Alt;
        if ((keyModifiers & KeyModifiers.Shift) != 0) mods |= HotkeyModifiers.Shift;
        if ((keyModifiers & KeyModifiers.Meta) != 0) mods |= HotkeyModifiers.Win;

        if (mods == HotkeyModifiers.None)
        {
            return null;
        }

        return new HotkeyDefinition(mods, vk.Value);
    }

    public static string Format(HotkeyModifiers modifiers, uint virtualKey)
    {
        var sb = new StringBuilder();
        if ((modifiers & HotkeyModifiers.Control) != 0) sb.Append("Ctrl+");
        if ((modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift+");
        if ((modifiers & HotkeyModifiers.Win) != 0) sb.Append("Win+");
        sb.Append(VirtualKeyName(virtualKey));
        return sb.ToString();
    }

    private static uint? AvaloniaKeyToVirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
        Key.Space => 0x20,
        Key.Tab => 0x09,
        Key.Enter => 0x0D,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        Key.Home => 0x24,
        Key.End => 0x23,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.Up => 0x26,
        Key.Down => 0x28,
        Key.Left => 0x25,
        Key.Right => 0x27,
        Key.OemPlus => 0xBB,
        Key.OemMinus => 0xBD,
        Key.OemComma => 0xBC,
        Key.OemPeriod => 0xBE,
        Key.OemQuestion => 0xBF,
        Key.OemTilde => 0xC0,
        Key.OemOpenBrackets => 0xDB,
        Key.OemCloseBrackets => 0xDD,
        Key.OemPipe => 0xDC,
        Key.OemSemicolon => 0xBA,
        Key.OemQuotes => 0xDE,
        _ => null
    };

    private static string VirtualKeyName(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (char)('0' + (vk - 0x60)),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        0xBB => "=",
        0xBD => "-",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBA => ";",
        0xDE => "'",
        _ => $"VK_{vk:X2}"
    };
}
