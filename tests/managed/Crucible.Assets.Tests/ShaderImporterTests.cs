using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// The Shader importer (ADR 0052): material shaders in a project, their
// material types, the C# written for them, and their imports as
// dependencies.

public class ShaderImporterTests
{
    private const string Common = """
        public float3 tinted(float3 color, float amount) { return color * amount; }
        """;

    private const string Bricks = """
        import crucible.material;
        import common;

        public struct Brick : IMaterial
        {
            public float4 tint = float4(1, 0.5, 0.25, 1);
            public TextureSlot albedo;
            public float roughness = 0.75;
            public float weights[3] = { 1, 2, 3 };
            public typealias Shading = ToonShading;
            public SurfaceData surface(SurfaceInput input)
            {
                SurfaceData s = SurfaceData.defaults(input);
                s.albedo = tinted(tint.rgb * albedo.sample(input.uv0, float4(1, 1, 1, 1)).rgb, weights[1]);
                s.roughness = roughness;
                return s;
            }
            public ToonData shading(SurfaceInput input) { return ToonData.defaults(); }
        }
        """;

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-shaders-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new ShaderImporter()]);

        public string PathOf(string assetPath)
        {
            string path = Path.Combine(Project.AssetsFolder, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    [Test]
    public async Task AMaterialShaderImportsWithItsTypesAndGeneratedCode()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.PathOf("Materials/common.slang"), Common);
        File.WriteAllText(scratch.PathOf("Materials/Bricks.slang"), Bricks);
        AssetDatabase database = scratch.Open();
        ImportResult result = database.ImportAll().Single(r => r.Path == "Materials/Bricks.slang");
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);

        using ImportedFile file = ImportedFile.Open(result.OutputPath!, ShaderImporter.Kind, ShaderImporter.KindVersion);
        List<MaterialTypes.Type> types = MaterialTypes.Read(file.Read(file.Find(MaterialTypes.Tag)));
        await Assert.That(string.Join(",", types.Select(type => type.Name))).IsEqualTo("Brick");
        await Assert.That(types[0].Size).IsEqualTo(36u);

        string generated = File.ReadAllText(Path.Combine(scratch.Project.CodeFolder, "Generated", "Materials", "Brick.g.cs"));
        AssetId shader = database.Find("Materials/Bricks.slang")!.Id;
        await Assert.That(generated).Contains($"AssetId.Parse(\"{shader}\")");
        await Assert.That(generated).Contains("[FieldOffset(16)] public TextureSlot Albedo;");
        await Assert.That(generated).Contains("[FieldOffset(24)] public BrickWeightsArray Weights;");
        await Assert.That(generated).Contains("value.Tint = new Vector4(1f, 0.5f, 0.25f, 1f);");
        await Assert.That(generated).Contains("value.Weights[2] = 3f;");
        await Assert.That(generated).Contains("[InlineArray(3)]");
    }

    [Test]
    public async Task ChangingAnImportedModuleReimportsTheShader()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.PathOf("Materials/common.slang"), Common);
        File.WriteAllText(scratch.PathOf("Materials/Bricks.slang"), Bricks);
        AssetDatabase database = scratch.Open();
        _ = database.ImportAll();
        await Assert.That(database.ImportAll().Single(r => r.Path == "Materials/Bricks.slang").Outcome)
            .IsEqualTo(ImportOutcome.UpToDate);

        File.WriteAllText(scratch.PathOf("Materials/common.slang"), Common + "\n// changed\n");
        database.Refresh();
        await Assert.That(database.ImportAll().Single(r => r.Path == "Materials/Bricks.slang").Outcome)
            .IsEqualTo(ImportOutcome.Imported);
    }

    [Test]
    public async Task AShaderThatDoesntCompileFailsWithSlangsMessage()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.PathOf("Broken.slang"), "public struct Broken { float x }");
        ImportResult result = scratch.Open().ImportAll().Single(r => r.Path == "Broken.slang");
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error!).Contains("doesn't compile");
    }
}
