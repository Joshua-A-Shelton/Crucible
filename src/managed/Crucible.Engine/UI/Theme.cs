using System.Text.Json;
using Crucible.Binary;

namespace Crucible.UI;

/// <summary>An element's interaction states, which selectors match as <c>:hover</c> and so on.</summary>
[Flags]
public enum UIState
{
    None = 0,
    Hover = 1,
    Pressed = 2,
    Focus = 4,
    Disabled = 8,
}

/// <summary>
/// Which elements a style applies to (ADR 0061): an element type (which
/// its subclasses match too), classes, and states, as
/// <c>Button.primary.large:hover:focus</c>. <c>*</c> or an empty type
/// matches any element. No parts for parents or descendants.
/// </summary>
public sealed class Selector
{
    private Selector(string text, string? type, string[] classes, UIState states)
    {
        Text = text;
        Type = type;
        Classes = classes;
        States = states;
    }

    public string Text { get; }

    /// <summary>The element type's name, or null for any.</summary>
    public string? Type { get; }

    public IReadOnlyList<string> Classes { get; }

    public UIState States { get; }

    /// <summary>
    /// Classes and states count alike, ahead of the type: more specific
    /// selectors win, and between equals, the later style.
    /// </summary>
    public int Specificity => (Classes.Count + System.Numerics.BitOperations.PopCount((uint)States)) * 16 + (Type is null ? 0 : 1);

    public static Selector Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? type = null;
        List<string> classes = [];
        UIState states = UIState.None;
        int at = 0;
        int start = at;
        while (at < text.Length && text[at] != '.' && text[at] != ':')
        {
            ++at;
        }
        string head = text[start..at];
        if (head.Length > 0 && head != "*")
        {
            type = Identifier(head, text);
        }
        while (at < text.Length)
        {
            char kind = text[at++];
            start = at;
            while (at < text.Length && text[at] != '.' && text[at] != ':')
            {
                ++at;
            }
            string name = Identifier(text[start..at], text);
            if (kind == '.')
            {
                classes.Add(name);
            }
            else
            {
                states |= name switch
                {
                    "hover" => UIState.Hover,
                    "pressed" => UIState.Pressed,
                    "focus" => UIState.Focus,
                    "disabled" => UIState.Disabled,
                    _ => throw new InvalidDataException(
                        $"\"{text}\": :{name} isn't a state (:hover, :pressed, :focus, :disabled)"),
                };
            }
        }
        return new Selector(text, type, [.. classes], states);
    }

    public bool Matches(UIElement element)
    {
        if ((element.State & States) != States)
        {
            return false;
        }
        foreach (string name in Classes)
        {
            if (!element.Classes.Contains(name))
            {
                return false;
            }
        }
        if (Type is null)
        {
            return true;
        }
        for (Type? t = element.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            if (t.Name == Type)
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString() => Text;

    private static string Identifier(string name, string text)
    {
        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new InvalidDataException($"\"{text}\" isn't a selector: Type.class:state, any part optional");
        }
        return name;
    }
}

/// <summary>A property's transition: changes the style makes animate over <see cref="Duration"/> seconds.</summary>
public readonly record struct Transition(float Duration, Easing Easing);

/// <summary>Values for the elements a <see cref="Selector"/> matches, and how their changes animate.</summary>
public sealed class Style(Selector selector)
{
    private readonly Dictionary<StyledProperty, object> _values = [];
    private readonly Dictionary<StyledProperty, Transition> _transitions = [];

    public Style(string selector) : this(Selector.Parse(selector))
    {
    }

    public Selector Selector { get; } = selector;

    public IReadOnlyDictionary<StyledProperty, object> Values => _values;

    public IReadOnlyDictionary<StyledProperty, Transition> Transitions => _transitions;

    public Style Set<T>(StyledProperty<T> property, T value)
    {
        _values[property] = value!;
        return this;
    }

    public Style Transition(StyledProperty property, float duration, Easing easing)
    {
        _transitions[property] = new Transition(duration, easing);
        return this;
    }

    internal void SetValue(StyledProperty property, object value) => _values[property] = value;
}

/// <summary>
/// Styles for a UI (ADR 0061), set on a <see cref="UIRoot"/>. Built in C#,
/// or read from a theme file: a <c>.crstyle</c> (JSON) imported to a
/// <c>.crsty</c>. Changing a theme (or loading it again from a changed file)
/// restyles the roots using it.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Styles": [
///     {
///       "Selector": "Button.primary:hover",
///       "Set": { "Background": "#3a7bd5", "CornerRadii": 6, "Padding": [12, 6] },
///       "Transitions": { "Background": { "Duration": 0.15, "Easing": "EaseOut" } }
///     }
///   ]
/// }
/// </code>
/// Easings are Linear, Ease, EaseIn, EaseOut, EaseInOut, or [x1, y1, x2, y2].
/// </remarks>
public sealed class Theme
{
    public static readonly uint Kind = Tag.Of("STYL");
    public static readonly uint JsonSection = Tag.Of("JSON");
    public const uint KindVersion = 1;

    private readonly List<Style> _styles = [];

