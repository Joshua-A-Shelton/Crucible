using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;

namespace Crucible.Serialization;

/// <summary>
/// The reads and writes generated serializers call (ADR 0038): one value
/// each, with errors that say what was expected and where. Public because
/// the generated code lives in game assemblies; not meant to be called by
/// hand.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class JsonValues
{
    /// <summary>The property holding a versioned type's data version; written first.</summary>
    public const string VersionProperty = "$version";

    public static JsonException Error(in Utf8JsonReader reader, string message) =>
        new($"{message} (at byte {reader.TokenStartIndex})");

    public static void Expect(in Utf8JsonReader reader, JsonTokenType type, string what)
    {
        if (reader.TokenType != type)
        {
            throw Error(reader, $"expected {what}, found {Describe(reader.TokenType)}");
        }
    }

    public static bool ReadBoolean(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        _ => throw Error(reader, $"expected true or false, found {Describe(reader.TokenType)}"),
    };

    public static byte ReadByte(ref Utf8JsonReader reader) =>
        Number(reader).TryGetByte(out byte value) ? value : throw OutOfRange(reader, "byte");

    public static sbyte ReadSByte(ref Utf8JsonReader reader) =>
        Number(reader).TryGetSByte(out sbyte value) ? value : throw OutOfRange(reader, "sbyte");

    public static short ReadInt16(ref Utf8JsonReader reader) =>
        Number(reader).TryGetInt16(out short value) ? value : throw OutOfRange(reader, "short");

    public static ushort ReadUInt16(ref Utf8JsonReader reader) =>
        Number(reader).TryGetUInt16(out ushort value) ? value : throw OutOfRange(reader, "ushort");

    public static int ReadInt32(ref Utf8JsonReader reader) =>
        Number(reader).TryGetInt32(out int value) ? value : throw OutOfRange(reader, "int");

    public static uint ReadUInt32(ref Utf8JsonReader reader) =>
        Number(reader).TryGetUInt32(out uint value) ? value : throw OutOfRange(reader, "uint");

    public static long ReadInt64(ref Utf8JsonReader reader) =>
        Number(reader).TryGetInt64(out long value) ? value : throw OutOfRange(reader, "long");

    public static ulong ReadUInt64(ref Utf8JsonReader reader) =>
        Number(reader).TryGetUInt64(out ulong value) ? value : throw OutOfRange(reader, "ulong");

    /// <summary>A number, or "NaN", "Infinity", or "-Infinity", which JSON numbers can't hold.</summary>
    public static float ReadSingle(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? (float)NamedNumber(reader)
        : Number(reader).TryGetSingle(out float value) ? value
        : throw OutOfRange(reader, "float");

    public static double ReadDouble(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? NamedNumber(reader)
        : Number(reader).TryGetDouble(out double value) ? value
        : throw OutOfRange(reader, "double");

    public static string? ReadString(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Null => null,
        _ => throw Error(reader, $"expected a string, found {Describe(reader.TokenType)}"),
    };

    public static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        Expect(reader, JsonTokenType.String, "a GUID string");
        return reader.TryGetGuid(out Guid value) ? value : throw Error(reader, "expected a GUID string");
    }

    /// <summary>An asset reference: its GUID string, or null for none.</summary>
    public static AssetId ReadAssetId(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Null ? AssetId.None : new AssetId(ReadGuid(ref reader));

    public static void WriteAssetId(Utf8JsonWriter writer, AssetId value)
    {
        if (value.IsNone)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }

    /// <summary>An enum by name (or names, comma-separated, for flags).</summary>
    public static T ReadEnum<T>(ref Utf8JsonReader reader) where T : struct, Enum
    {
        Expect(reader, JsonTokenType.String, $"a {typeof(T).Name} name");
        string name = reader.GetString()!;
        return Enum.TryParse(name, ignoreCase: false, out T value)
            ? value
            : throw Error(reader, $"{name} isn't a {typeof(T).Name}");
    }

    public static Vector2 ReadVector2(ref Utf8JsonReader reader)
    {
        Span<float> v = stackalloc float[2];
        ReadFloats(ref reader, v, "Vector2");
        return new Vector2(v[0], v[1]);
    }

    public static Vector3 ReadVector3(ref Utf8JsonReader reader)
    {
        Span<float> v = stackalloc float[3];
        ReadFloats(ref reader, v, "Vector3");
        return new Vector3(v[0], v[1], v[2]);
    }

    public static Vector4 ReadVector4(ref Utf8JsonReader reader)
    {
        Span<float> v = stackalloc float[4];
        ReadFloats(ref reader, v, "Vector4");
        return new Vector4(v[0], v[1], v[2], v[3]);
    }

    public static Quaternion ReadQuaternion(ref Utf8JsonReader reader)
    {
        Span<float> v = stackalloc float[4];
        ReadFloats(ref reader, v, "Quaternion");
        return new Quaternion(v[0], v[1], v[2], v[3]);
    }

    /// <summary>Sixteen numbers, row by row (M11, M12, ... M44).</summary>
    public static Matrix4x4 ReadMatrix4x4(ref Utf8JsonReader reader)
    {
        Span<float> v = stackalloc float[16];
        ReadFloats(ref reader, v, "Matrix4x4");
        return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13],
            v[14], v[15]);
    }

    // Floats are written in the shortest form that reads back exactly.
    public static void WriteSingle(Utf8JsonWriter writer, float value)
    {
        if (float.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(NameOf(value));
        }
    }

    public static void WriteDouble(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(NameOf(value));
        }
    }

    public static void WriteEnum<T>(Utf8JsonWriter writer, T value) where T : struct, Enum =>
        writer.WriteStringValue(value.ToString());

    // Vectors and matrices stay on one line: a position is one diff line.
    public static void WriteVector2(Utf8JsonWriter writer, Vector2 value) => WriteFloats(writer, [value.X, value.Y]);

    public static void WriteVector3(Utf8JsonWriter writer, Vector3 value) =>
        WriteFloats(writer, [value.X, value.Y, value.Z]);

    public static void WriteVector4(Utf8JsonWriter writer, Vector4 value) =>
        WriteFloats(writer, [value.X, value.Y, value.Z, value.W]);

    public static void WriteQuaternion(Utf8JsonWriter writer, Quaternion value) =>
        WriteFloats(writer, [value.X, value.Y, value.Z, value.W]);

    public static void WriteMatrix4x4(Utf8JsonWriter writer, Matrix4x4 m) =>
        WriteFloats(writer,
            [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44]);

    /// <summary>A dictionary's keys in ordinal order, so files don't depend on insertion order.</summary>
    public static string[] SortedKeys<T>(Dictionary<string, T> dictionary)
    {
        string[] keys = [.. dictionary.Keys];
        Array.Sort(keys, StringComparer.Ordinal);
        return keys;
    }

    /// <summary>
    /// A versioned object's data version: its first property if that is
    /// <see cref="VersionProperty"/>, else 1 (data saved before the type was
    /// versioned). The reader is left where it was.
    /// </summary>
    public static int PeekVersion(in Utf8JsonReader reader)
    {
        Utf8JsonReader probe = reader;
        if (probe.TokenType != JsonTokenType.StartObject || !probe.Read() ||
            probe.TokenType != JsonTokenType.PropertyName || !probe.ValueTextEquals(VersionProperty) || !probe.Read())
        {
            return 1;
        }
        return probe.TokenType == JsonTokenType.Number && probe.TryGetInt32(out int version) && version >= 1
            ? version
            : throw Error(probe, $"expected a version number of at least 1 for {VersionProperty}");
    }

    /// <summary>
    /// Reads an older version's object as a tree, lets the type's migration
    /// upgrade it, and returns it as JSON for the generated reader. Only old
    /// data pays for this.
    /// </summary>
    public static byte[] Migrate(ref Utf8JsonReader reader, int fromVersion, int toVersion,
        Action<JsonObject, int> migrate, string typeName)
    {
        if (fromVersion > toVersion)
        {
            throw Error(reader, $"{typeName}: data version {fromVersion} is newer than this build's {toVersion}");
        }
        JsonObject data = JsonNode.Parse(ref reader)?.AsObject() ?? throw Error(reader, $"{typeName}: expected an object");
        data.Remove(VersionProperty);
        migrate(data, fromVersion);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            data.WriteTo(writer);
        }
        return stream.ToArray();
    }

    private static Utf8JsonReader Number(in Utf8JsonReader reader)
    {
        Expect(reader, JsonTokenType.Number, "a number");
        return reader;
    }

    private static JsonException OutOfRange(in Utf8JsonReader reader, string type) =>
        Error(reader, $"{Encoding.UTF8.GetString(reader.ValueSpan)} doesn't fit in a {type}");

    private static double NamedNumber(in Utf8JsonReader reader) => reader.GetString() switch
    {
        "NaN" => double.NaN,
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        string other => throw Error(reader, $"expected a number, found the string \"{other}\""),
        null => throw Error(reader, "expected a number"),
    };

    private static string NameOf(double value) =>
        double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";

    private static void ReadFloats(ref Utf8JsonReader reader, scoped Span<float> values, string type)
    {
        Expect(reader, JsonTokenType.StartArray, $"a {type} ([{values.Length} numbers])");
        for (int i = 0; i < values.Length; ++i)
        {
            if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
            {
                throw Error(reader, $"a {type} needs {values.Length} numbers");
            }
            values[i] = ReadSingle(ref reader);
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw Error(reader, $"a {type} has only {values.Length} numbers");
        }
    }

    private static void WriteFloats(Utf8JsonWriter writer, ReadOnlySpan<float> values)
    {
        StringBuilder text = new("[");
        for (int i = 0; i < values.Length; ++i)
        {
            text.Append(i == 0 ? "" : ", ");
            float value = values[i];
            text.Append(float.IsFinite(value)
                ? value.ToString("R", CultureInfo.InvariantCulture)
                : $"\"{NameOf(value)}\"");
        }
        writer.WriteRawValue(text.Append(']').ToString(), skipInputValidation: true);
    }

    private static string Describe(JsonTokenType type) => type switch
    {
        JsonTokenType.StartObject => "an object",
        JsonTokenType.StartArray => "an array",
        JsonTokenType.String => "a string",
        JsonTokenType.Number => "a number",
        JsonTokenType.True or JsonTokenType.False => "true or false",
        JsonTokenType.Null => "null",
        JsonTokenType.None => "the end of the data",
        _ => type.ToString(),
    };
}
