using System.Numerics;
using System.Text.Json;

namespace Crucible.UI;

/// <summary>What a property's change makes the element do again.</summary>
[Flags]
public enum PropertyAffects
{
    None = 0,
    Measure = 1, // its size may change
    Visual = 2,  // it looks different
}

/// <summary>
/// A property themes can style and animations can animate (ADR 0061),
/// registered once per owner type. An element's value comes from layers,
/// highest first: an animation, a value set in code, the best-matching
/// style, then the default.
/// </summary>
public abstract class StyledProperty
{
    private static readonly List<StyledProperty> _all = [];
    private static readonly Dictionary<string, StyledProperty> _byName = new(StringComparer.Ordinal);

    private protected StyledProperty(string name, Type owner, PropertyAffects affects)
    {
        Name = name;
        Owner = owner;
        Affects = affects;
        lock (_all)
        {
            if (!_byName.TryAdd(name, this))
            {
                throw new InvalidOperationException($"A styled property named {name} is already registered");
            }
            Index = _all.Count;
            _all.Add(this);
        }
    }

    /// <summary>The name themes give it ("Background").</summary>
    public string Name { get; }

    /// <summary>The type that registered it; elements of other types ignore it.</summary>
    public Type Owner { get; }

    public PropertyAffects Affects { get; }

    internal int Index { get; }

    internal static int Count => _all.Count;

    /// <summary>A registered property by name, or null.</summary>
    public static StyledProperty? Find(string name)
    {
        lock (_all)
        {
            return _byName.GetValueOrDefault(name);
        }
    }

    // A theme's value for it, from JSON; throws InvalidDataException naming
    // what it expected.
    internal abstract object ReadJson(JsonElement value);

    internal abstract ValueSlot CreateSlot();
}

/// <summary>A styled property of type <typeparamref name="T"/>.</summary>
public sealed class StyledProperty<T> : StyledProperty
{
    private readonly Func<JsonElement, T> _readJson;

    private StyledProperty(string name, Type owner, T defaultValue, PropertyAffects affects,
        Func<T, T, float, T>? interpolate, Func<JsonElement, T> readJson)
        : base(name, owner, affects)
    {
        Default = defaultValue;
        Interpolate = interpolate;
        _readJson = readJson;
    }

    public T Default { get; }

    /// <summary>Blends two values (0 to 1) for transitions and tweens; null jumps at the end.</summary>
    public Func<T, T, float, T>? Interpolate { get; }

    /// <summary>
    /// Registers a property. <paramref name="readJson"/> reads a theme's
    /// value (the standard types have readers in <see cref="StyleValues"/>).
    /// </summary>
    public static StyledProperty<T> Register<TOwner>(string name, T defaultValue, PropertyAffects affects,
        Func<JsonElement, T> readJson, Func<T, T, float, T>? interpolate = null) where TOwner : UIElement =>
        new(name, typeof(TOwner), defaultValue, affects, interpolate, readJson);

    internal override object ReadJson(JsonElement value) => _readJson(value)!;

    internal override ValueSlot CreateSlot() => new ValueSlot<T>(this);
}

// One element's layers for one property.
internal abstract class ValueSlot
{
    public bool HasLocal;
    public bool HasStyle;
    public Animation? Animation;

    public abstract StyledProperty Property { get; }

    // The style layer's new value (null: no style sets it).
    public abstract void Restyle(UIElement element, object? value, Dictionary<StyledProperty, Transition> transitions,
        double now);

    public abstract void SetLocal(object value);
}

internal sealed class ValueSlot<T>(StyledProperty<T> property) : ValueSlot
{
    public T Local = default!;
    public T Style = default!;
    public T Animated = default!;

    public override StyledProperty Property => property;

    public override void Restyle(UIElement element, object? value, Dictionary<StyledProperty, Transition> transitions,
        double now)
    {
        T before = Animation is not null ? Animated : HasLocal ? Local : HasStyle ? Style : property.Default;
        if (value is null)
        {
            HasStyle = false;
            Style = default!;
        }
        else
        {
            Style = (T)value;
            HasStyle = true;
        }
        element.StyleValueChanged(property, this, before, transitions, now);
    }

    public override void SetLocal(object value)
    {
        Local = (T)value;
        HasLocal = true;
    }
}

