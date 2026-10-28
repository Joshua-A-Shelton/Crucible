using System.Text;
using Crucible.Assets;
using Crucible.Binary;

namespace Crucible.Tests.Assets;

// The asset database (ADR 0040) on throwaway projects in the temp folder.
public class AssetDatabaseTests
{
    // A fresh project per test, deleted afterwards.
    private sealed class Scratch : IDisposable
    {
        private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Scratch()
        {
            Project = Project.Create(Path.Combine(Path.GetTempPath(), "crucible-assets-" + Guid.NewGuid().ToString("N")), "Game");
        }

        public Project Project { get; }

        public TextImporter Text { get; } = new();

        public ListImporter List { get; } = new();

        public AssetDatabase Open() => AssetDatabase.Open(Project, [Text, List, new BrokenImporter()]);

        public string PathOf(string assetPath) => Path.Combine(Project.AssetsFolder, assetPath);

        // Writes a file and moves its modification time forward, so a scan
        // sees the change whatever the file system's time resolution.
        public void Write(string assetPath, string text)
        {
            string path = PathOf(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            Touch(assetPath);
        }

        public void Touch(string assetPath)
        {
            _clock = _clock.AddMinutes(1);
            File.SetLastWriteTimeUtc(PathOf(assetPath), _clock);
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static string Output(AssetDatabase database, AssetId id)
    {
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(id)!, TextImporter.Kind, 1);
        return Encoding.UTF8.GetString(file.Read(file.Find(TextImporter.Body)));
    }

    private static ImportOutcome Outcome(AssetDatabase database, string path) =>
        database.ImportAll().Single(result => result.Path == path).Outcome;

    [Test]
    public async Task ProjectsHaveTheirFoldersAndIgnoreTheCache()
    {
        using Scratch scratch = new();
        Project opened = Project.Open(scratch.Project.ProjectFilePath);
        await Assert.That(opened.Name).IsEqualTo("Game");
        foreach (string folder in (string[])[opened.AssetsFolder, opened.CodeFolder, opened.PluginsFolder, opened.CacheFolder])
        {
            await Assert.That(Directory.Exists(folder)).IsTrue();
        }
        await Assert.That(File.ReadAllText(Path.Combine(opened.Root, ".gitignore"))).Contains("/Cache/");
    }

    [Test]
    public async Task EveryFileGetsASidecarAndKeepsItsGuid()
    {
        using Scratch scratch = new();
        scratch.Write("hello.txt", "hi");
        scratch.Write("Art/logo.png", "not really a png");
        AssetDatabase first = scratch.Open();
        await Assert.That(first.Warnings).IsEmpty();
        await Assert.That(string.Join(" ", first.Assets.Select(asset => asset.Path))).IsEqualTo("Art/logo.png hello.txt");
        await Assert.That(first.Find("hello.txt")!.Importer).IsEqualTo("Text");
        await Assert.That(first.Find("Art/logo.png")!.Importer).IsEqualTo(""); // no importer for .png yet
        await Assert.That(File.Exists(scratch.PathOf("hello.txt.meta"))).IsTrue();
        string sidecar = File.ReadAllText(scratch.PathOf("hello.txt.meta"));
        await Assert.That(sidecar).Contains("\"Importer\": \"Text\"");
        await Assert.That(sidecar).Contains("\"Upper\": true");
        await Assert.That(first.FilesHashed).IsEqualTo(2);

        AssetDatabase second = scratch.Open();
        await Assert.That(second.Find("hello.txt")!.Id).IsEqualTo(first.Find("hello.txt")!.Id);
        await Assert.That(second.FilesHashed).IsEqualTo(0); // the stamps matched
        await Assert.That(File.ReadAllText(scratch.PathOf("hello.txt.meta"))).IsEqualTo(sidecar); // untouched
    }

    [Test]
    public async Task MovesKeepTheGuidOnlyWithTheSidecar()
    {
        using Scratch scratch = new();
        scratch.Write("a.txt", "a");
        scratch.Write("b.txt", "b");
        AssetDatabase database = scratch.Open();
        AssetId a = database.Find("a.txt")!.Id;
        AssetId b = database.Find("b.txt")!.Id;

        Directory.CreateDirectory(scratch.PathOf("Moved"));
        File.Move(scratch.PathOf("a.txt"), scratch.PathOf("Moved/a.txt"));
        File.Move(scratch.PathOf("a.txt.meta"), scratch.PathOf("Moved/a.txt.meta"));
        File.Move(scratch.PathOf("b.txt"), scratch.PathOf("Moved/b.txt")); // sidecar left behind
        database.Refresh();

        await Assert.That(database.Find("Moved/a.txt")!.Id).IsEqualTo(a);
        await Assert.That(database.Find(a)!.Path).IsEqualTo("Moved/a.txt");
        await Assert.That(database.Find("Moved/b.txt")!.Id).IsNotEqualTo(b);
        await Assert.That(database.Warnings.Single().Path).IsEqualTo("b.txt.meta");
    }

    [Test]
    public async Task ACopyWithItsSidecarGetsANewGuid()
    {
        using Scratch scratch = new();
        scratch.Write("a.txt", "a");
        AssetId original = scratch.Open().Find("a.txt")!.Id;
        File.Copy(scratch.PathOf("a.txt"), scratch.PathOf("b.txt"));
        File.Copy(scratch.PathOf("a.txt.meta"), scratch.PathOf("b.txt.meta"));

        AssetDatabase database = scratch.Open();
        await Assert.That(database.Find("a.txt")!.Id).IsEqualTo(original); // first in path order keeps it
        await Assert.That(database.Find("b.txt")!.Id).IsNotEqualTo(original);
        await Assert.That(database.Warnings.Single().Message).Contains("same GUID as a.txt");
        // The new GUID was saved, so it stays.
        await Assert.That(scratch.Open().Find("b.txt")!.Id).IsEqualTo(database.Find("b.txt")!.Id);
    }

    [Test]
    public async Task AnAssetImportsOnceAndThenComesFromTheCache()
    {
        using Scratch scratch = new();
        scratch.Write("hello.txt", "hi");
        AssetDatabase database = scratch.Open();
        AssetId id = database.Find("hello.txt")!.Id;
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(Output(database, id)).IsEqualTo("HI");
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.UpToDate);

        // A new session finds the same result.
        AssetDatabase reopened = scratch.Open();
        await Assert.That(Outcome(reopened, "hello.txt")).IsEqualTo(ImportOutcome.UpToDate);
        await Assert.That(scratch.Text.Runs).IsEqualTo(1);
        await Assert.That(reopened.ImportedFile(id)).StartsWith(Path.Combine(scratch.Project.CacheFolder, "Imported"));
    }

