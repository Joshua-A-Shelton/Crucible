using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// The Font importer (ADR 0057): a font file kept whole in its .crfont, and
// files that aren't single fonts refused.
public class FontImporterTests
{
    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-fonts-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new FontImporter()]);

        public string PathOf(string assetPath)
        {
            string path = Path.Combine(Project.AssetsFolder, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static string NotoSans =>
        Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf");

    [Test]
    public async Task AFontImportsWhole()
    {
        using Scratch scratch = new();
        File.Copy(NotoSans, scratch.PathOf("Fonts/NotoSans-Regular.ttf"));
        ImportResult result = scratch.Open().ImportAll().Single();
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.OutputPath!).EndsWith(".crfont");
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, FontImporter.Kind, FontImporter.KindVersion);
        byte[] data = file.Read(file.Find(FontImporter.DataSection));
        await Assert.That(data.AsSpan().SequenceEqual(File.ReadAllBytes(NotoSans))).IsTrue();
    }

    [Test]
    public async Task CollectionsAndOtherFilesAreRefused()
    {
        using Scratch scratch = new();
        File.WriteAllBytes(scratch.PathOf("Fonts/Collection.ttf"), [(byte)'t', (byte)'t', (byte)'c', (byte)'f', 0, 2, 0, 0, 0, 0, 0, 2]);
        File.WriteAllText(scratch.PathOf("Fonts/Text.otf"), "not a font at all");
        Dictionary<string, ImportResult> results = scratch.Open().ImportAll().ToDictionary(r => r.Path);
        await Assert.That(results["Fonts/Collection.ttf"].Error!).Contains("collection");
        await Assert.That(results["Fonts/Text.otf"].Error!).Contains("Not a TrueType or OpenType font");
    }
}
