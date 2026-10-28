using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Crucible.Serialization;

/// <summary>
/// Reads and writes Crucible's authored JSON (ADR 0021, 0038): two-space
/// indents, one field per line, '\n' line endings on every platform, and a
/// final newline, so files diff the same everywhere. Names are placeholders
/// (docs/naming.md).
/// </summary>
public static class Json
{
    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>Writes the value as a UTF-8 JSON document.</summary>
    public static byte[] Serialize<T>(in T value) where T : IJsonSerialized<T>
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, _writerOptions))
        {
            T.WriteJson(writer, value);
        }
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    public static string SerializeToString<T>(in T value) where T : IJsonSerialized<T> =>
        Encoding.UTF8.GetString(Serialize(value));

    /// <summary>
    /// Reads a UTF-8 JSON document. Throws <see cref="JsonException"/>,
    /// naming the type and member, if it doesn't fit the type.
    /// </summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> utf8) where T : IJsonSerialized<T>
    {
        Utf8JsonReader reader = new(utf8);
        if (!reader.Read())
        {
            throw new JsonException($"{typeof(T).Name}: the document is empty.");
        }
        T value = T.ReadJson(ref reader);
        if (reader.Read())
        {
            throw new JsonException($"{typeof(T).Name}: unexpected data after the value, at byte {reader.TokenStartIndex}.");
        }
        return value;
    }

    public static T Deserialize<T>(string json) where T : IJsonSerialized<T> =>
        Deserialize<T>(Encoding.UTF8.GetBytes(json));
}
