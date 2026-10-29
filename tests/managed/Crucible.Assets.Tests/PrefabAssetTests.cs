using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Scenes;

namespace Crucible.Tests.Assets;

// Prefabs and models through the asset database (ADR 0045).
public class PrefabAssetTests
{
    [Test]
    public async Task PrefabsAndModelsInstantiateThroughTheDatabase()
    {
        string? samples = Environment.GetEnvironmentVariable("CRUCIBLE_UFBX_DATA")
            ?? throw new InvalidOperationException("CRUCIBLE_UFBX_DATA isn't set: run these tests through CTest");
        Project project = Project.Create(Path.Combine(Path.GetTempPath(), "crucible-prefabs-" + Guid.NewGuid().ToString("N")), "Game");
        try
        {
            File.Copy(Path.Combine(samples, "max_geometry_transform_7700_binary.fbx"), Path.Combine(project.AssetsFolder, "boxes.fbx"));
            AssetId crateId = AssetId.New();
            using (World world = new())
            {
                Entity crate = world.CreateEntity();
                world.Set(crate, LocalTransform.Identity);
                File.WriteAllBytes(Path.Combine(project.AssetsFolder, "crate.crprefab"), Prefab.Save(world, crate, crateId));
            }

            AssetDatabase database = AssetDatabase.Open(project, [new FbxImporter()]);
            await Assert.That(database.Find("crate.crprefab")!.Id).IsEqualTo(crateId);
            await Assert.That(File.Exists(Path.Combine(project.AssetsFolder, "crate.crprefab.crmeta"))).IsFalse();
            AssetId boxesId = database.Find("boxes.fbx")!.Id;

            // The model is imported on first use.
            PrefabLibrary library = database.Prefabs();
            using World scene = new();
            Entity boxes = Prefab.Instantiate(scene, boxesId, library);
            Entity crateInstance = Prefab.Instantiate(scene, crateId, library);
            scene.Get<LocalTransform>(crateInstance).Position.X = 3;
            Entity[] children = new Entity[4];
            int count = scene.ChildrenOf(boxes, children);
            await Assert.That(count).IsEqualTo(1); // Box001, with Box002 under it
            await Assert.That(scene.Read<MeshInstance>(children[0]).Model).IsEqualTo(boxesId);

            byte[] saved = Scene.Save(scene, AssetId.New(), library);
            using World loaded = new();
            SceneLoad load = Scene.Load(loaded, saved, prefabs: database.Prefabs());
            await Assert.That(load.Roots.Count).IsEqualTo(2);
            await Assert.That(loaded.EntityCount).IsEqualTo(scene.EntityCount);
            await Assert.That(Convert.ToHexString(Scene.Save(loaded, load.Id, library))).IsEqualTo(Convert.ToHexString(saved));
        }
        finally
        {
            Directory.Delete(project.Root, recursive: true);
        }
    }
}
