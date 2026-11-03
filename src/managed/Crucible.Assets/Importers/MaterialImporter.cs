using System.Runtime.InteropServices;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A material has no import settings: everything is in the file.</summary>
[Serialized]
public partial class MaterialImportSettings;

/// <summary>
/// Imports materials (ADR 0052): an authored <c>.crmaterial</c> (JSON with
/// its GUID inside, like scenes) naming a material shader and one of its
/// types, with values by parameter name and textures by asset ID, laid out
/// with the shader's reflection into a <c>.crmat</c>: a MATR file whose MTLS
/// section holds one record (MaterialFile.h). The shader is a dependency:
/// changing it reimports its materials.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Shader": "…",                       the material shader's asset ID
///   "Type": "Brick",                     one of its IMaterial types
///   "AlphaMode": "Opaque",               Opaque, Mask, or Blend
///   "DoubleSided": false,
///   "AlphaCutoff": 0.5,
///   "Values": { "roughness": 0.25, "tint": [1, 0.5, 0.25, 1] },
///   "Textures": { "albedo": "…" }        texture asset IDs
/// }
/// </code>
/// Parameters not given keep the shader's defaults (its field initializers).
/// </remarks>
public sealed class MaterialImporter : Importer<MaterialImportSettings>
{
    public static readonly uint Kind = Tag.Of("MATR");
    public const uint KindVersion = 2;
    public const string FileExtension = ".crmaterial";
    public static readonly uint RecordSection = Tag.Of("MTLS");

    public override string Name => "Material";

    public override int Version => 2;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ".crmat";

    public override void Import(ImportContext context, MaterialImportSettings settings)
    {
        using JsonDocument document = JsonDocument.Parse(context.ReadSource());
        JsonElement root = document.RootElement;
        AssetId shaderId = AssetId.TryParse(String(root, "Shader"), out AssetId parsed)
            ? parsed
            : throw new InvalidDataException("Shader must be a material shader's asset ID");
        string typeName = String(root, "Type");
        byte[] shaderFile = context.ReadImported(shaderId) ??
                            throw new InvalidDataException($"its shader, {shaderId}, isn't an asset that imports");
        List<MaterialTypes.Type> types;
        using (ImportedFile shader = ImportedFile.Parse(shaderFile, ShaderImporter.Kind, ShaderImporter.KindVersion))
        {
            int section = shader.Find(MaterialTypes.Tag);
            types = section < 0 ? [] : MaterialTypes.Read(shader.Read(section));
        }
        int typeIndex = types.FindIndex(type => type.Name == typeName);
        if (typeIndex < 0)
        {
            throw new InvalidDataException(types.Count == 0
                ? $"its shader declares no materials (structs implementing IMaterial)"
                : $"its shader has no material {typeName}; it has {string.Join(", ", types.Select(t => t.Name))}");
        }
        MaterialTypes.Type type = types[typeIndex];
        byte[] parameters = (byte[])type.Defaults.Clone();

        if (root.TryGetProperty("Values", out JsonElement values))
        {
            foreach (JsonProperty value in values.EnumerateObject())
            {
                MaterialTypes.Parameter parameter = Find(type, value.Name);
                if (parameter.Kind == MaterialParameterKind.Texture)
                {
                    throw new InvalidDataException($"{value.Name} is a texture: give it under Textures");
                }
                WriteValue(parameters, parameter, value.Value);
            }
        }

        List<MaterialTexture> textures = [];
        if (root.TryGetProperty("Textures", out JsonElement textureValues))
        {
            foreach (JsonProperty texture in textureValues.EnumerateObject())
            {
                MaterialTypes.Parameter parameter = Find(type, texture.Name);
                if (parameter.Kind != MaterialParameterKind.Texture)
                {
                    throw new InvalidDataException($"{texture.Name} isn't a TextureSlot: give it under Values");
                }
                JsonElement[] ids = parameter.ArrayLength > 0
                    ? Elements(texture.Value, parameter.ArrayLength, texture.Name)
                    : [texture.Value];
                for (int e = 0; e < ids.Length; ++e)
                {
                    if (ids[e].ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }
                    if (!AssetId.TryParse(ids[e].GetString(), out AssetId id))
                    {
                        throw new InvalidDataException($"{texture.Name}: a texture is given by its asset ID");
                    }
                    MaterialTexture entry = new() { Offset = (uint)(parameter.Offset + e * parameter.ArrayStride) };
                    id.Value.TryWriteBytes(entry.Texture);
                    textures.Add(entry);
                }
            }
        }

        AlphaMode alphaMode = root.TryGetProperty("AlphaMode", out JsonElement mode)
            ? Enum.TryParse(mode.GetString(), out AlphaMode chosen) && Enum.IsDefined(chosen)
                ? chosen
                : throw new InvalidDataException("AlphaMode is Opaque, Mask, or Blend")
            : AlphaMode.Opaque;
        MaterialInfo info = new()
        {
            Type = (uint)typeIndex,
            AlphaMode = alphaMode,
            DoubleSided = root.TryGetProperty("DoubleSided", out JsonElement sides) && sides.GetBoolean() ? 1u : 0u,
            AlphaCutoff = root.TryGetProperty("AlphaCutoff", out JsonElement cutoff) ? cutoff.GetSingle() : 0.5f,
        };
        if (root.TryGetProperty("Outline", out JsonElement outline))
        {
            MeshImporter.Outline(ref info, ReadOutline(outline));
        }
        shaderId.Value.TryWriteBytes(info.Shader);
        context.CreateOutput(Kind, KindVersion).Add(RecordSection, 1, Record(info, parameters, textures));
    }

