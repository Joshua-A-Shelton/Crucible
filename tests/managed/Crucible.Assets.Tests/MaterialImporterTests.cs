using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// The material importer (ADR 0052): .crmaterial files laid out with their
// shader's reflection into .crmat records.

public class MaterialImporterTests
{
    private const string Bricks = """
        import crucible.material;

        public struct Brick : IMaterial
        {
            public float4 tint = float4(1, 0.5, 0.25, 1);
            public TextureSlot albedo;
            public float roughness = 0.75;
            public float weights[3] = { 1, 2, 3 };
            public uint flags;
            public typealias Shading = ToonShading;
            public SurfaceData surface(SurfaceInput input)
            {
                SurfaceData s = SurfaceData.defaults(input);
                s.albedo = tint.rgb * albedo.sample(input.uv0, float4(1, 1, 1, 1)).rgb * weights[1];
                s.roughness = roughness;
                return s;
            }
            public ToonData shading(SurfaceInput input) { return ToonData.defaults(); }
        }
        """;

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-materials-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new ShaderImporter(), new MaterialImporter()]);

        public string PathOf(string assetPath)
        {
            string path = Path.Combine(Project.AssetsFolder, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    // A project with the shader and a texture (any file has an ID), then the
    // material written against their IDs.
    private static (AssetDatabase Database, AssetId Shader, AssetId Texture) Setup(Scratch scratch, string material)
    {
        File.WriteAllText(scratch.PathOf("Materials/Bricks.slang"), Bricks);
        File.WriteAllBytes(scratch.PathOf("Textures/brick.png"), [1, 2, 3]);
        AssetDatabase database = scratch.Open();
        AssetId shader = database.Find("Materials/Bricks.slang")!.Id;
        AssetId texture = database.Find("Textures/brick.png")!.Id;
        File.WriteAllText(scratch.PathOf("Materials/wall.crmaterial"),
            material.Replace("SHADER", shader.ToString()).Replace("TEXTURE", texture.ToString()));
        database.Refresh();
        return (database, shader, texture);
    }

    private const string Wall = """
        {
          "$guid": "2f1e1c3a-6b5d-4e7f-8a9b-0c1d2e3f4a5b",
          "Shader": "SHADER",
          "Type": "Brick",
          "AlphaMode": "Mask",
          "DoubleSided": true,
          "AlphaCutoff": 0.3,
          "Values": { "roughness": 0.25, "weights": [4, 5, 6], "flags": 9 },
          "Textures": { "albedo": "TEXTURE" }
        }
        """;

    [Test]
    public async Task AMaterialIsLaidOutByItsShader()
    {
        using Scratch scratch = new();
        (AssetDatabase database, AssetId shader, AssetId texture) = Setup(scratch, Wall);
        ImportResult result = database.ImportAll().Single(r => r.Path == "Materials/wall.crmaterial");
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, MaterialImporter.Kind, MaterialImporter.KindVersion);
        byte[] record = file.Read(file.Find(MaterialImporter.RecordSection));
        MaterialInfo info = MemoryMarshal.Read<MaterialInfo>(record);
        await Assert.That(new Guid(info.Shader)).IsEqualTo(shader.Value);
        await Assert.That((info.Type, info.AlphaMode, info.DoubleSided, info.AlphaCutoff))
            .IsEqualTo((0u, AlphaMode.Mask, 1u, 0.3f));
        await Assert.That(info.ParameterSize).IsEqualTo(40u);
        float[] floats = MemoryMarshal.Cast<byte, float>(record.AsSpan((int)info.ParameterOffset, 36)).ToArray();
        await Assert.That(floats[0]).IsEqualTo(1f);    // tint: the default
        await Assert.That(floats[5]).IsEqualTo(0.25f); // roughness
        await Assert.That(string.Join(",", floats[6..9])).IsEqualTo("4,5,6");
        await Assert.That(MemoryMarshal.Read<uint>(record.AsSpan((int)info.ParameterOffset + 36))).IsEqualTo(9u);
        await Assert.That(info.TextureCount).IsEqualTo(1u);
        MaterialTexture slot = MemoryMarshal.Read<MaterialTexture>(record.AsSpan((int)info.TextureOffset));
        await Assert.That(slot.Offset).IsEqualTo(16u);
        await Assert.That(new Guid(slot.Texture)).IsEqualTo(texture.Value);
    }

    [Test]
    public async Task ChangingTheShaderReimportsItsMaterials()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, _) = Setup(scratch, Wall);
        _ = database.ImportAll();
        File.WriteAllText(scratch.PathOf("Materials/Bricks.slang"), Bricks + "\n// changed\n");
        database.Refresh();
        await Assert.That(database.ImportAll().Single(r => r.Path == "Materials/wall.crmaterial").Outcome)
            .IsEqualTo(ImportOutcome.Imported);
    }

    [Test]
    [Arguments("\"Values\": { \"shininess\": 1 }", "has no parameter shininess")]
    [Arguments("\"Values\": { \"weights\": [1, 2] }", "weights takes an array of 3")]
    [Arguments("\"Values\": { \"albedo\": 1 }", "albedo is a texture")]
    [Arguments("\"Values\": { \"flags\": -1 }", "flags takes whole numbers from 0")]
    [Arguments("\"Textures\": { \"roughness\": \"TEXTURE\" }", "roughness isn't a TextureSlot")]
    public async Task MistakesAreReportedByName(string body, string message)
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, _) = Setup(scratch, $$"""
            { "$guid": "2f1e1c3a-6b5d-4e7f-8a9b-0c1d2e3f4a5b", "Shader": "SHADER", "Type": "Brick", {{body}} }
            """);
        ImportResult result = database.ImportAll().Single(r => r.Path == "Materials/wall.crmaterial");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error!).Contains(message);
    }

    [Test]
    public async Task AnUnknownTypeListsTheShadersMaterials()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, _) = Setup(scratch, """
            { "$guid": "2f1e1c3a-6b5d-4e7f-8a9b-0c1d2e3f4a5b", "Shader": "SHADER", "Type": "Stone" }
            """);
        ImportResult result = database.ImportAll().Single(r => r.Path == "Materials/wall.crmaterial");
        await Assert.That(result.Error!).Contains("no material Stone; it has Brick");
    }
}
