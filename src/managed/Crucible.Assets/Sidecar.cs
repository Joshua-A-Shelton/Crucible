using System.Buffers;
using System.Text.Json;
using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// A foreign file's companion, <c>&lt;file&gt;.crmeta</c> (ADR 0021, 0040): its
/// GUID, its importer, and the importer's settings.
/// </summary>
/// <remarks>
/// Written by hand rather than generated, because the settings' type depends
/// on the importer:
/// <code>
/// {
///   "Guid": "8f14e45f-ceea-467a-9a36-dedd4bea2543",
///   "Importer": "Texture",
///   "Settings": { ... }
/// }
/// </code>
/// </remarks>
internal sealed record Sidecar(AssetId Id, string Importer, byte[] SettingsJson)
{
    public const string Extension = ".crmeta";

    public static Sidecar Read(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);
        reader.Read();
        JsonValues.Expect(reader, JsonTokenType.StartObject, "a sidecar object");
        AssetId id = AssetId.None;
        string importer = "";
        byte[] settings = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string property = reader.GetString()!;
            reader.Read();
            switch (property)
            {
                case "Guid":
                    id = JsonValues.ReadAssetId(ref reader);
                    break;
                case "Importer":
                    importer = JsonValues.ReadString(ref reader) ?? "";
                    break;
                case "Settings":
                    // Kept as JSON for the importer to read.
                    int start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    settings = json[start..(int)reader.BytesConsumed].ToArray();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        if (id.IsNone)
        {
            throw new JsonException("the sidecar has no Guid");
        }
        return new Sidecar(id, importer, settings);
    }

    public byte[] Write(Importer? importer)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Guid"u8);
            JsonValues.WriteAssetId(writer, Id);
            writer.WriteString("Importer"u8, Importer);
            if (importer is not null)
            {
                writer.WritePropertyName("Settings"u8);
                importer.WriteSettings(writer, SettingsJson);
            }
            writer.WriteEndObject();
        }
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }
}
