namespace Crucible.Platform;

/// <summary>
/// SDL's keycodes (SDL_keycode.h) for the keys shortcuts use (ADR 0066): a
/// key as the user's layout labels it, so Ctrl+Z is the key marked Z on
/// any layout. Letters and digits are their lowercase characters; other
/// keys are their scancode with bit 30 set.
/// </summary>
public static class Keycode
{
    private const uint ScancodeMask = 1u << 30;

    public const uint Return = '\r', Escape = 0x1b, Backspace = '\b', Tab = '\t', Space = ' ', Delete = 0x7f;
    public const uint A = 'a', B = 'b', C = 'c', D = 'd', E = 'e', F = 'f', G = 'g', H = 'h', I = 'i', J = 'j';
    public const uint K = 'k', L = 'l', M = 'm', N = 'n', O = 'o', P = 'p', Q = 'q', R = 'r', S = 's', T = 't';
    public const uint U = 'u', V = 'v', W = 'w', X = 'x', Y = 'y', Z = 'z';
    public const uint D0 = '0', D1 = '1', D2 = '2', D3 = '3', D4 = '4', D5 = '5', D6 = '6', D7 = '7', D8 = '8', D9 = '9';
    public const uint F1 = 58 | ScancodeMask, F2 = 59 | ScancodeMask, F3 = 60 | ScancodeMask, F4 = 61 | ScancodeMask;
    public const uint F5 = 62 | ScancodeMask, F6 = 63 | ScancodeMask, F7 = 64 | ScancodeMask, F8 = 65 | ScancodeMask;
    public const uint F9 = 66 | ScancodeMask, F10 = 67 | ScancodeMask, F11 = 68 | ScancodeMask, F12 = 69 | ScancodeMask;

    /// <summary>The key's name for menus and shortcut text: "Z", "F5", "Delete".</summary>
    public static string Name(uint keycode) => keycode switch
    {
        Return => "Enter",
        Escape => "Esc",
        Backspace => "Backspace",
        Tab => "Tab",
        Space => "Space",
        Delete => "Delete",
        >= 'a' and <= 'z' => ((char)(keycode - 32)).ToString(),
        >= '!' and <= '`' => ((char)keycode).ToString(),
        >= F1 and <= F12 => "F" + (keycode - F1 + 1),
        _ => $"Key {keycode:X}",
    };
}
