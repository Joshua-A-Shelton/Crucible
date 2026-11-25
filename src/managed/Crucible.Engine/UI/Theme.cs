using System.Numerics;
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

/// <summary>How the next part of a selector sits in an element its part matched.</summary>
public enum Combinator
{
    /// <summary>Anywhere inside it: <c>Button:hover Icon</c>.</summary>
    Descendant,

    /// <summary>Directly inside it: <c>Button &gt; Icon</c>.</summary>
    Child,
}

/// <summary>
/// One element's part of a <see cref="Selector"/>: an element type (which
/// its subclasses match too), classes, and states, as
/// <c>Button.primary.large:hover:focus</c>. <c>*</c> or an empty type
/// matches any element.
/// </summary>
public sealed class SelectorPart
{
    internal SelectorPart(string? type, string[] classes, UIState states, Combinator combinator)
    {
        Type = type;
        Classes = classes;
        States = states;
        Combinator = combinator;
    }

    /// <summary>The element type's name, or null for any.</summary>
    public string? Type { get; }

    public IReadOnlyList<string> Classes { get; }

    public UIState States { get; }

    /// <summary>How the next part sits in this one's element (the last part's says nothing).</summary>
    public Combinator Combinator { get; }

    /// <summary>Classes and states count alike, ahead of the type.</summary>
    public int Specificity => (Classes.Count + System.Numerics.BitOperations.PopCount((uint)States)) * 16 + (Type is null ? 0 : 1);

    public bool Matches(UIElement element) => (element.State & States) == States && MatchesClassesAndType(element);

    // Whether its type matches, whatever the element's classes and states:
    // a change to those may change whether it matches.
    internal bool MatchesType(UIElement element)
    {
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

    private bool MatchesClassesAndType(UIElement element)
    {
        foreach (string name in Classes)
        {
            if (!element.Classes.Contains(name))
            {
                return false;
            }
        }
        return MatchesType(element);
    }
}

/// <summary>
/// Which elements a style applies to (ADR 0061, 0129): one or more
/// <see cref="SelectorPart"/>s, the last the element styled and the ones
/// before its ancestors, a space between parts for anywhere inside and
/// <c>&gt;</c> for directly inside: <c>Button:hover Icon</c>,
/// <c>DockTab.active &gt; Label</c>.
/// </summary>
public sealed class Selector
{
    private readonly SelectorPart[] _parts;

    private Selector(string text, SelectorPart[] parts)
    {
        Text = text;
        _parts = parts;
    }

    public string Text { get; }

    /// <summary>Its parts, outermost first; the last is the element styled.</summary>
    public IReadOnlyList<SelectorPart> Parts => _parts;

    private SelectorPart Subject => _parts[^1];

    /// <summary>The styled element's type name, or null for any.</summary>
    public string? Type => Subject.Type;

    /// <summary>The styled element's classes.</summary>
    public IReadOnlyList<string> Classes => Subject.Classes;

    /// <summary>The styled element's states.</summary>
    public UIState States => Subject.States;

    /// <summary>
    /// Every part's classes and states count alike, ahead of types: more
    /// specific selectors win, and between equals, the later style.
    /// </summary>
    public int Specificity => _parts.Sum(part => part.Specificity);

