using System.Buffers;
using System.Text.Json;
using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// A foreign file's companion, <c>&lt;file&gt;.crmeta</c> (ADR 0021, 0040): its
/// GUID, its importer, the importer's settings, and the GUIDs of the
/// sub-assets its last import made, by name (ADR 0078).
/// </summary>
/// <remarks>
/// Written by hand rather than generated, because the settings' type depends
/// on the importer:
/// <code>
/// {
///   "Guid": "8f14e45f-ceea-467a-9a36-dedd4bea2543",
///   "Importer": "Texture",
///   "Settings": { ... },
///   "SubAssets": { "Walk": "0d8a…", "Idle": "71c2…" }
/// }
/// </code>
/// </remarks>
internal sealed record Sidecar(AssetId Id, string Importer, byte[] SettingsJson)
{
    public IReadOnlyDictionary<string, AssetId> SubAssets { get; init; } = new Dictionary<string, AssetId>();

    public const string Extension = ".crmeta";

    public static Sidecar Read(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);
        reader.Read();
        JsonValues.Expect(reader, JsonTokenType.StartObject, "a sidecar object");
        AssetId id = AssetId.None;
        string importer = "";
        byte[] settings = [];
        Dictionary<string, AssetId> subAssets = new(StringComparer.Ordinal);
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
                case "SubAssets":
                    JsonValues.Expect(reader, JsonTokenType.StartObject, "an object of sub-asset GUIDs");
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string name = reader.GetString()!;
                        reader.Read();
                        subAssets[name] = JsonValues.ReadAssetId(ref reader);
                    }
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
        return new Sidecar(id, importer, settings) { SubAssets = subAssets };
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
            if (SubAssets.Count > 0)
            {
                writer.WritePropertyName("SubAssets"u8);
                writer.WriteStartObject();
                foreach ((string name, AssetId id) in SubAssets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    JsonValues.WriteAssetId(writer, id);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }
}
