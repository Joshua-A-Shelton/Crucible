using System.Text.Json;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The editor's icons (ADR 0124): Lucide's (ISC, lucide.dev), by their
/// names there ("stamp", "eye-off"), drawn from its icon font in the
/// element's <c>Foreground</c>, so they follow the theme as text does. For
/// the editor's own buttons and extensions' alike.
/// </summary>
public static class EditorIcons
{
    private static Dictionary<string, int>? _codes;

    /// <summary>Lucide's font, loaded by the editor when it starts; none before (icons then draw nothing).</summary>
    internal static FontHandle Font { get; set; }

    /// <summary>Whether there's an icon by that name.</summary>
    public static bool Has(string name) => Codes.ContainsKey(name ?? "");

    // Weak keys: a game's component types unload with its build.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, string> _components = [];

    /// <summary>A component type's icon, from its [ComponentIcon] (ADR 0131); "" for none.</summary>
    public static string OfComponent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_components.TryGetValue(type, out string? name))
        {
            name = type.GetCustomAttributes(typeof(Crucible.Scenes.ComponentIconAttribute), false)
                .OfType<Crucible.Scenes.ComponentIconAttribute>().FirstOrDefault()?.Name ?? "";
            _components.AddOrUpdate(type, name);
        }
        return name;
    }

    /// <summary>An icon by its Lucide name, at the editor's icon size; an unknown name draws nothing.</summary>
    public static Icon Icon(string name)
    {
        Icon icon = new(Font, Glyph(name))
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Classes.Add("editor-icon");
        return icon;
    }

    /// <summary>A button showing an icon, its tooltip saying what it does (and its key: "Stamp (B)").</summary>
    public static Button Button(string name, string toolTip)
    {
        Button button = new() { Content = Icon(name), ToolTip = toolTip ?? "", VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("icon");
        return button;
    }

    /// <summary>An on/off button showing an icon, lit while on (Flip X, Show Collision); no box.</summary>
    public static Toggle Toggle(string name, string toolTip)
    {
        Toggle toggle = new() { Content = Icon(name), ToolTip = toolTip ?? "", VerticalAlignment = VerticalAlignment.Center };
        toggle.Box.Visibility = Visibility.Collapsed;
        toggle.Classes.Add("icon");
        return toggle;
    }

    /// <summary>The icon's character in Lucide's font; empty for an unknown name.</summary>
    public static string Glyph(string name) =>
        Codes.TryGetValue(name ?? "", out int code) ? char.ConvertFromUtf32(code) : "";

    // Lucide's names to its font's characters (lucide-codepoints.json, beside the font).
    private static Dictionary<string, int> Codes => _codes ??= ReadCodes();

    private static Dictionary<string, int> ReadCodes()
    {
        string file = Path.Combine(Shell.FontsFolder(), "lucide-codepoints.json");
        Dictionary<string, int> codes = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (JsonProperty icon in document.RootElement.EnumerateObject())
            {
                if (icon.Value.TryGetInt32(out int code))
                {
                    codes[icon.Name] = code;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Crucible.Diagnostics.Log.Warning($"Crucible.Editor: the icons' names can't be read: {exception.Message}");
        }
        return codes;
    }
}
