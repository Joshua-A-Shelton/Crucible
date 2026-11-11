namespace Crucible.Serialization;

/// <summary>What a serialized member holds (ADR 0069): how its JSON looks and which editor shows it.</summary>
public enum FieldKind
{
    Boolean,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    String,
    Guid,
    AssetId,
    Entity,
    Vector2,
    Vector3,
    Vector4,
    Quaternion,
    Matrix4x4,
    Enum,
    Nullable,
    Array,
    List,
    Dictionary,
    Serialized,
}

/// <summary>
/// One member of a <see cref="SerializedAttribute"/> type as its JSON
/// shows it (ADR 0069), from the type's generated <c>SerializedFields</c>:
/// its name (the JSON key), its kind, and the editor hints on it. An editor
/// reads a value's JSON, changes one member, and writes it back, so nothing
/// here touches the value itself.
/// </summary>
public sealed class SerializedField
{
    private readonly Func<IReadOnlyList<SerializedField>>? _fields;

    /// <summary>For generated code: <paramref name="fields"/> gives a nested type's table, read when first asked for.</summary>
    public SerializedField(string name, FieldKind kind, SerializedField? element = null, string[]? enumNames = null,
        Func<IReadOnlyList<SerializedField>>? fields = null, float min = float.NaN, float max = float.NaN,
        string? tooltip = null, string? header = null, bool isColor = false, bool isLayers = false,
        string? label = null)
    {
        Name = name;
        Kind = kind;
        Element = element;
        EnumNames = enumNames ?? [];
        _fields = fields;
        Min = min;
        Max = max;
        Tooltip = tooltip;
        Header = header;
        IsColor = isColor;
        IsLayers = isLayers;
        Label = label;
    }

    /// <summary>The member's name and JSON key; empty for a list's or dictionary's items.</summary>
    public string Name { get; }

    public FieldKind Kind { get; }

    /// <summary>What a nullable, array, list, or dictionary holds.</summary>
    public SerializedField? Element { get; }

    /// <summary>An enum's names, in declaration order (enums are saved by name).</summary>
    public IReadOnlyList<string> EnumNames { get; }

    /// <summary>A nested <see cref="FieldKind.Serialized"/> type's members; empty for other kinds.</summary>
    public IReadOnlyList<SerializedField> Fields => _fields?.Invoke() ?? [];

    /// <summary>From <see cref="RangeAttribute"/>; NaN without one.</summary>
    public float Min { get; }

    public float Max { get; }

    public bool HasRange => !float.IsNaN(Min) && !float.IsNaN(Max);

    /// <summary>From <see cref="TooltipAttribute"/>.</summary>
    public string? Tooltip { get; }

    /// <summary>From <see cref="HeaderAttribute"/>: a heading shown above it.</summary>
    public string? Header { get; }

    /// <summary>From <see cref="ColorAttribute"/>: a Vector3 (RGB) or Vector4 (RGBA) shown as a color.</summary>
    public bool IsColor { get; }

    /// <summary>From <see cref="LayersAttribute"/>: a uint shown as a layer mask.</summary>
    public bool IsLayers { get; }

    /// <summary>From <see cref="LabelAttribute"/>: the name the inspector shows, or null for its own.</summary>
    public string? Label { get; }
}