    public static Selector Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<SelectorPart> parts = [];
        int at = 0;
        SkipSpaces(text, ref at);
        while (true)
        {
            int start = at;
            while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] != '>')
            {
                ++at;
            }
            if (at == start)
            {
                throw Invalid(text);
            }
            string compound = text[start..at];
            SkipSpaces(text, ref at);
            Combinator combinator = Combinator.Descendant;
            if (at < text.Length && text[at] == '>')
            {
                combinator = Combinator.Child;
                ++at;
                SkipSpaces(text, ref at);
                if (at == text.Length)
                {
                    throw Invalid(text);
                }
            }
            parts.Add(Part(compound, text, combinator));
            if (at == text.Length)
            {
                break;
            }
        }
        return new Selector(text, [.. parts]);
    }

    public bool Matches(UIElement element) => Subject.Matches(element) && MatchesOuter(element, _parts.Length - 1);

    // Whether a change to this element's classes or states could change what
    // this selector matches below it: one of its outer parts names its type.
    internal bool MayMatchAsAncestor(UIElement element)
    {
        for (int i = 0; i < _parts.Length - 1; ++i)
        {
            if (_parts[i].MatchesType(element))
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString() => Text;

    // `element` matched part `index`: do its ancestors match the parts before?
    private bool MatchesOuter(UIElement element, int index)
    {
        if (index == 0)
        {
            return true;
        }
        SelectorPart outer = _parts[index - 1];
        if (outer.Combinator == Combinator.Child)
        {
            UIElement? parent = element.Parent;
            return parent is not null && outer.Matches(parent) && MatchesOuter(parent, index - 1);
        }
        for (UIElement? ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (outer.Matches(ancestor) && MatchesOuter(ancestor, index - 1))
            {
                return true;
            }
        }
        return false;
    }

    private static SelectorPart Part(string compound, string text, Combinator combinator)
    {
        string? type = null;
        List<string> classes = [];
        UIState states = UIState.None;
        int at = 0;
        while (at < compound.Length && compound[at] != '.' && compound[at] != ':')
        {
            ++at;
        }
        string head = compound[..at];
        if (head.Length > 0 && head != "*")
        {
            type = Identifier(head, text);
        }
        while (at < compound.Length)
        {
            char kind = compound[at++];
            int start = at;
            while (at < compound.Length && compound[at] != '.' && compound[at] != ':')
            {
                ++at;
            }
            string name = Identifier(compound[start..at], text);
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
        return new SelectorPart(type, [.. classes], states, combinator);
    }

    private static void SkipSpaces(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            ++at;
        }
    }

    private static string Identifier(string name, string text)
    {
        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
        {
            throw Invalid(text);
        }
        return name;
    }

    private static InvalidDataException Invalid(string text) => new(
        $"\"{text}\" isn't a selector: Type.class:state, any part optional, then more for elements inside it " +
        "(a space for anywhere inside, > for directly inside)");
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
///
/// Tokens (ADR 0129) name values that styles share: <c>"Tokens": {
/// "accent": "#ff7418", "radius": 5, "selection": "@accent/35%" }</c>, and a
/// style's value <c>"@accent"</c> is the token's, <c>"@accent/55%"</c> a
/// color token at that opacity. A token may name another; each is read as
/// the property it's used for needs.
/// </remarks>
public sealed partial class Theme
{
    public static readonly uint Kind = Tag.Of("STYL");
    public static readonly uint JsonSection = Tag.Of("JSON");
    public const uint KindVersion = 1;

    private readonly List<Style> _styles = [];
    private readonly Dictionary<string, object> _tokens = [];
    private readonly Dictionary<(TokenReference, StyledProperty), object> _resolved = [];
    private bool _hasAncestorParts;

    public IReadOnlyList<Style> Styles => _styles;

    /// <summary>
    /// The named values styles use as <c>"@name"</c>: a value from a theme
    /// file is its <see cref="JsonElement"/>, one set in C# the object given.
    /// </summary>
    public IReadOnlyDictionary<string, object> Tokens => _tokens;

    /// <summary>Counts changes, for the roots using it.</summary>
    public int Version { get; private set; }

    public Theme Add(Style style)
    {
        _styles.Add(style);
        _hasAncestorParts |= style.Selector.Parts.Count > 1;
        ++Version;
        return this;
    }

    public void Clear()
    {
        _styles.Clear();
        _tokens.Clear();
        _resolved.Clear();
        _hasAncestorParts = false;
        ++Version;
    }

    /// <summary>Replaces this theme's styles and tokens with another's, so roots using it restyle (hot reload).</summary>
    public void ReplaceWith(Theme other)
    {
        _styles.Clear();
        _styles.AddRange(other._styles);
        _tokens.Clear();
        foreach ((string name, object value) in other._tokens)
        {
            _tokens[name] = value;
        }
        _resolved.Clear();
        _hasAncestorParts = other._hasAncestorParts;
        ++Version;
    }

    /// <summary>
    /// Sets a token: a <see cref="JsonElement"/> read as each property needs,
    /// or a value of the type the properties using it hold (a color is a
    /// <see cref="Vector4"/>). Every style using it must be able to read it,
    /// or InvalidDataException says which can't, and nothing changes.
    /// </summary>
    public void SetToken(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        bool had = _tokens.TryGetValue(name, out object? before);
        _tokens[name] = value;
        _resolved.Clear();
        try
        {
            foreach (Style style in _styles)
            {
                foreach ((StyledProperty property, object styleValue) in style.Values)
                {
                    if (styleValue is TokenReference reference)
                    {
                        Resolve(reference, property, $"{style.Selector}: {property.Name}");
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            if (had)
            {
                _tokens[name] = before!;
            }
            else
            {
                _tokens.Remove(name);
            }
            _resolved.Clear();
            throw;
        }
        ++Version;
    }

    public bool TryGetToken(string name, out object value) => _tokens.TryGetValue(name, out value!);

    /// <summary>A color token as straight-alpha sRGB, for drawing; InvalidDataException if it isn't one.</summary>
    public Vector4 Color(string name) => (Vector4)Resolve(new TokenReference(name, null), UIElement.BackgroundProperty, name);

    // Whether a change to this element's classes or states may restyle
    // elements inside it, through a style with an ancestor part.
    internal bool MayStyleInside(UIElement element)
    {
        if (!_hasAncestorParts)
        {
            return false;
        }
        foreach (Style style in _styles)
        {
            if (style.Selector.Parts.Count > 1 && style.Selector.MayMatchAsAncestor(element))
            {
                return true;
            }
        }
        return false;
    }

    // A style's value as the element gets it: a token's, read for the property.
    internal object ValueFor(StyledProperty property, object value) =>
        value is TokenReference reference ? Resolve(reference, property, property.Name) : value;

    private object Resolve(TokenReference reference, StyledProperty property, string where)
    {
        if (_resolved.TryGetValue((reference, property), out object? cached))
        {
            return cached;
        }
        object value = Token(reference.Name, property, where, []);
        if (reference.Opacity is float opacity)
        {
            value = value is Vector4 color
                ? color with { W = color.W * opacity }
                : throw new InvalidDataException($"{where}: @{reference.Name}/…% is a color at an opacity, " +
                                                 $"but {property.Name} isn't a color");
        }
        _resolved[(reference, property)] = value;
        return value;
    }

    private object Token(string name, StyledProperty property, string where, HashSet<string> seen)
    {
        if (!seen.Add(name))
        {
            throw new InvalidDataException($"{where}: tokens refer to each other: @{string.Join(" → @", seen)} → @{name}");
        }
        if (!_tokens.TryGetValue(name, out object? token))
        {
            throw new InvalidDataException($"{where}: no token @{name}");
        }
        if (token is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String && TokenReference.TryParse(json.GetString()!, out TokenReference inner))
            {
                object value = Token(inner.Name, property, where, seen);
                if (inner.Opacity is float opacity)
                {
                    value = value is Vector4 color
                        ? color with { W = color.W * opacity }
                        : throw new InvalidDataException($"{where}: @{name} is @{inner.Name} at an opacity, " +
                                                         $"but {property.Name} isn't a color");
                }
                return value;
            }
            try
            {
                return property.ReadJson(json);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"{where}: @{name} as {property.Name}: {exception.Message}", exception);
            }
        }
        return property.ValueType.IsInstanceOfType(token)
            ? token
            : throw new InvalidDataException($"{where}: @{name} is a {token.GetType().Name}, but {property.Name} " +
                                             $"holds a {property.ValueType.Name}");
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
        if (root.TryGetProperty("Tokens", out JsonElement tokens))
        {
            foreach (JsonProperty token in Object(tokens, "Tokens"))
            {
                if (token.Name.Length == 0 || !token.Name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                {
                    throw new InvalidDataException($"Tokens: \"{token.Name}\" isn't a token name (letters, digits, - and _)");
                }
                theme._tokens[token.Name] = token.Value.Clone();
            }
        }
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
                    string at = $"{where}.Set.{value.Name}";
                    if (value.Value.ValueKind == JsonValueKind.String && value.Value.GetString()!.StartsWith('@'))
                    {
                        TokenReference reference = TokenReference.TryParse(value.Value.GetString()!, out TokenReference parsed)
                            ? parsed
                            : throw new InvalidDataException($"{at}: \"{value.Value.GetString()}\" isn't a token: @name or @name/50%");
                        theme.Resolve(reference, property, at); // says now what's wrong
                        style.SetValue(property, reference);
                    }
                    else
                    {
                        style.SetValue(property, Context(at, () => property.ReadJson(value.Value)));
                    }
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
        theme._resolved.Clear();
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

// A style's value naming a token: "@accent", or "@accent/55%" for a color
// at that opacity.
internal readonly record struct TokenReference(string Name, float? Opacity)
{
    public static bool TryParse(string text, out TokenReference reference)
    {
        reference = default;
        if (!text.StartsWith('@'))
        {
            return false;
        }
        int slash = text.IndexOf('/');
        string name = slash < 0 ? text[1..] : text[1..slash];
        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
        {
            return false;
        }
        float? opacity = null;
        if (slash >= 0)
        {
            string percent = text[(slash + 1)..];
            if (!percent.EndsWith('%') || !float.TryParse(percent[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value) || value < 0 || value > 100)
            {
                return false;
            }
            opacity = value / 100;
        }
        reference = new TokenReference(name, opacity);
        return true;
    }
}

/// <summary>An element's classes (ADR 0061); changing them restyles it.</summary>
public sealed class Classes : IEnumerable<string>
{
    private readonly UIElement _owner;
    private readonly List<string> _names = [];

    internal Classes(UIElement owner) => _owner = owner;

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
