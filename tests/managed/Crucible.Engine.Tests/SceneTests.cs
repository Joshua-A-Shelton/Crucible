using System.Text;
using System.Text.Json;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Tests;

// Scenes (ADR 0041), under JIT and NativeAOT.

[Component, Serialized]
public partial struct SceneHealth
{
    public int Value;
}

[Component, Serialized]
public partial struct SceneFollow
{
    public Entity Target;
    public float Distance;
}

[Component(Sparse = true), Serialized]
public partial struct SceneStun
{
    public float Seconds;
}

[Component, Serialized]
public partial struct SceneMarker;

[Component, Serialized]
public partial class SceneLabel
{
    public string Text = "";
}

// Not [Serialized]: a runtime cache, never saved.
[Component]
public struct SceneCache
{
    public int Value;
}

public class SceneTests
{
    private static readonly AssetId _sceneId = AssetId.Parse("5e1f0c2a-9b3d-4e6f-8a7b-0c1d2e3f4a5b");

    [Before(Class)]
    public static void Register() => new Crucible.Generated.ComponentRegistration().Register();

    // Two roots; the first has two children in a set order, and references
    // point forward, backward, and at a destroyed entity.
    private static World Sample(out Entity hero)
    {
        World world = new();
        hero = world.CreateEntity(Components.Of<SceneHealth>(), Components.Of<SceneMarker>());
        world.Set(hero, new SceneHealth { Value = 10 });
        world.SetManaged(hero, new SceneLabel { Text = "hero" });
        Entity sidekick = world.CreateEntity();
        Entity shield = world.CreateEntity();
        world.Set(shield, new SceneHealth { Value = 3 });
        world.Set(shield, new SceneCache { Value = 99 });
        world.SetParent(shield, hero);
        world.SetParent(sidekick, hero, 0); // first, though created later than the shield
        world.Set(sidekick, new SceneFollow { Target = shield, Distance = 1.5f });
        world.Set(sidekick, new SceneStun { Seconds = 2 });
        Entity camera = world.CreateEntity();
        world.Set(camera, new SceneFollow { Target = hero, Distance = 8 });
        Entity gone = world.CreateEntity();
        Entity ghost = world.CreateEntity();
        world.Set(ghost, new SceneFollow { Target = gone });
        world.Destroy(gone);
        return world;
    }

    private static string Text(byte[] json) => Encoding.UTF8.GetString(json);

    [Test]
    public async Task SavingTheLoadedSceneGivesTheSameFile()
    {
        using World original = Sample(out _);
        byte[] first = Scene.Save(original, _sceneId);
        // Saving again keeps every ID.
        await Assert.That(Text(Scene.Save(original, _sceneId))).IsEqualTo(Text(first));

        using World loaded = new();
        SceneLoad load = Scene.Load(loaded, first);
        await Assert.That(load.Id).IsEqualTo(_sceneId);
        await Assert.That(load.SkippedComponents).IsEmpty();
        await Assert.That(Text(Scene.Save(loaded, _sceneId))).IsEqualTo(Text(first));
    }

    [Test]
    public async Task LoadingRebuildsEntitiesHierarchyAndReferences()
    {
        using World original = Sample(out _);
        using World world = new();
        SceneLoad load = Scene.Load(world, Scene.Save(original, _sceneId));
        await Assert.That(world.EntityCount).IsEqualTo(5); // the destroyed one isn't there

        Entity hero = load.Roots.Single(root => world.Has(root, Components.Of<SceneMarker>()));
        await Assert.That(world.Read<SceneHealth>(hero).Value).IsEqualTo(10);
        await Assert.That(world.GetManaged<SceneLabel>(hero).Text).IsEqualTo("hero");
        Entity[] children = new Entity[4];
        await Assert.That(world.ChildrenOf(hero, children)).IsEqualTo(2);
        Entity sidekick = children[0];
        Entity shield = children[1];
        await Assert.That(world.Read<SceneHealth>(shield).Value).IsEqualTo(3);
        await Assert.That(world.Has(shield, Components.Of<SceneCache>())).IsFalse(); // not saved
        await Assert.That(world.Read<SceneFollow>(sidekick).Target).IsEqualTo(shield); // a reference forward
        await Assert.That(world.Read<SceneStun>(sidekick).Seconds).IsEqualTo(2f);     // sparse
        Entity camera = load.Roots.Single(root =>
            world.Has(root, Components.Of<SceneFollow>()) && world.Read<SceneFollow>(root).Target.Equals(hero));
        await Assert.That(world.Read<SceneFollow>(camera).Distance).IsEqualTo(8f);
        Entity ghost = load.Roots.Single(root => !root.Equals(hero) && !root.Equals(camera));
        await Assert.That(world.Read<SceneFollow>(ghost).Target.IsNull).IsTrue(); // it pointed at a destroyed entity
    }