    [Test]
    public async Task ChangesThatMatterReimportAndOthersDont()
    {
        using Scratch scratch = new();
        scratch.Write("hello.txt", "hi");
        AssetDatabase database = scratch.Open();
        AssetId id = database.Find("hello.txt")!.Id;
        database.ImportAll();

        // The source.
        scratch.Write("hello.txt", "ho");
        database.Refresh();
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(Output(database, id)).IsEqualTo("HO");

        // A newer time, same contents: rehashed, not reimported.
        scratch.Touch("hello.txt");
        database.Refresh();
        await Assert.That(database.FilesHashed).IsEqualTo(1);
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.UpToDate);

        // Its settings.
        string sidecar = scratch.PathOf("hello.txt.meta");
        File.WriteAllText(sidecar, File.ReadAllText(sidecar).Replace("\"Upper\": true", "\"Upper\": false").Replace("\"Repeat\": 1", "\"Repeat\": 2"));
        database.Refresh();
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(Output(database, id)).IsEqualTo("hoho");

        // Reformatting the sidecar changes nothing that matters.
        File.WriteAllText(sidecar, File.ReadAllText(sidecar).Replace("\n", "\n\n").Replace("  ", "    "));
        database.Refresh();
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.UpToDate);

        // The importer's version.
        scratch.Text.ImporterVersion = 2;
        await Assert.That(Outcome(database, "hello.txt")).IsEqualTo(ImportOutcome.Imported);

