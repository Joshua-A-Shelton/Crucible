using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Tests;

// Prefabs (ADR 0045), under JIT and NativeAOT.

[Component, Serialized]
public partial struct PrefabStats
{
    public int Health;
    public int Max;
}

public class PrefabTests
{
    private static readonly AssetId _sceneId = AssetId.Parse("0c4d1a3e-5f6b-4a7c-8d9e-0f1a2b3c4d5e");
    private static readonly AssetId _door = AssetId.Parse("d0000000-0000-4000-8000-000000000001");
    private static readonly AssetId _house = AssetId.Parse("40000000-0000-4000-8000-000000000002");
    private static readonly AssetId _model = AssetId.Parse("70000000-0000-4000-8000-000000000003");

    [Before(Class)]
    public static void Register() => new Crucible.Generated.ComponentRegistration().Register();

    private static string Text(byte[] json) => Encoding.UTF8.GetString(json);

    // Prefab files by ID, standing in for the asset database.
    private sealed class Files
    {
        public readonly Dictionary<AssetId, byte[]> Bytes = [];

        public PrefabLibrary Library() => new(id => Bytes.GetValueOrDefault(id));
    }

    private static Entity[] Children(World world, Entity entity)
    {
        Entity[] children = new Entity[16];
        return children[..world.ChildrenOf(entity, children)];
    }

    // A door: a frame (stats 10 of 10) with a handle that follows the frame.
    private static byte[] DoorPrefab(int health = 10, int max = 10)
    {
        using World world = new();
        Entity frame = world.CreateEntity();
        world.Set(frame, new PrefabStats { Health = health, Max = max });
        world.SetManaged(frame, new SceneLabel { Text = "door" });
        Entity handle = world.CreateEntity();
        world.Set(handle, new SceneFollow { Target = frame, Distance = 0.5f });
        world.Add(handle, Components.Of<SceneMarker>());
        world.SetParent(handle, frame);
        world.Set(frame, new SceneEntityId { Value = 0x0d00 });
        world.Set(handle, new SceneEntityId { Value = 0x0d01 });
        return Prefab.Save(world, frame, _door);
    }

    // A house: a root holding two door instances, the second with its
    // health overridden at the house's level.
    private static byte[] HousePrefab(Files files)
    {
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity house = world.CreateEntity();
        world.Set(house, new SceneEntityId { Value = 0x4000 });
        world.Set(house, new LocalTransform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        Entity front = Prefab.Instantiate(world, _door, library, house);
        Entity back = Prefab.Instantiate(world, _door, library, house);
        world.Get<PrefabStats>(back).Health = 4;
        return Prefab.Save(world, house, _house, library);
    }

    private static Files Sample()
    {
        Files files = new();
        files.Bytes[_door] = DoorPrefab();
        files.Bytes[_house] = HousePrefab(files);
        return files;
    }

    [Test]
    public async Task AnInstanceSavesOnlyTheFieldsItChanges()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity door = Prefab.Instantiate(world, _door, library);
        world.Get<PrefabStats>(door).Health = 3;
        string json = Text(Scene.Save(world, _sceneId, library));

        await Assert.That(json).Contains($"\"Prefab\": \"{_door.Value}\"");
        await Assert.That(json).Contains("\"Health\": 3");
        await Assert.That(json).DoesNotContain("\"Max\"");       // unchanged field
        await Assert.That(json).DoesNotContain("SceneLabel");    // unchanged component
        await Assert.That(json).DoesNotContain("\"Children\"");  // the handle comes from the prefab
        await Assert.That(json).DoesNotContain("AddedChildren");
    }

    [Test]
    public async Task PrefabEditsReachInstancesExceptOverriddenFields()
    {
        Files files = Sample();
        using World world = new();
        Entity door = Prefab.Instantiate(world, _door, files.Library());
        world.Get<PrefabStats>(door).Health = 3;
        byte[] scene = Scene.Save(world, _sceneId, files.Library());

        // The prefab changes both fields; the instance changed only Health.
        files.Bytes[_door] = DoorPrefab(health: 20, max: 20);
        using World loaded = new();
        SceneLoad load = Scene.Load(loaded, scene, prefabs: files.Library());
        PrefabStats stats = loaded.Read<PrefabStats>(load.Roots.Single());
        await Assert.That(stats.Health).IsEqualTo(3);
        await Assert.That(stats.Max).IsEqualTo(20);
    }

