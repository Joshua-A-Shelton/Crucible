using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Importers;

namespace Crucible.Tests.Assets;

// The FBX importer (ADR 0044) through the asset database, on ufbx's sample
// files (CTest sets CRUCIBLE_UFBX_DATA to their folder).
public class FbxImporterTests
{
    private static string Sample(string name)
    {
        string? folder = Environment.GetEnvironmentVariable("CRUCIBLE_UFBX_DATA");
        if (folder is null)
        {
            throw new InvalidOperationException("CRUCIBLE_UFBX_DATA isn't set: run these tests through CTest");
        }
        return Path.Combine(folder, name);
    }

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-fbx-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new FbxImporter()]);

        public string PathOf(string assetPath)
        {
            string path = Path.Combine(Project.AssetsFolder, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static ImportResult Result(AssetDatabase database, string path) =>
        database.ImportAll().Single(result => result.Path == path);

    [Test]
    public async Task TexturesAreFoundByTheirStoredPathThenByNameBesideTheModel()
    {
        using Scratch scratch = new();
        // The FBX names textures\checkerboard_*.png, relative to itself.
        File.Copy(Sample("blender_293_textures_7400_binary.fbx"), scratch.PathOf("Models/cube.fbx"));
        File.WriteAllBytes(scratch.PathOf("Models/textures/checkerboard_diffuse.png"), [1]);
        File.WriteAllBytes(scratch.PathOf("Models/checkerboard_emissive.png"), [2]);
        AssetDatabase database = scratch.Open();
        ImportResult result = Result(database, "Models/cube.fbx");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, MeshImporter.Kind, MeshImporter.KindVersion);
        MaterialEntry material = MemoryMarshal.Read<MaterialEntry>(file.Read(file.Find(MeshImporter.MaterialSection)));
        AssetId diffuse = database.Find("Models/textures/checkerboard_diffuse.png")!.Id;
        AssetId emissive = database.Find("Models/checkerboard_emissive.png")!.Id;
        await Assert.That(new Guid(MeshImporter.TextureSlot(ref material, TextureRole.BaseColor))).IsEqualTo(diffuse.Value);
        await Assert.That(new Guid(MeshImporter.TextureSlot(ref material, TextureRole.Emissive))).IsEqualTo(emissive.Value);

        // Blender's 2 m cube, in meters whatever the file's own units. Its
        // export scales the node by 100 and the vertices by 1/100.
        float[] positions = MemoryMarshal.Cast<byte, float>(file.Read(file.Find(Tag.Of("POSN")))).ToArray();
        MeshNode node = MemoryMarshal.Read<MeshNode>(file.Read(file.Find(Tag.Of("NODE"))));
        await Assert.That(positions.Max() * node.Scale[0]).IsEqualTo(1f).Within(0.001f);
        await Assert.That(positions.Min() * node.Scale[0]).IsEqualTo(-1f).Within(0.001f);
    }

    [Test]
    public async Task AMissingTextureIsAWarningNamingWhereItWasLookedFor()
    {
        using Scratch scratch = new();
        File.Copy(Sample("blender_293_textures_7400_binary.fbx"), scratch.PathOf("Models/cube.fbx"));
        ImportResult result = Result(scratch.Open(), "Models/cube.fbx");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(result.Warnings).Contains(
            "material 0: its BaseColor texture, Models/textures/checkerboard_diffuse.png or Models/checkerboard_diffuse.png, isn't in Assets/");
    }

    [Test]
    public async Task AFileThatIsNotFbxFails()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.PathOf("broken.fbx"), "not an FBX");
        ImportResult result = Result(scratch.Open(), "broken.fbx");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error).Contains("can't read the FBX");
    }
}