        // Losing the whole cache.
        Directory.Delete(scratch.Project.CacheFolder, recursive: true);
        Directory.CreateDirectory(scratch.Project.CacheFolder);
        await Assert.That(Outcome(scratch.Open(), "hello.txt")).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(scratch.Text.Runs).IsEqualTo(5);
    }

    [Test]
    public async Task ChangingADependencyReimportsWhatReadIt()
    {
        using Scratch scratch = new();
        scratch.Write("parts/one.txt", "1");
        scratch.Write("parts/two.txt", "2");
        scratch.Write("all.list", "parts/one.txt\nparts/two.txt\n");
        AssetDatabase database = scratch.Open();
        AssetId list = database.Find("all.list")!.Id;
        database.ImportAll();
        await Assert.That(Output(database, list)).IsEqualTo("1+2");

        scratch.Write("parts/two.txt", "22");
        database.Refresh();
        IReadOnlyList<ImportResult> results = database.ImportAll();
        await Assert.That(results.Single(result => result.Path == "all.list").Outcome).IsEqualTo(ImportOutcome.Imported);
        await Assert.That(results.Single(result => result.Path == "parts/one.txt").Outcome).IsEqualTo(ImportOutcome.UpToDate);
        await Assert.That(Output(database, list)).IsEqualTo("1+22");
        await Assert.That(scratch.List.Runs).IsEqualTo(2);

        // A dependency that disappears makes the list stale, and its import fails.
        File.Delete(scratch.PathOf("parts/one.txt"));
        File.Delete(scratch.PathOf("parts/one.txt.meta"));
        database.Refresh();
        await Assert.That(database.ImportedFile(list)).IsNull();
        ImportResult failed = database.ImportAll().Single(result => result.Path == "all.list");
        await Assert.That(failed.Outcome).IsEqualTo(ImportOutcome.Failed);
    }

    [Test]
    public async Task FailuresAndUnknownImportersAreReportedNotFatal()
    {
        using Scratch scratch = new();
        scratch.Write("ok.txt", "fine");
        scratch.Write("broken.bad", "?");
        scratch.Write("other.dat", "?");
        File.WriteAllText(scratch.PathOf("other.dat.meta"), """{ "Guid": "1b2c3d4e-0000-4000-8000-000000000001", "Importer": "Gone" }""");
        AssetDatabase database = scratch.Open();
        await Assert.That(database.Warnings.Single().Message).Contains("Gone");

        IReadOnlyList<ImportResult> results = database.ImportAll();
        ImportResult broken = results.Single(result => result.Path == "broken.bad");
        await Assert.That(broken.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(broken.Error).IsEqualTo("this file is beyond repair");
        await Assert.That(results.Single(result => result.Path == "other.dat").Outcome).IsEqualTo(ImportOutcome.NoImporter);
        await Assert.That(results.Single(result => result.Path == "ok.txt").Outcome).IsEqualTo(ImportOutcome.Imported);
    }

    [Test]
    public async Task UnreadableSidecarsAreLeftForTheirAuthor()
    {
        using Scratch scratch = new();
        scratch.Write("hello.txt", "hi");
        const string broken = "{ \"Guid\": \"not a guid\" ";
        File.WriteAllText(scratch.PathOf("hello.txt.meta"), broken);
        AssetDatabase database = scratch.Open();
        await Assert.That(database.Find("hello.txt")).IsNull();
        await Assert.That(database.Warnings.Single().Path).IsEqualTo("hello.txt.meta");
        await Assert.That(File.ReadAllText(scratch.PathOf("hello.txt.meta"))).IsEqualTo(broken);
    }

    [Test]
    public async Task AFileTakesAnImporterThatArrivesLater()
    {
        using Scratch scratch = new();
        scratch.Write("notes.txt", "n");
        AssetId id = AssetDatabase.Open(scratch.Project, []).Find("notes.txt")!.Id; // no importers yet
        await Assert.That(File.ReadAllText(scratch.PathOf("notes.txt.meta"))).Contains("\"Importer\": \"\"");

        AssetEntry adopted = scratch.Open().Find("notes.txt")!;
        await Assert.That(adopted.Id).IsEqualTo(id);
        await Assert.That(adopted.Importer).IsEqualTo("Text");
    }
}
