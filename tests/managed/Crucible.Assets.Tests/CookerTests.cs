using System.Buffers.Binary;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// Cooking (ADR 0098): from the build settings' scenes and include list, a
// walk to everything they need, packed into one archive.
public class CookerTests
{
    // A copy of the Walker's project, without its cache or builds.
    private static Project CopyWalker()
    {
        string walker = Environment.GetEnvironmentVariable("CRUCIBLE_WALKER_PROJECT")
            ?? throw new InvalidOperationException("CRUCIBLE_WALKER_PROJECT isn't set: run these tests through CTest");
        string root = Path.Combine(Path.GetTempPath(), "crucible-cook-" + Guid.NewGuid().ToString("N"));
        foreach (string file in Directory.GetFiles(walker, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(walker, file);
            if (relative.StartsWith("Cache", StringComparison.Ordinal) || relative.StartsWith("Build", StringComparison.Ordinal))
            {
                continue;
            }
            string target = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return Project.Open(Path.Combine(root, "Walker.crproject"));
    }

    private static AssetDatabase Open(Project project) => AssetDatabase.Open(project,
        [new TextureImporter(), new MeshImporter(), new StateMachineImporter(), new SoundImporter(), new ChainImporter()]);

    private static AssetId Id(AssetDatabase database, string path) =>
        database.Find(path)?.Id ?? throw new InvalidOperationException($"no {path}");

    private static AssetId Sub(AssetDatabase database, string path, string name) =>
        database.SubAssetsOf(Id(database, path)).Single(sub => sub.SubAssetName == name).Id;

    [Test]
    public async Task TheWalkersLevelCooksToJustWhatItNeeds()
    {
        Project project = CopyWalker();
        try
        {
            AssetDatabase database = Open(project);
            AssetId level = Id(database, "Scenes/Level.crscene");
            await Assert.That(Sorted(project.Build.Scenes)).IsEqualTo(Sorted([level]));
            CookResult result = new Cooker(database).Cook();
            await Assert.That(string.Join("; ", result.Errors)).IsEqualTo("");
            await Assert.That(result.ArchivePath).IsEqualTo(Path.Combine(project.PackageDataFolder, "Game.crpak"));

            // The scene, the level and the Walker (models), the scene's
            // state machine and the clips it plays, and the footstep sound:
            // nothing else (the other scenes, the Walk clip).
            byte[] archive = File.ReadAllBytes(result.ArchivePath);
            var index = ArchiveWriter.ReadIndex(archive);
            AssetId[] expected =
            [
                level, Id(database, "Models/Level.gltf"), Id(database, "Models/Walker.gltf"),
                Id(database, "Models/Level.crstates"), Sub(database, "Models/Walker.gltf", "Idle"),
                Sub(database, "Models/Walker.gltf", "Swing"), Sub(database, "Models/Walker.gltf", "WalkForward"),
                Id(database, "Sounds/Footstep.wav"),
            ];
            await Assert.That(Sorted(index.Select(entry => entry.Id))).IsEqualTo(Sorted(expected));
            await Assert.That(result.AssetCount).IsEqualTo(expected.Length);
            await Assert.That(ArchiveWriter.ReadStartScene(archive)).IsEqualTo(level);

            // Sorted by the IDs' bytes, each entry on a 4 KiB boundary, apart.
            long indexOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(16));
            for (int i = 0; i < index.Count; ++i)
            {
                await Assert.That(index[i].Offset % ArchiveWriter.Alignment).IsEqualTo(0);
                long end = i + 1 < index.Count ? index[i + 1].Offset : indexOffset;
                await Assert.That(index[i].Offset + index[i].Size).IsLessThanOrEqualTo(end);
                if (i > 0)
                {
                    await Assert.That(Bytes(index[i - 1].Id).AsSpan().SequenceCompareTo(Bytes(index[i].Id))).IsLessThan(0);
                }
            }

            // The scene as it is; an imported file with the same sections as
            // the cache's, compressed harder.
            var scene = index.Single(entry => entry.Id == level);
            await Assert.That(scene.Extension).IsEqualTo(".crscene");
            await Assert.That(archive.AsSpan((int)scene.Offset, (int)scene.Size).SequenceEqual(
                File.ReadAllBytes(Path.Combine(project.AssetsFolder, "Scenes", "Level.crscene")))).IsTrue();
            AssetId model = Id(database, "Models/Level.gltf");
            var mesh = index.Single(entry => entry.Id == model);
            await Assert.That(mesh.Extension).IsEqualTo(".crmesh");
            using ImportedFile cooked = ImportedFile.Parse(archive.AsSpan((int)mesh.Offset, (int)mesh.Size),
                MeshImporter.Kind, MeshImporter.KindVersion);
            using ImportedFile cached = ImportedFile.Open(database.ImportedFile(model)!, MeshImporter.Kind, MeshImporter.KindVersion);
            await Assert.That(cooked.SectionCount).IsEqualTo(cached.SectionCount);
            for (int i = 0; i < cached.SectionCount; ++i)
            {
                await Assert.That(cooked.Read(i).AsSpan().SequenceEqual(cached.Read(i))).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task StateMachinesRecordTheClipsTheyPlay()
    {
        Project project = CopyWalker();
        try
        {
            AssetDatabase database = Open(project);
            AssetId machine = Id(database, "Models/Level.crstates");
            database.Import(machine);
            await Assert.That(Sorted(database.ReferencesOf(machine))).IsEqualTo(Sorted(
            [
                Sub(database, "Models/Walker.gltf", "Idle"), Sub(database, "Models/Walker.gltf", "Swing"),
                Sub(database, "Models/Walker.gltf", "WalkForward"),
            ]));
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task IncludedFoldersArePackedWhetherOrNotAnythingNamesThem()
    {
        Project project = CopyWalker();
        try
        {
            project.SaveBuild(new BuildSettings { Scenes = [.. project.Build.Scenes], Include = ["Chains", "Scenes/Walk.crscene"] });
            AssetDatabase database = Open(Project.Open(project.ProjectFilePath));
            await Assert.That(string.Join(",", database.Project.Build.Include)).IsEqualTo("Chains,Scenes/Walk.crscene");
            CookResult result = new Cooker(database).Cook();
            await Assert.That(string.Join("; ", result.Errors)).IsEqualTo("");
            HashSet<AssetId> packed = [.. ArchiveWriter.ReadIndex(File.ReadAllBytes(result.ArchivePath)).Select(entry => entry.Id)];
            await Assert.That(packed.Contains(Id(database, "Chains/Hills.crchain"))).IsTrue();
            // The Walk scene, and what it needs: its own state machine and the Walk clip.
            await Assert.That(packed.Contains(Id(database, "Scenes/Walk.crscene"))).IsTrue();
            await Assert.That(packed.Contains(Id(database, "Models/Walker.crstates"))).IsTrue();
            await Assert.That(packed.Contains(Sub(database, "Models/Walker.gltf", "Walk"))).IsTrue();
            await Assert.That(packed.Contains(Id(database, "Scenes/Hitboxes.crscene"))).IsFalse();
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task ACookWithProblemsSaysWhatAndWritesNothing()
    {
        Project project = CopyWalker();
        try
        {
            AssetId missing = AssetId.New();
            project.SaveBuild(new BuildSettings { Scenes = [missing], Include = ["Nowhere"] });
            AssetDatabase database = Open(Project.Open(project.ProjectFilePath));
            CookResult result = new Cooker(database).Cook();
            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Errors.Any(error => error.Contains(missing.ToString(), StringComparison.Ordinal))).IsTrue();
            await Assert.That(result.Errors.Any(error => error.Contains("Nowhere", StringComparison.Ordinal))).IsTrue();
            await Assert.That(File.Exists(result.ArchivePath)).IsFalse();

            project.SaveBuild(new BuildSettings());
            CookResult none = new Cooker(Open(Project.Open(project.ProjectFilePath))).Cook();
            await Assert.That(string.Join("; ", none.Errors)).IsEqualTo("the build settings name no scenes");
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task TheArchiveReaderFindsWhatTheCookerPacked()
    {
        Project project = CopyWalker();
        try
        {
            AssetDatabase database = Open(project);
            CookResult result = new Cooker(database).Cook();
            await Assert.That(string.Join("; ", result.Errors)).IsEqualTo("");
            AssetId level = Id(database, "Scenes/Level.crscene");
            using Archive archive = Archive.Open(result.ArchivePath);
            await Assert.That(archive.Count).IsEqualTo(8);
            await Assert.That(archive.StartScene).IsEqualTo(level);

            // The Level scene's list: everything else packed, as the writer recorded it.
            IReadOnlyList<AssetId> packed = [.. ArchiveWriter.ReadIndex(File.ReadAllBytes(result.ArchivePath)).Select(entry => entry.Id)];
            await Assert.That(Sorted(archive.SceneAssets(level))).IsEqualTo(Sorted(packed.Where(id => id != level)));
            await Assert.That(Sorted(ArchiveWriter.ReadSceneAssets(File.ReadAllBytes(result.ArchivePath))[level]))
                .IsEqualTo(Sorted(archive.SceneAssets(level)));
            await Assert.That(archive.SceneAssets(Id(database, "Models/Level.gltf")).Length).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task EachScenesListHasOnlyWhatItReaches()
    {
        Project project = CopyWalker();
        try
        {
            project.SaveBuild(new BuildSettings { Scenes = [.. project.Build.Scenes], Include = ["Scenes/Walk.crscene"] });
            AssetDatabase database = Open(Project.Open(project.ProjectFilePath));
            CookResult result = new Cooker(database).Cook();
            await Assert.That(string.Join("; ", result.Errors)).IsEqualTo("");
            using Archive archive = Archive.Open(result.ArchivePath);
            HashSet<AssetId> level = [.. archive.SceneAssets(Id(database, "Scenes/Level.crscene"))];
            HashSet<AssetId> walk = [.. archive.SceneAssets(Id(database, "Scenes/Walk.crscene"))];
            // Both use the Walker; each its own state machine and clips.
            await Assert.That(level.Contains(Id(database, "Models/Walker.gltf")) && walk.Contains(Id(database, "Models/Walker.gltf"))).IsTrue();
            await Assert.That(level.Contains(Id(database, "Models/Level.crstates"))).IsTrue();
            await Assert.That(walk.Contains(Id(database, "Models/Level.crstates"))).IsFalse();
            await Assert.That(walk.Contains(Sub(database, "Models/Walker.gltf", "Walk"))).IsTrue();
            await Assert.That(level.Contains(Sub(database, "Models/Walker.gltf", "Walk"))).IsFalse();
            await Assert.That(level.Contains(Id(database, "Scenes/Walk.crscene")) || walk.Contains(Id(database, "Scenes/Level.crscene"))).IsFalse();
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    [Test]
    public async Task ADamagedOrOldArchiveDoesntOpen()
    {
        Project project = CopyWalker();
        try
        {
            CookResult result = new Cooker(Open(project)).Cook();
            byte[] good = File.ReadAllBytes(result.ArchivePath);
            string path = Path.Combine(project.Root, "Damaged.crpak");
            async Task Fails(byte[] bytes, Status expected)
            {
                File.WriteAllBytes(path, bytes);
                Status status = Status.Ok;
                try
                {
                    Archive.Open(path).Dispose();
                }
                catch (CrucibleException exception)
                {
                    status = exception.Status;
                }
                await Assert.That(status).IsEqualTo(expected);
            }
            await Fails(good[..^20], Status.InvalidData);
            byte[] old = (byte[])good.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(old.AsSpan(4), 1);
            await Fails(old, Status.Unsupported);
            byte[] other = (byte[])good.Clone();
            other[0] = (byte)'X';
            await Fails(other, Status.InvalidData);
            Status missing = Status.Ok;
            try
            {
                Archive.Open(Path.Combine(project.Root, "Nowhere.crpak"));
            }
            catch (CrucibleException exception)
            {
                missing = exception.Status;
            }
            await Assert.That(missing).IsEqualTo(Status.NotFound);
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }

    // IDs as text, sorted, so two sets compare as strings.
    internal static string Sorted(IEnumerable<AssetId> ids) =>
        string.Join(",", ids.Select(id => id.ToString()).Order(StringComparer.Ordinal));

    private static byte[] Bytes(AssetId id)
    {
        byte[] bytes = new byte[16];
        id.Value.TryWriteBytes(bytes);
        return bytes;
    }
}