/// <summary>JSON readers and blends for the standard styled-property types (ADR 0061).</summary>
public static class StyleValues
{
    public static float ReadFloat(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetSingle() : throw Expected("a number");

    public static Vector2 ReadVector2(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? new Vector2(value.GetSingle())
        : Floats(value, 2, "a number or [x, y]") is var f ? new Vector2(f[0], f[1]) : default;

    /// <summary>Corner radii: one number for all, or [top left, top right, bottom right, bottom left].</summary>
    public static Vector4 ReadVector4(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? new Vector4(value.GetSingle())
        : Floats(value, 4, "a number or four numbers") is var f ? new Vector4(f[0], f[1], f[2], f[3]) : default;

    /// <summary>One number for all sides, [horizontal, vertical], or [left, top, right, bottom].</summary>
    public static Thickness ReadThickness(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return new Thickness(value.GetSingle());
        }
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2)
        {
            float[] two = Floats(value, 2, "");
            return new Thickness(two[0], two[1], two[0], two[1]);
        }
        float[] f = Floats(value, 4, "a number, [horizontal, vertical], or [left, top, right, bottom]");
        return new Thickness(f[0], f[1], f[2], f[3]);
    }

    /// <summary>"#rgb", "#rgba", "#rrggbb", "#rrggbbaa", or [r, g, b(, a)] from 0 to 1: sRGB, straight alpha.</summary>
    public static Vector4 ReadColor(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString()!;
            if (text.StartsWith('#') && text.Length is 4 or 5 or 7 or 9 && uint.TryParse(text.AsSpan(1),
                    System.Globalization.NumberStyles.HexNumber, null, out uint hex))
            {
                if (text.Length <= 5)
                {
                    uint shortRgba = text.Length == 4 ? (hex << 4) | 15 : hex;
                    return new Vector4((shortRgba >> 12) / 15f, ((shortRgba >> 8) & 15) / 15f, ((shortRgba >> 4) & 15) / 15f,
                        (shortRgba & 15) / 15f);
                }
                uint rgba = text.Length == 7 ? (hex << 8) | 0xff : hex;
                return new Vector4((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f,
                    (rgba & 255) / 255f);
            }
            throw Expected("a color: \"#rgb\", \"#rgba\", \"#rrggbb\", \"#rrggbbaa\", or [r, g, b, a]");
        }
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3)
        {
            float[] rgb = Floats(value, 3, "");
            return new Vector4(rgb[0], rgb[1], rgb[2], 1);
        }
        float[] f = Floats(value, 4, "a color: \"#rgb\", \"#rgba\", \"#rrggbb\", \"#rrggbbaa\", or [r, g, b, a]");
        return new Vector4(f[0], f[1], f[2], f[3]);
    }

    public static Assets.AssetId ReadAssetId(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Assets.AssetId.TryParse(value.GetString(), out Assets.AssetId id)
            ? id
            : throw Expected("an asset ID");

    public static TEnum ReadEnum<TEnum>(JsonElement value) where TEnum : struct, System.Enum =>
        value.ValueKind == JsonValueKind.String && System.Enum.TryParse(value.GetString(), out TEnum parsed) &&
        System.Enum.IsDefined(parsed)
            ? parsed
            : throw Expected($"one of {string.Join(", ", System.Enum.GetNames<TEnum>())}");

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => System.Numerics.Vector2.Lerp(a, b, t);

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => System.Numerics.Vector4.Lerp(a, b, t);

    public static Thickness Lerp(Thickness a, Thickness b, float t) =>
        new(Lerp(a.Left, b.Left, t), Lerp(a.Top, b.Top, t), Lerp(a.Right, b.Right, t), Lerp(a.Bottom, b.Bottom, t));

    /// <summary>Colors (sRGB, straight alpha) blended in linear light, premultiplied, as the canvas blends.</summary>
    public static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        Vector4 la = Premultiplied(a), lb = Premultiplied(b);
        Vector4 mixed = System.Numerics.Vector4.Lerp(la, lb, t);
        if (mixed.W <= 0)
        {
            return System.Numerics.Vector4.Zero;
        }
        return new Vector4(Encode(mixed.X / mixed.W), Encode(mixed.Y / mixed.W), Encode(mixed.Z / mixed.W), mixed.W);

        static Vector4 Premultiplied(Vector4 c) => new(Decode(c.X) * c.W, Decode(c.Y) * c.W, Decode(c.Z) * c.W, c.W);
        static float Decode(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float Encode(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
    }

    private static float[] Floats(JsonElement value, int count, string expected)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
        {
            throw Expected(expected);
        }
        float[] result = new float[count];
        int i = 0;
        foreach (JsonElement element in value.EnumerateArray())
        {
            result[i++] = element.ValueKind == JsonValueKind.Number ? element.GetSingle() : throw Expected(expected);
        }
        return result;
    }

    private static InvalidDataException Expected(string what) => new($"expected {what}");
}
