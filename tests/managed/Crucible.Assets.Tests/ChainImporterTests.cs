using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// The Chain importer (ADR 0095): a .crchain (JSON, its GUID inside) checked
// and laid out as ChainFile.h has it, in a .crchn.
public class ChainImporterTests
{
    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-chain-" + Guid.NewGuid().ToString("N")), "Game");

        public ImportResult Import(string text)
        {
            File.WriteAllText(Path.Combine(Project.AssetsFolder, "Hills.crchain"), text);
            return AssetDatabase.Open(Project, [new ChainImporter()]).ImportAll().Single();
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    [Test]
    public async Task AChainImportsItsPointsAndLoop()
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import("""
            { "$guid": "6f1d0c3e-2b7a-4c51-9e0f-3a8d5b2c1e01", "Points": [ [2, 0], [0, 1], [-2, 0] ], "Loop": true }
            """);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Id.ToString()).IsEqualTo("6f1d0c3e-2b7a-4c51-9e0f-3a8d5b2c1e01");
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, ChainImporter.Kind, ChainImporter.KindVersion);
        ChainInfo info = MemoryMarshal.Read<ChainInfo>(file.Read(file.Find(Tag.Of("INFO"))));
        Vector2[] points = MemoryMarshal.Cast<byte, Vector2>(file.Read(file.Find(Tag.Of("PNTS")))).ToArray();
        await Assert.That(info.PointCount).IsEqualTo(3u);
        await Assert.That(info.Loop).IsEqualTo(1u);
        await Assert.That(points.SequenceEqual([new Vector2(2, 0), new Vector2(0, 1), new Vector2(-2, 0)])).IsTrue();
    }

    [Test]
    public async Task AChainIsOpenUnlessItSaysItLoops()
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import("""{ "$guid": "6f1d0c3e-2b7a-4c51-9e0f-3a8d5b2c1e02", "Points": [ [1, 0], [-1, 0] ] }""");
        await Assert.That(result.Error).IsNull();
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, ChainImporter.Kind, ChainImporter.KindVersion);
        await Assert.That(MemoryMarshal.Read<ChainInfo>(file.Read(file.Find(Tag.Of("INFO")))).Loop).IsEqualTo(0u);
    }

    [Test]
    [Arguments("""{ "Loop": false }""", "a chain needs Points")]
    [Arguments("""{ "Points": [ [0, 0] ] }""", "a chain needs at least 2 points, not 1")]
    [Arguments("""{ "Points": [ [0, 0], [1, 0] ], "Loop": true }""", "a loop needs at least 3 points, not 2")]
    [Arguments("""{ "Points": [ [0, 0], [1] ] }""", "point 2 isn't [x, y]")]
    [Arguments("""{ "Points": [ [0, 0], [1, 0], [1, 0.001] ] }""", "points 2 and 3 are too close together")]
    [Arguments("""{ "Points": [ [0, 0], [1, 0], [0.001, 0] ], "Loop": true }""", "points 3 and 1 are too close together")]
    [Arguments("""{ "Points": [ [0, 0], [1, 0] ], "Loop": "yes" }""", "Loop is true or false")]
    public async Task AMistakenChainFailsTheImportSayingWhere(string chain, string message)
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import(chain.Insert(1, """ "$guid": "6f1d0c3e-2b7a-4c51-9e0f-3a8d5b2c1e03", """));
        await Assert.That(result.Error!).Contains(message);
    }
}