    public IReadOnlyList<Style> Styles => _styles;

    /// <summary>Counts changes, for the roots using it.</summary>
    public int Version { get; private set; }

    public Theme Add(Style style)
    {
        _styles.Add(style);
        ++Version;
        return this;
    }

    public void Clear()
    {
        _styles.Clear();
        ++Version;
    }

    /// <summary>Replaces this theme's styles with another's, so roots using it restyle (hot reload).</summary>
    public void ReplaceWith(Theme other)
    {
        _styles.Clear();
        _styles.AddRange(other._styles);
        ++Version;
    }

    /// <summary>A theme from <c>.crstyle</c> JSON; InvalidDataException says where it's wrong.</summary>
    public static Theme Parse(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray(),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"not JSON: {exception.Message}", exception);
        }
        using (document)
        {
            return Read(document.RootElement);
        }
    }

    public static Theme Parse(string json) => Parse(System.Text.Encoding.UTF8.GetBytes(json));

    /// <summary>A theme from an imported <c>.crsty</c> file's bytes.</summary>
    public static Theme Load(ReadOnlySpan<byte> importedFile)
    {
        using ImportedFile file = ImportedFile.Parse(importedFile, Kind, KindVersion);
        int section = file.Find(JsonSection);
        if (section < 0)
        {
            throw new InvalidDataException("the theme file has no JSON section");
        }
        return Parse(file.Read(section));
    }

    private static Theme Read(JsonElement root)
    {
        // UIElement's properties register with its static fields.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(UIElement).TypeHandle);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Styles", out JsonElement styles) ||
            styles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("a theme is an object with a Styles array");
        }
        Theme theme = new();
        int index = 0;
        foreach (JsonElement entry in styles.EnumerateArray())
        {
            string where = $"Styles[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("Selector", out JsonElement selector) ||
                selector.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{where}: a style is an object with a Selector string");
            }
            Style style = new(Context(where, () => Selector.Parse(selector.GetString()!)));
            where += $" ({selector.GetString()})";
            if (entry.TryGetProperty("Set", out JsonElement values))
            {
                foreach (JsonProperty value in Object(values, $"{where}.Set"))
                {
                    StyledProperty property = Property(value.Name, $"{where}.Set");
                    style.SetValue(property, Context($"{where}.Set.{value.Name}", () => property.ReadJson(value.Value)));
                }
            }
            if (entry.TryGetProperty("Transitions", out JsonElement transitions))
            {
                foreach (JsonProperty transition in Object(transitions, $"{where}.Transitions"))
                {
                    string at = $"{where}.Transitions.{transition.Name}";
                    StyledProperty property = Property(transition.Name, $"{where}.Transitions");
                    JsonElement t = transition.Value;
                    float duration = t.ValueKind == JsonValueKind.Object && t.TryGetProperty("Duration", out JsonElement d) &&
                                     d.ValueKind == JsonValueKind.Number && d.GetSingle() >= 0
                        ? d.GetSingle()
                        : throw new InvalidDataException($"{at}: a transition has a Duration in seconds");
                    Easing easing = t.TryGetProperty("Easing", out JsonElement e) ? Context($"{at}.Easing", () => ReadEasing(e))
                        : Easing.Ease;
                    style.Transition(property, duration, easing);
                }
            }
            theme.Add(style);
        }
        theme.Version = 1;
        return theme;
    }

    private static Easing ReadEasing(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && Easing.TryParse(value.GetString()!, out Easing named))
        {
            return named;
        }
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 4 &&
            value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number))
        {
            float[] p = [.. value.EnumerateArray().Select(v => v.GetSingle())];
            return new Easing(p[0], p[1], p[2], p[3]);
        }
        throw new InvalidDataException("expected Linear, Ease, EaseIn, EaseOut, EaseInOut, or [x1, y1, x2, y2]");
    }

    private static JsonElement.ObjectEnumerator Object(JsonElement value, string where) =>
        value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject()
            : throw new InvalidDataException($"{where}: expected an object of property names");

    private static StyledProperty Property(string name, string where) =>
        StyledProperty.Find(name) ?? throw new InvalidDataException($"{where}: {name} isn't a styled property");

    private static T Context<T>(string where, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{where}: {exception.Message}", exception);
        }
    }
}

/// <summary>An element's classes (ADR 0061); changing them restyles it.</summary>
public sealed class ClassList : IEnumerable<string>
{
    private readonly UIElement _owner;
    private readonly List<string> _names = [];

    internal ClassList(UIElement owner) => _owner = owner;

    public int Count => _names.Count;

    public bool Contains(string name) => _names.Contains(name);

    public void Add(string name)
    {
        if (!_names.Contains(name))
        {
            _names.Add(name);
            _owner.StyleChanged();
        }
    }

    public void Remove(string name)
    {
        if (_names.Remove(name))
        {
            _owner.StyleChanged();
        }
    }

    /// <summary>Adds or removes it.</summary>
    public void Set(string name, bool present)
    {
        if (present)
        {
            Add(name);
        }
        else
        {
            Remove(name);
        }
    }

    public IEnumerator<string> GetEnumerator() => _names.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
