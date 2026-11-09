using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Hosting;
using Crucible.Scenes;

namespace Crucible.Tests;

// A project run as a game (ADR 0073): AssetCache reads what the editor's
// asset database left in Cache (imported files by key, prefab files by
// sidecar), and ProjectStartup loads the scene from the game arguments.
public class ProjectStartupTests
{
    private static readonly AssetId _texture = AssetId.Parse("1a2b3c4d-0000-4000-8000-000000000001");
    private static readonly AssetId _prefab = AssetId.Parse("1a2b3c4d-0000-4000-8000-000000000002");
    private static readonly AssetId _stale = AssetId.Parse("1a2b3c4d-0000-4000-8000-000000000003");

    [Before(Class)]
    public static void Register() => new Crucible.Generated.ComponentRegistration().Register();

    // A project folder with one imported texture, one prefab file, and an
    // import record whose file is gone.
    private static string MakeProject()
    {
        string root = Path.Combine(Path.GetTempPath(), $"crucible-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Cache", "Imported", "ab"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Prefabs"));
        File.WriteAllText(Path.Combine(root, "Game.crproject"), """{ "Name": "Game" }""");
        File.WriteAllText(Path.Combine(root, "Cache", "imports.json"), $$"""
            {
              "Assets": {
                "{{_texture}}": { "Key": "ab01", "Dependencies": {} },
                "{{_stale}}": { "Key": "cd02", "Dependencies": {} },
                "not an id": { "Key": "ab01" }
              }
            }
            """);
        File.WriteAllBytes(Path.Combine(root, "Cache", "Imported", "ab", "ab01.crtexture"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(root, "Assets", "Prefabs", "Door.crprefab"), [4, 5]);
        File.WriteAllText(Path.Combine(root, "Assets", "Prefabs", "Door.crprefab.crmeta"),
            $$"""{ "Guid": "{{_prefab}}", "Importer": "" }""");
        return root;
    }

    [Test]
    public async Task TheCacheReadsImportedFilesAndPrefabs()
    {
        string root = MakeProject();
        try
        {
            AssetCache cache = AssetCache.Open(Path.Combine(root, "Game.crproject"));
            await Assert.That(cache.Count).IsEqualTo(2);
            await Assert.That(Convert.ToHexString(cache.Read(_texture) ?? [])).IsEqualTo("010203");
            await Assert.That(Convert.ToHexString(cache.Read(_prefab) ?? [])).IsEqualTo("0405");
            await Assert.That(cache.Read(_stale)).IsNull(); // recorded, but its file is gone
            await Assert.That(cache.Read(AssetId.New())).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AProjectWithNothingImportedReadsNothing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"crucible-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            AssetCache cache = AssetCache.Open(Path.Combine(root, "Empty.crproject"));
            await Assert.That(cache.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TheSceneLoadsFromTheGameArguments()
    {
        string root = MakeProject();
        try
        {
            using World authored = new();
            Entity hero = authored.CreateEntity(Components.Of<SceneHealth>());
            authored.Set(hero, new SceneHealth { Value = 7 });
            authored.CreateEntity();
            string scene = Path.Combine(root, "Assets", "Start.crscene");
            File.WriteAllBytes(scene, Scene.Save(authored, AssetId.New()));

            using World world = new();
            await Assert.That(ProjectStartup.Open(world, new Dictionary<string, string>())).IsNull();
            await Assert.That(ProjectStartup.Open(world, new Dictionary<string, string> { ["project"] = "x" })).IsNull();
            AssetCache? opened = ProjectStartup.Open(world, new Dictionary<string, string>
            {
                ["project"] = Path.Combine(root, "Game.crproject"),
                ["scene"] = scene,
            });
            await Assert.That(opened).IsNotNull();
            await Assert.That(opened!.Count).IsEqualTo(2);
            await Assert.That(world.EntityCount).IsEqualTo(2);
            Entity loaded = world.Contents().Select(item => item.Entity).First(entity => world.Has(entity, Components.Of<SceneHealth>()));
            await Assert.That(world.Read<SceneHealth>(loaded).Value).IsEqualTo(7);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