    [Test]
    public async Task TheFileNestsChildrenAndNamesReferencesByScene()
    {
        using World world = Sample(out Entity hero);
        string json = Text(Scene.Save(world, _sceneId));
        string heroId = world.Read<SceneEntityId>(hero).Value.ToString("x16");
        await Assert.That(json).StartsWith("{\n  \"$guid\": \"5e1f0c2a-9b3d-4e6f-8a7b-0c1d2e3f4a5b\",\n  \"Entities\": [\n");
        await Assert.That(json).Contains($"\"Target\": \"{heroId}\"");
        await Assert.That(json).Contains("\"Target\": null");
        await Assert.That(json).DoesNotContain("SceneCache");
        await Assert.That(json).DoesNotContain("SceneEntityId");
        // Components in name order, children nested.
        int label = json.IndexOf("Crucible.Tests.SceneLabel", StringComparison.Ordinal);
        await Assert.That(json.IndexOf("Crucible.Tests.SceneHealth", StringComparison.Ordinal)).IsLessThan(label);
        await Assert.That(json).Contains("\"Children\": [");
        await Assert.That(Scene.ReadId(Encoding.UTF8.GetBytes(json))).IsEqualTo(_sceneId);
    }

    [Test]
    public async Task LoadingWithNewIdsMakesAnIndependentCopy()
    {
        using World original = Sample(out _);
        byte[] json = Scene.Save(original, _sceneId);
        using World world = new();
        SceneLoad first = Scene.Load(world, json);
        SceneLoad second = Scene.Load(world, json, newIds: true);
        await Assert.That(world.EntityCount).IsEqualTo(10);
        HashSet<ulong> ids = [];
        Entity[] children = new Entity[4];
        foreach (Entity root in first.Roots.Concat(second.Roots))
        {
            ids.Add(world.Read<SceneEntityId>(root).Value);
        }
        await Assert.That(ids.Count).IsEqualTo(6); // every root distinct across the copies
        // Each copy's references stay inside it.
        Entity secondHero = second.Roots.Single(root => world.Has(root, Components.Of<SceneMarker>()));
        world.ChildrenOf(secondHero, children);
        await Assert.That(world.Read<SceneFollow>(children[0]).Target).IsEqualTo(children[1]);
    }

    [Test]
    public async Task UnknownComponentsAreSkippedAndBadFilesRefused()
    {
        using World world = new();
        SceneLoad load = Scene.Load(world, Encoding.UTF8.GetBytes("""
            { "$guid": null, "Entities": [ { "Id": "0000000000000001", "Components": { "Game.Removed": { "X": 1 } } } ] }
            """));
        await Assert.That(load.SkippedComponents.Single()).IsEqualTo("Game.Removed");
        await Assert.That(load.Roots.Count).IsEqualTo(1);

        JsonException duplicate = Assert.Throws<JsonException>(() => Scene.Load(world, Encoding.UTF8.GetBytes("""
            { "Entities": [ { "Id": "0000000000000002" }, { "Id": "0000000000000002" } ] }
            """)));
        await Assert.That(duplicate.Message).Contains("two entities have the ID 0000000000000002");
        JsonException noId = Assert.Throws<JsonException>(() =>
            Scene.Load(world, Encoding.UTF8.GetBytes("""{ "Entities": [ { "Components": {} } ] }""")));
        await Assert.That(noId.Message).Contains("has no Id");
        JsonException badValue = Assert.Throws<JsonException>(() => Scene.Load(world, Encoding.UTF8.GetBytes("""
            { "Entities": [ { "Id": "0000000000000003", "Components": { "Crucible.Tests.SceneHealth": { "Value": "x" } } } ] }
            """)));
        await Assert.That(badValue.Message).StartsWith("Crucible.Tests.SceneHealth: SceneHealth.Value: expected a number");
    }
}
