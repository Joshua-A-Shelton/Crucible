using System.Runtime.InteropServices;
using System.Text;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Importers;

namespace Crucible.Tests.Assets;

// The glTF importer (ADR 0043) through the asset database.
public class MeshImporterTests
{
    // One textured triangle: positions, normals, and UVs as floats, then
    // three 32-bit indices.
    private static byte[] TriangleBuffer()
    {
        float[] floats =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 0, // positions
            0, 0, 1, 0, 0, 1, 0, 0, 1, // normals
            0, 1, 1, 1, 0, 0, // UVs
        ];
        uint[] indices = [0, 1, 2];
        return [.. MemoryMarshal.AsBytes(floats.AsSpan()), .. MemoryMarshal.AsBytes(indices.AsSpan())];
    }

    private static string TriangleJson(string bufferUri, string texture) => $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [ { {{(bufferUri.Length == 0 ? "" : $"\"uri\": \"{bufferUri}\", ")}}"byteLength": 108 } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 24 },
            { "buffer": 0, "byteOffset": 96, "byteLength": 12 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
            { "bufferView": 3, "componentType": 5125, "count": 3, "type": "SCALAR" }
          ],
          "images": [ { "uri": "{{texture}}" } ],
          "textures": [ { "source": 0 } ],
          "materials": [ { "name": "brick", "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ],
          "meshes": [ { "name": "triangle", "primitives": [
            { "attributes": { "POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2 }, "indices": 3, "material": 0 } ] } ],
          "nodes": [ { "name": "root", "mesh": 0 } ]
        }
        """;

    // A .glb: a 12-byte header, then the JSON and binary chunks, each padded
    // to four bytes.
    private static byte[] Glb(string json, byte[] binary)
    {
        byte[] text = Encoding.UTF8.GetBytes(json);
        int jsonLength = (text.Length + 3) & ~3, binLength = (binary.Length + 3) & ~3;
        byte[] file = new byte[12 + 8 + jsonLength + 8 + binLength];
        BitConverter.TryWriteBytes(file.AsSpan(0), 0x46546C67u); // "glTF"
        BitConverter.TryWriteBytes(file.AsSpan(4), 2u);
        BitConverter.TryWriteBytes(file.AsSpan(8), (uint)file.Length);
        BitConverter.TryWriteBytes(file.AsSpan(12), (uint)jsonLength);
        BitConverter.TryWriteBytes(file.AsSpan(16), 0x4E4F534Au); // "JSON"
        file.AsSpan(20, jsonLength).Fill((byte)' ');
        text.CopyTo(file, 20);
        int at = 20 + jsonLength;
        BitConverter.TryWriteBytes(file.AsSpan(at), (uint)binLength);
        BitConverter.TryWriteBytes(file.AsSpan(at + 4), 0x004E4942u); // "BIN\0"
        binary.CopyTo(file, at + 8);
        return file;
    }

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-meshes-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new MeshImporter()]);

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
    public async Task AModelBecomesAMeshFileWithItsTexturesById()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("Models/tri.bin"), TriangleBuffer());
        File.WriteAllText(scratch.PathOf("Models/tri.gltf"), TriangleJson("tri.bin", "../Textures/brick.png"));
        File.WriteAllBytes(scratch.PathOf("Textures/brick.png"), [1, 2, 3]); // any file: it only needs an ID
        AssetDatabase database = scratch.Open();
        ImportResult result = Result(database, "Models/tri.gltf");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(result.Warnings).IsEmpty();

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, MeshImporter.Kind, MeshImporter.KindVersion);
        foreach (string tag in (string[])["INFO", "MSHS", "LODS", "RNGS", "POSN", "NRML", "TNGT", "UV0_", "INDX", "NODE", "NAME", "MATL"])
        {
            await Assert.That(file.Find(Tag.Of(tag))).IsGreaterThanOrEqualTo(0);
        }
        MeshFileInfo info = MemoryMarshal.Read<MeshFileInfo>(file.Read(file.Find(Tag.Of("INFO"))));
        await Assert.That((info.MeshCount, info.NodeCount, info.MaterialCount, info.VertexCount)).IsEqualTo((1u, 1u, 1u, 3u));

        MaterialEntry material = MemoryMarshal.Read<MaterialEntry>(file.Read(file.Find(MeshImporter.MaterialSection)));
        AssetId brick = database.Find("Textures/brick.png")!.Id;
        await Assert.That(new Guid(MeshImporter.TextureSlot(ref material, TextureRole.BaseColor))).IsEqualTo(brick.Value);
        await Assert.That(new Guid(MeshImporter.TextureSlot(ref material, TextureRole.Normal))).IsEqualTo(Guid.Empty);

        // Mirrored on X: the vertex at glTF x = 1 is at x = -1.
        float[] positions = MemoryMarshal.Cast<byte, float>(file.Read(file.Find(Tag.Of("POSN")))).ToArray();
        await Assert.That(positions.Where((_, i) => i % 3 == 0).Min()).IsEqualTo(-1f);
    }

    [Test]
    public async Task ChangingABufferReimportsTheModel()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("tri.bin"), TriangleBuffer());
        File.WriteAllText(scratch.PathOf("tri.gltf"), TriangleJson("tri.bin", "brick.png"));
        AssetDatabase database = scratch.Open();
        await Assert.That(Result(database, "tri.gltf").Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(Result(database, "tri.gltf").Outcome).IsEqualTo(ImportOutcome.UpToDate);

        byte[] moved = TriangleBuffer();
        BitConverter.TryWriteBytes(moved.AsSpan(4 * 4), 2f); // the second vertex's y
        File.WriteAllBytes(scratch.PathOf("tri.bin"), moved);
        File.SetLastWriteTimeUtc(scratch.PathOf("tri.bin"), DateTime.UtcNow.AddMinutes(5));
        database.Refresh();
        await Assert.That(Result(database, "tri.gltf").Outcome).IsEqualTo(ImportOutcome.Imported);
    }

    [Test]
    public async Task MissingBuffersAndTexturesAreReported()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.PathOf("lost.gltf"), TriangleJson("lost.bin", "brick.png"));
        ImportResult lost = Result(scratch.Open(), "lost.gltf");
        await Assert.That(lost.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(lost.Error).Contains("lost.bin can't be read");

        File.WriteAllBytes(scratch.PathOf("lost.bin"), TriangleBuffer());
        AssetDatabase database = scratch.Open();
        ImportResult imported = Result(database, "lost.gltf");
        await Assert.That(imported.Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(imported.Warnings.Single()).Contains("brick.png, isn't in Assets/");
    }

    [Test]
    public async Task BinaryGltfImportsToo()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("tri.glb"), Glb(TriangleJson("", "brick.png"), TriangleBuffer()));
        ImportResult result = Result(scratch.Open(), "tri.glb");
        await Assert.That(result.Error).IsNull();
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, MeshImporter.Kind, MeshImporter.KindVersion);
        await Assert.That(MemoryMarshal.Read<MeshFileInfo>(file.Read(file.Find(Tag.Of("INFO")))).VertexCount).IsEqualTo(3u);
    }

    [Test]
    public async Task PathsResolveRelativeToTheModel()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("Models/Props/a.bin"), TriangleBuffer());
        File.WriteAllText(scratch.PathOf("Models/Props/crate.gltf"), TriangleJson("./a.bin", "../../Textures/wood.png"));
        AssetDatabase database = scratch.Open();
        ImportResult result = Result(database, "Models/Props/crate.gltf");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(result.Warnings.Single()).Contains("Textures/wood.png");

        File.WriteAllText(scratch.PathOf("escape.gltf"), TriangleJson("../outside.bin", "x.png"));
        database.Refresh();
        await Assert.That(Result(database, "escape.gltf").Error).Contains("outside Assets/");
    }
}
