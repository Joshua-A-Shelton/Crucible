using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.UI;

namespace Crucible.Tests;

// The Style importer (ADR 0061): a .crstyle (JSON, its GUID inside) read
// as the runtime reads it, kept in a .crsty the runtime loads.
public class StyleImporterTests
{
    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-styles-" + Guid.NewGuid().ToString("N")), "Game");

        public AssetDatabase Open() => AssetDatabase.Open(Project, [new StyleImporter()]);

        public string Write(string assetPath, string text)
        {
            string path = Path.Combine(Project.AssetsFolder, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    [Test]
    public async Task AThemeImportsAndLoads()
    {
        using Scratch scratch = new();
        scratch.Write("UI/Menu.crstyle", """
            {
              "$guid": "6a1f0d2e-3c4b-4d5e-8f60-718293a4b5c6",
              "Styles": [ { "Selector": "UIElement.primary", "Set": { "Background": "#336699" } } ]
            }
            """);
        AssetDatabase database = scratch.Open();
        ImportResult result = database.ImportAll().Single();
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Id.ToString()).IsEqualTo("6a1f0d2e-3c4b-4d5e-8f60-718293a4b5c6");
        await Assert.That(result.OutputPath!).EndsWith(".crsty");
        Theme theme = Theme.Load(File.ReadAllBytes(result.OutputPath!));
        await Assert.That(theme.Styles.Single().Selector.Classes.Single()).IsEqualTo("primary");
    }

    [Test]
    public async Task AMistakeFailsTheImportSayingWhere()
    {
        using Scratch scratch = new();
        scratch.Write("UI/Broken.crstyle", """
            { "$guid": "6a1f0d2e-3c4b-4d5e-8f60-718293a4b5c7",
              "Styles": [ { "Selector": "UIElement", "Set": { "Opacity": "half" } } ] }
            """);
        ImportResult result = scratch.Open().ImportAll().Single();
        await Assert.That(result.Error!).Contains("Styles[0] (UIElement).Set.Opacity: expected a number");
    }
}
