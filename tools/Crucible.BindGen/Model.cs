namespace Crucible.BindGen;

/// <summary>The C-shaped value types that may cross the boundary (ADR 0017).</summary>
internal enum Primitive
{
    Void,
    Bool,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
}

/// <summary>The shapes a type can take at the boundary.</summary>
internal enum Shape
{
    Primitive,
    Struct,
    Enum,

    /// <summary>std::span&lt;T&gt;, as a parameter: pointer + length.</summary>
    Span,

    /// <summary>std::string_view, as a parameter: UTF-8 pointer + length.</summary>
    String,

    /// <summary>A fixed-size array, as a struct field.</summary>
    Array,

    /// <summary>A pointer or reference to an opaque native type: a typed handle in C#.</summary>
    Opaque,

    /// <summary>void* or const void*: a raw address (nint) for engine code.</summary>
    Address,
}

/// <summary>A type in an exposed signature or struct.</summary>
/// <param name="Element">A span's or array's element type.</param>
/// <param name="Length">An array's length.</param>
/// <param name="IsConst">Whether a span's elements are const (ReadOnlySpan in C#).</param>
internal sealed record TypeRef(
    Shape Shape,
    Primitive Primitive = Primitive.Void,
    ExposedStruct? Struct = null,
    ExposedEnum? Enum = null,
    TypeRef? Element = null,
    bool IsConst = false,
    long Length = 0,
    ExposedOpaque? Opaque = null,
    bool IsReference = false)
{
    public static TypeRef Of(Primitive primitive) => new(Shape.Primitive, primitive);

    public bool Is(Primitive primitive) => Shape == Shape.Primitive && Primitive == primitive;
}

/// <summary>
/// The namespaces around a declaration, outermost first, starting with
/// "crucible". Each maps to a C# namespace (ADR 0017): crucible::platform is
/// Crucible.Platform.
/// </summary>
internal sealed record Scope(IReadOnlyList<string> Namespaces)
{
    /// <summary>The C++ prefix, "crucible::platform::".</summary>
    public string Cpp => string.Concat(Namespaces.Select(name => $"{name}::"));

    public string CSharp => string.Join('.', Namespaces.Select(Names.Pascal));

    /// <summary>The namespaces below crucible, joined for C and raw C# names.</summary>
    public IEnumerable<string> Inner => Namespaces.Skip(1);

    public bool Equals(Scope? other) => other is not null && Namespaces.SequenceEqual(other.Namespaces);

    public override int GetHashCode() => CSharp.GetHashCode(StringComparison.Ordinal);
}

internal sealed record Field(string Name, TypeRef Type, long Offset);

/// <summary>
/// A struct declared but not defined in the headers, marked CRUCIBLE_EXPOSE:
/// native code owns it, and C# holds a handle to it (World*, Query*).
/// </summary>
internal sealed record ExposedOpaque(string QualifiedName, string Name, Scope Scope)
{
    /// <summary>The C# handle type's name: WorldHandle.</summary>
    public string HandleName => $"{Name}Handle";
}

/// <param name="MappedCSharpType">
/// Set by CRUCIBLE_EXPOSE_AS: the existing C# type it crosses as
/// ("System.Numerics.Vector3"), so no mirror is generated.
/// </param>
internal sealed record ExposedStruct(string QualifiedName, string Name, Scope Scope, long Size,
    string? MappedCSharpType = null)
{
    public List<Field> Fields { get; } = [];
}

internal sealed record Enumerator(string Name, string Value);

internal sealed record ExposedEnum(string QualifiedName, string Name, Scope Scope, Primitive Underlying, long Size)
{
    public List<Enumerator> Enumerators { get; } = [];
}

internal sealed record Parameter(string Name, TypeRef Type);

/// <param name="ReturnType">What the function returns, or the T of a returned Result&lt;T&gt;.</param>
/// <param name="ReturnsResult">Whether the function returns crucible::Result (ADR 0030).</param>
/// <param name="Internal">
/// Marked CRUCIBLE_EXPOSE_INTERNAL: the C# method is internal, for the engine
/// and its tests, not for games.
/// </param>
/// <param name="StatusForm">
/// Marked CRUCIBLE_EXPOSE_STATUS: C# returns the Status (expected or per-frame
/// failures) instead of throwing.
/// </param>
internal sealed record ExposedFunction(
    string Name,
    Scope Scope,
    TypeRef ReturnType,
    List<Parameter> Parameters,
    bool ReturnsResult,
    bool StatusForm,
    bool Internal = false)
{
    public string QualifiedName => $"{Scope.Cpp}{Name}";

    /// <summary>The exported C symbol: crucible_platform_createWindow.</summary>
    public string CName => string.Join('_', ["crucible", .. Scope.Inner, Name]);

    /// <summary>The raw C# layer's method name: PlatformCreateWindow.</summary>
    public string RawName => string.Concat(Scope.Inner.Append(Name).Select(Names.Pascal));
}

internal sealed class BindingModel
{
    public List<ExposedEnum> Enums { get; } = [];
    public List<ExposedOpaque> Opaques { get; } = [];
    public List<ExposedStruct> Structs { get; } = [];
    public List<ExposedFunction> Functions { get; } = [];

    /// <summary>crucible::Status, which Result-returning functions report in C#.</summary>
    public ExposedEnum? Status { get; set; }

    /// <summary>The input headers, as include paths ("crucible/Core.h").</summary>
    public List<string> Headers { get; } = [];

    /// <summary>Every file parsing read, for the depfile.</summary>
    public List<string> Dependencies { get; } = [];
}

internal static class Names
{
    public static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}

/// <summary>C ABI and C# spellings of each primitive.</summary>
internal static class Primitives
{
    /// <summary>The C type in the shim's signature. bool crosses as a byte.</summary>
    public static string CAbi(Primitive primitive) => primitive switch
    {
        Primitive.Void => "void",
        Primitive.Bool => "uint8_t",
        Primitive.Int8 => "int8_t",
        Primitive.UInt8 => "uint8_t",
        Primitive.Int16 => "int16_t",
        Primitive.UInt16 => "uint16_t",
        Primitive.Int32 => "int32_t",
        Primitive.UInt32 => "uint32_t",
        Primitive.Int64 => "int64_t",
        Primitive.UInt64 => "uint64_t",
        Primitive.Float => "float",
        Primitive.Double => "double",
        _ => throw new ArgumentOutOfRangeException(nameof(primitive)),
    };

    /// <summary>The C# type in the raw layer: exactly the C ABI type.</summary>
    public static string CSharpRaw(Primitive primitive) => primitive switch
    {
        Primitive.Void => "void",
        Primitive.Bool => "byte",
        Primitive.Int8 => "sbyte",
        Primitive.UInt8 => "byte",
        Primitive.Int16 => "short",
        Primitive.UInt16 => "ushort",
        Primitive.Int32 => "int",
        Primitive.UInt32 => "uint",
        Primitive.Int64 => "long",
        Primitive.UInt64 => "ulong",
        Primitive.Float => "float",
        Primitive.Double => "double",
        _ => throw new ArgumentOutOfRangeException(nameof(primitive)),
    };

    /// <summary>The C# type in the idiomatic layer.</summary>
    public static string CSharpIdiomatic(Primitive primitive) =>
        primitive == Primitive.Bool ? "bool" : CSharpRaw(primitive);
}
