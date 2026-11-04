namespace Crucible.UI;

/// <summary>
/// The system clipboard's text, for the text fields' copy and paste (ADR
/// 0065). Where the platform can't hold it (no platform started, as in
/// tests), it's kept here instead, so copy and paste still pair up.
/// </summary>
public static class Clipboard
{
    private static string? _fallback;

    public static string Text
    {
        get => _fallback ?? Platform.Api.ClipboardText();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fallback = Platform.Api.SetClipboardText(value) ? null : value;
        }
    }
}
