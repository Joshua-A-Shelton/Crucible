using System.Runtime.InteropServices;
using Crucible.Binary;
using Crucible.Importers;
using Crucible.Serialization;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Assets.Importers;

/// <summary>A model's import settings, kept in its sidecar (ADR 0043).</summary>
[Serialized]
public partial class MeshImportSettings
{
    /// <summary>Order triangles for the vertex cache and overdraw, and vertices for fetching.</summary>
    public bool Optimize = true;

    /// <summary>Make tangents with MikkTSpace where the source has none (and has UVs).</summary>
    public bool GenerateTangents = true;

    public bool GenerateLods = true;

    /// <summary>Levels after LOD 0, at most.</summary>
    public int MaxLods = 4;

    /// <summary>Each level keeps about this share of the last's triangles.</summary>
    public float LodRatio = 0.5f;

    /// <summary>Levels stop once they deviate more than this share of the mesh's size.</summary>
    public float LodMaxError = 0.05f;
}

/// <summary>
/// Imports glTF models (.gltf with its buffers, or .glb) as MESH binary files
/// (ADR 0043): every mesh of the file, its node hierarchy, and its materials,
/// mirrored into Crucible's space. Buffers in other files are read as
/// dependencies, so changing one reimports the model; textures are other
/// assets, referenced by ID. Names are placeholders (docs/naming.md).
/// </summary>
public sealed class MeshImporter : Importer<MeshImportSettings>
{
    public static readonly uint Kind = Tag.Of("MESH");
    public static readonly uint MaterialSection = Tag.Of("MATL");
    public const uint KindVersion = 1;

    public override string Name => "Mesh";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".gltf", ".glb"];

    public const string FileExtension = ".crmesh";

    public override string OutputExtension => FileExtension;

    public override void Import(ImportContext context, MeshImportSettings settings)
    {
        GltfSourceHandle source = Native(() => ImportersApi.ParseGltf(context.ReadSource()));
        try
        {
            for (uint i = 0; i < ImportersApi.GltfBufferCount(source); ++i)
            {
                string path = context.Resolve(ImportersApi.GltfBufferUri(source, i));
                byte[] bytes;
                try
                {
                    bytes = context.ReadDependency(path);
                }
                catch (IOException exception)
                {
                    throw new InvalidDataException($"its buffer {path} can't be read: {exception.Message}", exception);
                }
                Native(() => ImportersApi.SetGltfBuffer(source, i, bytes));
            }
            MeshImportHandle mesh = Native(() => ImportersApi.ImportGltf(source, ToNative(settings)));
            try
            {
                Write(context, mesh, uri => [context.Resolve(uri)]);
            }
            finally
            {
                ImportersApi.DestroyMeshImport(mesh);
            }
        }
        finally
        {
            ImportersApi.DestroyGltfSource(source);
        }
    }

    internal static MeshSettings ToNative(MeshImportSettings settings) => new()
    {
        Optimize = settings.Optimize,
        GenerateTangents = settings.GenerateTangents,
        GenerateLods = settings.GenerateLods,
        MaxLods = (uint)Math.Clamp(settings.MaxLods, 0, 8),
        LodRatio = settings.LodRatio,
        LodMaxError = Math.Max(0, settings.LodMaxError),
    };

    /// <summary>
    /// Writes an import as a MESH file, shared by every model format. Each
    /// texture's file, as the source names it, becomes the asset at the first
    /// of <paramref name="candidates"/>' paths that is one.
    /// </summary>
    internal static void Write(ImportContext context, MeshImportHandle mesh, Func<string, IReadOnlyList<string>> candidates)
    {
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        for (uint i = 0; i < ImportersApi.MeshSectionCount(mesh); ++i)
        {
            MeshSectionInfo section = ImportersApi.MeshSection(mesh, i);
            byte[] bytes = new byte[section.Size];
            ImportersApi.CopyMeshSection(mesh, i, bytes);
            output.Add(section.Tag, section.Version, bytes);
        }

        // Materials, with each texture's file turned into its asset ID.
        MaterialEntry[] materials = new MaterialEntry[ImportersApi.MeshMaterialCount(mesh)];
        for (uint m = 0; m < materials.Length; ++m)
        {
            MaterialEntry material = ImportersApi.MeshMaterial(mesh, m);
            foreach (TextureRole role in Enum.GetValues<TextureRole>())
            {
                string uri = ImportersApi.MeshMaterialTexture(mesh, m, role);
                if (uri.Length == 0)
                {
                    continue;
                }
                IReadOnlyList<string> paths = candidates(uri);
                AssetId texture = paths.Select(context.IdOf).FirstOrDefault(id => !id.IsNone);
                if (texture.IsNone)
                {
                    string looked = paths.Count == 0 ? uri : string.Join(" or ", paths);
                    context.Warn($"material {m}: its {role} texture, {looked}, isn't in Assets/");
                    continue;
                }
                texture.Value.TryWriteBytes(TextureSlot(ref material, role));
            }
            materials[m] = material;
        }
        output.Add(MaterialSection, 1, MemoryMarshal.AsBytes(materials.AsSpan()));

        for (uint i = 0; i < ImportersApi.MeshWarningCount(mesh); ++i)
        {
            context.Warn(ImportersApi.MeshWarning(mesh, i));
        }
    }

    /// <summary>A material's texture ID for a role: the GUID's bytes as <see cref="Guid.TryWriteBytes(Span{byte})"/> writes them.</summary>
    public static Span<byte> TextureSlot(ref MaterialEntry material, TextureRole role) => role switch
    {
        TextureRole.BaseColor => material.BaseColorTexture,
        TextureRole.MetallicRoughness => material.MetallicRoughnessTexture,
        TextureRole.Normal => material.NormalTexture,
        TextureRole.Occlusion => material.OcclusionTexture,
        _ => material.EmissiveTexture,
    };

    // The importers report failure as InvalidData with a reason.
    internal static T Native<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (CrucibleException exception) when (exception.Status == Status.InvalidData)
        {
            throw new InvalidDataException(ImportersApi.LastImportError(), exception);
        }
    }

    internal static void Native(Action call) => Native(() =>
    {
        call();
        return 0;
    });
}
