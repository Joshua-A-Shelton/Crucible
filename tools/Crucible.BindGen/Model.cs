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

/// <summary>A type in an exposed signature or struct: a primitive or an exposed struct.</summary>
internal sealed record TypeRef(Primitive Primitive, ExposedStruct? Struct)
{
    public bool IsStruct => Struct is not null;
}

internal sealed record Field(string Name, TypeRef Type, long Offset);

internal sealed record ExposedStruct(string QualifiedName, string Name, long Size)
{
    public List<Field> Fields { get; } = [];
}

internal sealed record Parameter(string Name, TypeRef Type);

internal sealed record ExposedFunction(string QualifiedName, string Name, TypeRef ReturnType, List<Parameter> Parameters)
{
    /// <summary>The exported C symbol.</summary>
    public string CName => $"crucible_{Name}";
}

internal sealed class BindingModel
{
    public List<ExposedStruct> Structs { get; } = [];
    public List<ExposedFunction> Functions { get; } = [];

    /// <summary>The input headers, as include paths ("crucible/Core.h").</summary>
    public List<string> Headers { get; } = [];

    /// <summary>Every file parsing read, for the depfile.</summary>
    public List<string> Dependencies { get; } = [];
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
