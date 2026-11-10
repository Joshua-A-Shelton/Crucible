using System.Text;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Scenes;

namespace Crucible.Tests.Assets;

// Sub-assets (ADR 0078): outputs of an import that are assets of their own,
// with IDs the parent's sidecar keeps by name.
public class SubAssetTests
{
    private static Project NewProject() =>
        Project.Create(Path.Combine(Path.GetTempPath(), "crucible-subassets-" + Guid.NewGuid().ToString("N")), "Game");

    private static string Text(string path)
    {
        using ImportedFile file = ImportedFile.Open(path, TextImporter.Kind, 1);
        return Encoding.UTF8.GetString(file.Read(file.Find(TextImporter.Body)));
    }

    private static void Write(Project project, string text, int minute)
    {
        string path = Path.Combine(project.AssetsFolder, "body.parts");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task SubAssetsKeepTheirIdsByName()
    {
        Project project = NewProject();
        try
        {
            Write(project, "arm=left\nleg=right", 1);
            AssetDatabase database = AssetDatabase.Open(project, [new PartsImporter()]);
            AssetEntry body = database.Find("body.parts")!;
            await Assert.That(database.SubAssetsOf(body.Id)).IsEmpty(); // not imported yet
            database.ImportAll();
            AssetEntry[] parts = [.. database.SubAssetsOf(body.Id)];
            await Assert.That(string.Join(" ", parts.Select(part => part.SubAssetName))).IsEqualTo("arm leg");
            await Assert.That(parts.All(part => part.Parent == body.Id && part.Path == "body.parts")).IsTrue();
            // Not listed among the assets, but found by ID, with their own files.
            await Assert.That(database.Assets.Count()).IsEqualTo(1);
            await Assert.That(Text(database.ImportedFile(parts[0].Id)!)).IsEqualTo("left");
            await Assert.That(Text(database.ImportedFile(parts[1].Id)!)).IsEqualTo("right");
            string sidecar = File.ReadAllText(Path.Combine(project.AssetsFolder, "body.parts.crmeta"));
            await Assert.That(sidecar).Contains("\"SubAssets\"");
            await Assert.That(sidecar).Contains(parts[0].Id.ToString());

            // Reopened, they're known from the sidecar, and up to date.
            AssetDatabase reopened = AssetDatabase.Open(project, [new PartsImporter()]);
            await Assert.That(reopened.Find(parts[1].Id)!.SubAssetName).IsEqualTo("leg");
            await Assert.That(reopened.ImportedFile(parts[1].Id)).IsEqualTo(database.ImportedFile(parts[1].Id));

            // Changed: a kept name keeps its ID, a new one gets one, and a
            // dropped one is forgotten.
            Write(project, "leg=both\nhead=round", 2);
            reopened.Refresh();
            await Assert.That(reopened.ImportedFile(parts[1].Id)).IsNull(); // its parent changed
            reopened.ImportAll();
            AssetEntry[] after = [.. reopened.SubAssetsOf(body.Id)];
            await Assert.That(string.Join(" ", after.Select(part => part.SubAssetName))).IsEqualTo("head leg");
            await Assert.That(after[1].Id).IsEqualTo(parts[1].Id);
            await Assert.That(Text(reopened.ImportedFile(parts[1].Id)!)).IsEqualTo("both");
            await Assert.That(reopened.Find(parts[0].Id)).IsNull();
            await Assert.That(File.ReadAllText(Path.Combine(project.AssetsFolder, "body.parts.crmeta")))
                .DoesNotContain(parts[0].Id.ToString());
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task ImportingASubAssetImportsItsParent()
    {
        Project project = NewProject();
        try
        {
            Write(project, "arm=left", 1);
            AssetDatabase database = AssetDatabase.Open(project, [new PartsImporter()]);
            database.ImportAll();
            AssetId arm = database.SubAssetsOf(database.Find("body.parts")!.Id).Single().Id;
            Directory.Delete(Path.Combine(project.CacheFolder, "Imported"), recursive: true);
            await Assert.That(database.ImportedFile(arm)).IsNull();
            ImportResult result = database.Import(arm);
            await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
            await Assert.That(Text(result.OutputPath!)).IsEqualTo("left");
            await Assert.That(database.Import(arm).Outcome).IsEqualTo(ImportOutcome.UpToDate);

            // A game reads them through the cache, by ID, like any import.
            AssetCache cache = AssetCache.Open(project.ProjectFilePath);
            await Assert.That(Text(cache.FileOf(arm)!)).IsEqualTo("left");
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task CopiedSidecarsGetNewSubAssetIds()
    {
        Project project = NewProject();
        try
        {
            Write(project, "arm=left", 1);
            AssetDatabase database = AssetDatabase.Open(project, [new PartsImporter()]);
            database.ImportAll();
            AssetId arm = database.SubAssetsOf(database.Find("body.parts")!.Id).Single().Id;
            File.Copy(Path.Combine(project.AssetsFolder, "body.parts"), Path.Combine(project.AssetsFolder, "copy.parts"));
            File.Copy(Path.Combine(project.AssetsFolder, "body.parts.crmeta"),
                Path.Combine(project.AssetsFolder, "copy.parts.crmeta"));
            database.Refresh();
            AssetEntry copy = database.SubAssetsOf(database.Find("copy.parts")!.Id).Single();
            await Assert.That(copy.Id).IsNotEqualTo(arm);
            await Assert.That(database.Find(arm)!.Parent).IsEqualTo(database.Find("body.parts")!.Id);
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task ModelClipsAreSubAssetsAndJointsArentEntities()
    {
        string? samples = Environment.GetEnvironmentVariable("CRUCIBLE_UFBX_DATA")
            ?? throw new InvalidOperationException("CRUCIBLE_UFBX_DATA isn't set: run these tests through CTest");
        Project project = NewProject();
        try
        {
            File.Copy(Path.Combine(samples, "max7_skin_5000_binary.fbx"), Path.Combine(project.AssetsFolder, "skin.fbx"));
            AssetDatabase database = AssetDatabase.Open(project, [new FbxImporter()]);
            ImportResult result = database.ImportAll().Single();
            await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported);
            AssetEntry[] clips = [.. database.SubAssetsOf(result.Id)];
            await Assert.That(clips.Length).IsEqualTo(2);
            foreach (AssetEntry clip in clips)
            {
                string? path = database.ImportedFile(clip.Id);
                await Assert.That(path).EndsWith(MeshImporter.ClipExtension);
                using ImportedFile file = ImportedFile.Open(path!, MeshImporter.ClipKind, MeshImporter.ClipVersion);
                await Assert.That(file.Find(Tag.Of("TRCK"))).IsGreaterThanOrEqualTo(0);
            }

            // The model's prefab has its meshes but not its skeleton.
            using World world = new();
            Entity model = Prefab.Instantiate(world, result.Id, database.Prefabs());
            List<string> names = [];
            Collect(world, model, names);
            int nodes, joints;
            using (ImportedFile mesh = ImportedFile.Open(database.ImportedFile(result.Id)!, MeshImporter.Kind,
                       MeshImporter.KindVersion))
            {
                nodes = (int)mesh.Section(mesh.Find(Tag.Of("NODE"))).Size / 56; // MeshNode
                joints = (int)mesh.Section(mesh.Find(Tag.Of("JNTS"))).Size / 80; // JointEntry
            }
            await Assert.That(joints).IsGreaterThan(0);
            await Assert.That(names.Count).IsGreaterThan(0);
            await Assert.That(names.Count).IsLessThanOrEqualTo(nodes - joints);
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task TheWalkerImportsWithTheClipIdsItsSidecarKeeps()
    {
        string? walker = Environment.GetEnvironmentVariable("CRUCIBLE_WALKER_PROJECT")
            ?? throw new InvalidOperationException("CRUCIBLE_WALKER_PROJECT isn't set: run these tests through CTest");
        Project project = NewProject();
        try
        {
            string models = Path.Combine(project.AssetsFolder, "Models");
            Directory.CreateDirectory(models);
            foreach (string file in Directory.GetFiles(Path.Combine(walker, "Assets", "Models")))
            {
                File.Copy(file, Path.Combine(models, Path.GetFileName(file)));
            }
            string sidecar = Path.Combine(models, "Walker.gltf.crmeta");
            string before = File.ReadAllText(sidecar);
            AssetDatabase database = AssetDatabase.Open(project, [new MeshImporter()]);
            AssetEntry model = database.Find("Models/Walker.gltf")!;
            AssetEntry[] clips = [.. database.SubAssetsOf(model.Id)];
            database.ImportAll();
            await Assert.That(database.ImportedFile(model.Id)).IsNotNull();
            await Assert.That(string.Join(" ", database.SubAssetsOf(model.Id).Select(clip => clip.SubAssetName)))
                .IsEqualTo("Idle Walk");
            await Assert.That(string.Join(" ", database.SubAssetsOf(model.Id).Select(clip => clip.Id)))
                .IsEqualTo(string.Join(" ", clips.Select(clip => clip.Id)));
            await Assert.That(clips.Length).IsEqualTo(2);
            await Assert.That(File.ReadAllText(sidecar)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    private static void Collect(World world, Entity entity, List<string> names)
    {
        Entity[] children = new Entity[64];
        int count = world.ChildrenOf(entity, children);
        for (int i = 0; i < count; ++i)
        {
            if (world.Has(children[i], Components.Of<EntityName>()))
            {
                names.Add(world.GetManaged<EntityName>(children[i]).Value);
            }
            Collect(world, children[i], names);
        }
    }
}
