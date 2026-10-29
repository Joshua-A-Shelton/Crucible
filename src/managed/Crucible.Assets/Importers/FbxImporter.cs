using Crucible.Importers;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Assets.Importers;

/// <summary>
/// Imports FBX models as MESH binary files (ADR 0044), like
/// <see cref="MeshImporter"/> does glTF: ufbx converts each file's axes and
/// units to meters with Y up, and the shared mesh builder does the rest.
/// Its own importer, so updating ufbx reimports only FBX models. Names are
/// placeholders (docs/naming.md).
/// </summary>
public sealed class FbxImporter : Importer<MeshImportSettings>
{
    public override string Name => "Fbx";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".fbx"];

    public override void Import(ImportContext context, MeshImportSettings settings)
    {
        byte[] bytes = context.ReadSource();
        MeshImportHandle mesh = MeshImporter.Native(() => ImportersApi.ImportFbx(bytes, MeshImporter.ToNative(settings)));
        try
        {
            MeshImporter.Write(context, mesh, path => TexturePaths(context, path));
        }
        finally
        {
            ImportersApi.DestroyMeshImport(mesh);
        }
    }

    /// <summary>
    /// Where to look for a texture the FBX names: the path it stores, if that
    /// is relative and stays inside <c>Assets/</c>, then a file of the same
    /// name beside the model, since FBX files often keep the artist's own
    /// absolute paths (ADR 0044).
    /// </summary>
    internal static IReadOnlyList<string> TexturePaths(ImportContext context, string stored)
    {
        string path = stored.Replace('\\', '/');
        List<string> result = [];
        bool absolute = path.StartsWith('/') || (path.Length >= 2 && path[1] == ':');
        if (!absolute)
        {
            try
            {
                result.Add(context.Resolve(path));
            }
            catch (InvalidDataException)
            {
                // Outside Assets/: only the name can still find it.
            }
        }
        string besideModel = context.Resolve(path[(path.LastIndexOf('/') + 1)..]);
        if (!result.Contains(besideModel))
        {
            result.Add(besideModel);
        }
        return result;
    }
}
