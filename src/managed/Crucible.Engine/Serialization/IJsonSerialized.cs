using System.Text.Json;

namespace Crucible.Serialization;

/// <summary>
/// A type with a generated JSON reader and writer (ADR 0038). The source
/// generator implements it on every <see cref="SerializedAttribute"/> type;
/// static members, so generic code calls the generated code directly.
/// </summary>
public interface IJsonSerialized<T> where T : IJsonSerialized<T>
{
    /// <summary>Writes the value, or null for a null reference.</summary>
    static abstract void WriteJson(Utf8JsonWriter writer, in T value);

    /// <summary>
    /// Reads a value starting at the reader's current token (an object, or
    /// null for a class). Leaves the reader on the value's last token.
    /// </summary>
    static abstract T ReadJson(ref Utf8JsonReader reader);
}