    [Test]
    public async Task SavingTheLoadedSceneGivesTheSameFile()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity house = Prefab.Instantiate(world, _house, library);
        Entity extra = world.CreateEntity();
        world.Set(extra, new SceneHealth { Value = 1 });
        world.SetParent(extra, house);
        Entity watcher = world.CreateEntity();
        world.Set(watcher, new SceneFollow { Target = Children(world, Children(world, house)[0])[0] });
        byte[] first = Scene.Save(world, _sceneId, library);

        using World loaded = new();
        Scene.Load(loaded, first, prefabs: library);
        await Assert.That(Text(Scene.Save(loaded, _sceneId, library))).IsEqualTo(Text(first));
    }

    [Test]
    public async Task NestedPrefabsLayerEachFilesOverrides()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity house = Prefab.Instantiate(world, _house, library);
        Entity[] doors = Children(world, house);
        await Assert.That(doors.Length).IsEqualTo(2);
        // The house overrides the back door; the scene overrides the front door.
        world.Get<PrefabStats>(doors[0]).Health = 7;
        byte[] scene = Scene.Save(world, _sceneId, library);
        string json = Text(scene);
        await Assert.That(json).Contains($"\"Prefab\": \"{_house.Value}\"");
        await Assert.That(json).DoesNotContain(_door.Value.ToString()); // the doors are the house's business

        using World loaded = new();
        Entity root = Scene.Load(loaded, scene, prefabs: library).Roots.Single();
        Entity[] loadedDoors = Children(loaded, root);
        await Assert.That(loaded.Read<PrefabStats>(loadedDoors[0]).Health).IsEqualTo(7);
        await Assert.That(loaded.Read<PrefabStats>(loadedDoors[1]).Health).IsEqualTo(4);
        await Assert.That(loaded.Read<PrefabStats>(loadedDoors[1]).Max).IsEqualTo(10);
        // Only the outermost instance is marked.
        await Assert.That(loaded.Read<PrefabInstance>(root).Prefab).IsEqualTo(_house);
        await Assert.That(loaded.Has(loadedDoors[0], Components.Of<PrefabInstance>())).IsFalse();
        // Each door's handle follows its own frame.
        foreach (Entity door in loadedDoors)
        {
            await Assert.That(loaded.Read<SceneFollow>(Children(loaded, door)[0]).Target).IsEqualTo(door);
        }
    }

    [Test]
    public async Task InstancesAddAndRemoveComponentsAndAddChildren()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity door = Prefab.Instantiate(world, _door, library);
        Entity handle = Children(world, door)[0];
        world.Remove(door, Components.Of<SceneLabel>());
        world.Set(handle, new SceneStun { Seconds = 2 });
        Entity knocker = world.CreateEntity();
        world.Set(knocker, new SceneHealth { Value = 5 });
        world.SetParent(knocker, handle);
        string json = Text(Scene.Save(world, _sceneId, library));
        await Assert.That(json).Contains("\"Removed\": [");
        await Assert.That(json).Contains("\"AddedChildren\": {");

        using World loaded = new();
        Entity root = Scene.Load(loaded, Encoding.UTF8.GetBytes(json), prefabs: library).Roots.Single();
        Entity loadedHandle = Children(loaded, root)[0];
        await Assert.That(loaded.Has(root, Components.Of<SceneLabel>())).IsFalse();
        await Assert.That(loaded.Read<SceneStun>(loadedHandle).Seconds).IsEqualTo(2f);
        Entity loadedKnocker = Children(loaded, loadedHandle).Single();
        await Assert.That(loaded.Read<SceneHealth>(loadedKnocker).Value).IsEqualTo(5);
    }

    [Test]
    public async Task InstancesKeepTheirOwnIdsAndReferences()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity first = Prefab.Instantiate(world, _door, library);
        Entity second = Prefab.Instantiate(world, _door, library);
        HashSet<ulong> ids = [];
        foreach (Entity root in (Entity[])[first, second])
        {
            ids.Add(world.Read<SceneEntityId>(root).Value);
            ids.Add(world.Read<SceneEntityId>(Children(world, root)[0]).Value);
            await Assert.That(world.Read<SceneFollow>(Children(world, root)[0]).Target).IsEqualTo(root);
        }
        await Assert.That(ids.Count).IsEqualTo(4);

        // Loading a scene twice with new IDs keeps instances apart too.
        byte[] scene = Scene.Save(world, _sceneId, library);
        using World twice = new();
        Scene.Load(twice, scene, prefabs: library);
        Scene.Load(twice, scene, newIds: true, prefabs: library);
        await Assert.That(twice.EntityCount).IsEqualTo(8);
        await Assert.That(Text(Scene.Save(twice, _sceneId, library))).Contains("\"Prefab\"");
    }

    [Test]
    public async Task UnpackingAnInstanceSavesPlainEntities()
    {
        Files files = Sample();
        using World world = new();
        Entity door = Prefab.Instantiate(world, _door, files.Library());
        world.Remove(door, Components.Of<PrefabInstance>());
        string json = Text(Scene.Save(world, _sceneId));
        await Assert.That(json).DoesNotContain("\"Prefab\"");
        await Assert.That(json).Contains("Crucible.Tests.SceneLabel");
        await Assert.That(json).Contains("\"Children\"");
    }

    [Test]
    public async Task SavingRefusesWhatAnInstanceCannotExpress()
    {
        Files files = Sample();
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity door = Prefab.Instantiate(world, _door, library);
        Entity handle = Children(world, door)[0];
        Entity elsewhere = world.CreateEntity();
        world.SetParent(handle, elsewhere);
        InvalidOperationException moved = Assert.Throws<InvalidOperationException>(() => Scene.Save(world, _sceneId, library));
        await Assert.That(moved.Message).Contains("can't reparent");
        world.Destroy(handle);
        InvalidOperationException gone = Assert.Throws<InvalidOperationException>(() => Scene.Save(world, _sceneId, library));
        await Assert.That(gone.Message).Contains("can't remove");
        InvalidOperationException noLibrary = Assert.Throws<InvalidOperationException>(() => Scene.Save(world, _sceneId));
        await Assert.That(noLibrary.Message).Contains("needs a prefab library");
    }

    [Test]
    public async Task MissingSelfContainingAndManyRootedPrefabsAreErrors()
    {
        Files files = new();
        using World world = new();
        JsonException missing = Assert.Throws<JsonException>(() => Prefab.Instantiate(world, _door, files.Library()));
        await Assert.That(missing.Message).Contains($"the prefab {_door} can't be found");

        files.Bytes[_door] = Encoding.UTF8.GetBytes($$"""
            { "$guid": "{{_door.Value}}", "Entities": [ { "Id": "0000000000000001", "Prefab": "{{_door.Value}}" } ] }
            """);
        JsonException loop = Assert.Throws<JsonException>(() => Prefab.Instantiate(world, _door, files.Library()));
        await Assert.That(loop.Message).Contains("contains itself");

        files.Bytes[_door] = Encoding.UTF8.GetBytes("""
            { "Entities": [ { "Id": "0000000000000001" }, { "Id": "0000000000000002" } ] }
            """);
        JsonException twoRoots = Assert.Throws<JsonException>(() => Prefab.Instantiate(world, _door, files.Library()));
        await Assert.That(twoRoots.Message).Contains("has 2 root entities");
    }

    // A MESH file with just nodes (ADR 0043's layout): "Body" with mesh 0,
    // and its children "Lid" (mesh 1) and another, without a mesh, named
    // `second`; listed in the given order.
    private static byte[] Model(bool reversed, string second = "Hinge")
    {
        (string Name, int Parent, int Mesh, float X)[] nodes =
            reversed
                ? [(second, 2, -1, 2f), ("Lid", 2, 1, 1f), ("Body", -1, 0, 0f)]
                : [("Body", -1, 0, 0f), ("Lid", 0, 1, 1f), (second, 0, -1, 2f)];
        List<byte> names = [];
        byte[] table = new byte[nodes.Length * 56];
        for (int i = 0; i < nodes.Length; ++i)
        {
            Span<byte> node = table.AsSpan(i * 56, 56);
            BitConverter.TryWriteBytes(node[0..], nodes[i].Parent);
            BitConverter.TryWriteBytes(node[4..], nodes[i].Mesh);
            BitConverter.TryWriteBytes(node[8..], nodes[i].X); // translation x
            BitConverter.TryWriteBytes(node[32..], 1f);        // rotation w
            for (int c = 0; c < 3; ++c)
            {
                BitConverter.TryWriteBytes(node[(36 + c * 4)..], 1f); // scale
            }
            BitConverter.TryWriteBytes(node[48..], (uint)names.Count);
            BitConverter.TryWriteBytes(node[52..], (uint)nodes[i].Name.Length);
            names.AddRange(Encoding.UTF8.GetBytes(nodes[i].Name));
        }
        string path = Path.Combine(Path.GetTempPath(), $"crucible-model-{Guid.NewGuid():N}.mesh");
        using (ImportedFileWriter writer = new(Tag.Of("MESH"), 1))
        {
            writer.Add(Tag.Of("NODE"), 1, table);
            writer.Add(Tag.Of("NAME"), 1, [.. names]);
            writer.Write(path, Compression.None);
        }
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    [Test]
    public async Task ModelsInstantiateOneEntityPerNode()
    {
        Files files = new();
        files.Bytes[_model] = Model(reversed: false, second: "Lid"); // two siblings named alike
        using World world = new();
        Entity root = Prefab.Instantiate(world, _model, files.Library());
        await Assert.That(world.Read<LocalTransform>(root).Scale).IsEqualTo(Vector3.One);
        Entity body = Children(world, root).Single();
        await Assert.That(world.Read<MeshInstance>(body)).IsEqualTo(new MeshInstance { Model = _model, Mesh = 0 });
        Entity[] lids = Children(world, body);
        await Assert.That(lids.Length).IsEqualTo(2);
        await Assert.That(world.Read<LocalTransform>(lids[0]).Position.X).IsEqualTo(1f);
        await Assert.That(world.Read<MeshInstance>(lids[0]).Mesh).IsEqualTo(1);
        await Assert.That(world.Has(lids[1], Components.Of<MeshInstance>())).IsFalse();
        await Assert.That(world.Read<SceneEntityId>(lids[0]).Value).IsNotEqualTo(world.Read<SceneEntityId>(lids[1]).Value);
    }

    [Test]
    public async Task ModelOverridesSurviveReorderedNodes()
    {
        Files files = new();
        files.Bytes[_model] = Model(reversed: false);
        PrefabLibrary library = files.Library();
        using World world = new();
        Entity root = Prefab.Instantiate(world, _model, library);
        Entity secondLid = Children(world, Children(world, root)[0])[1];
        world.Get<LocalTransform>(secondLid).Position.Y = 5f;
        byte[] scene = Scene.Save(world, _sceneId, library);

        // Re-exported with the nodes in another order: the override stays on
        // the hinge.
        files.Bytes[_model] = Model(reversed: true);
        using World loaded = new();
        Entity loadedRoot = Scene.Load(loaded, scene, prefabs: files.Library()).Roots.Single();
        Entity raised = Children(loaded, Children(loaded, loadedRoot)[0])
            .Single(lid => loaded.Read<LocalTransform>(lid).Position.Y == 5f);
        await Assert.That(loaded.Has(raised, Components.Of<MeshInstance>())).IsFalse();
        await Assert.That(loaded.Read<LocalTransform>(raised).Position.X).IsEqualTo(2f);
    }
}