    // "Outline": { "Width": pixels, "Color": [r, g, b], "Tint": 0 to 1 }, each
    // optional.
    private static OutlineSettings ReadOutline(JsonElement outline)
    {
        if (outline.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Outline is an object: { Width, Color, Tint }");
        }
        OutlineSettings result = default;
        foreach (JsonProperty property in outline.EnumerateObject())
        {
            switch (property.Name)
            {
            case "Width":
                result.Width = property.Value.GetSingle();
                break;
            case "Tint":
                result.Tint = property.Value.GetSingle();
                break;
            case "Color":
                JsonElement[] channels = Elements(property.Value, 3, "Outline.Color");
                result.Color = new System.Numerics.Vector3(channels[0].GetSingle(), channels[1].GetSingle(),
                    channels[2].GetSingle());
                break;
            default:
                throw new InvalidDataException($"Outline has no {property.Name}: it has Width, Color, and Tint");
            }
        }
        return result;
    }

    // One record: the info, the parameters, then the textures (4-aligned),
    // offsets from the section's start.
    internal static unsafe byte[] Record(MaterialInfo info, byte[] parameters, List<MaterialTexture> textures)
    {
        int parametersAt = sizeof(MaterialInfo);
        int texturesAt = (parametersAt + parameters.Length + 3) / 4 * 4;
        byte[] section = new byte[texturesAt + textures.Count * sizeof(MaterialTexture)];
        info.ParameterOffset = (uint)parametersAt;
        info.ParameterSize = (uint)parameters.Length;
        info.TextureOffset = (uint)texturesAt;
        info.TextureCount = (uint)textures.Count;
        MemoryMarshal.Write(section, in info);
        parameters.CopyTo(section, parametersAt);
        MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(textures)).CopyTo(section.AsSpan(texturesAt));
        return section;
    }

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{name} is missing");

    private static MaterialTypes.Parameter Find(MaterialTypes.Type type, string name) =>
        type.Parameters.FirstOrDefault(parameter => parameter.Name == name) ??
        throw new InvalidDataException($"{type.Name} has no parameter {name}; it has " +
                                       string.Join(", ", type.Parameters.Select(parameter => parameter.Name)));

    private static JsonElement[] Elements(JsonElement value, uint count, string name)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
        {
            throw new InvalidDataException($"{name} takes an array of {count}");
        }
        return [.. value.EnumerateArray()];
    }

    // A value as the parameter's type: a number for a scalar, an array for a
    // vector or matrix (row by row), and an array of those for an array.
    private static void WriteValue(byte[] parameters, MaterialTypes.Parameter parameter, JsonElement value)
    {
        JsonElement[] elements = parameter.ArrayLength > 0 ? Elements(value, parameter.ArrayLength, parameter.Name) : [value];
        uint components = parameter.Rows * parameter.Columns;
        for (int e = 0; e < elements.Length; ++e)
        {
            JsonElement[] numbers = components == 1 ? [elements[e]] : Elements(elements[e], components, parameter.Name);
            for (int c = 0; c < numbers.Length; ++c)
            {
                if (numbers[c].ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidDataException($"{parameter.Name} takes numbers");
                }
                Span<byte> at = parameters.AsSpan((int)(parameter.Offset + e * parameter.ArrayStride + c * 4), 4);
                switch (parameter.Kind)
                {
                    case MaterialParameterKind.Int:
                        MemoryMarshal.Write(at, numbers[c].TryGetInt32(out int i)
                            ? i
                            : throw new InvalidDataException($"{parameter.Name} takes whole numbers"));
                        break;
                    case MaterialParameterKind.Uint:
                        MemoryMarshal.Write(at, numbers[c].TryGetUInt32(out uint u)
                            ? u
                            : throw new InvalidDataException($"{parameter.Name} takes whole numbers from 0"));
                        break;
                    default:
                        MemoryMarshal.Write(at, numbers[c].GetSingle());
                        break;
                }
            }
        }
    }
}
