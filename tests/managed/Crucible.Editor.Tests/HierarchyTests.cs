using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Ecs;
using Crucible.Scenes;

namespace Crucible.Editor.Tests;

// The hierarchy's edits as undo steps (ADR 0068), on a world: components
// set from JSON, subtrees deleted and brought back with their scene IDs,
// entities moved between parents keeping where they are. The panel itself
// runs in launcher.jit.hierarchy.
public class HierarchyTests
{
    [Before(Class)]
    public static void Register() => SceneIds.RegisterComponents();

    private static Entity Make(World world, string name, Entity parent = default, Vector3 position = default)
    {
        Entity entity = world.CreateEntity();
        world.Set(entity, LocalTransform.Identity with { Position = position });
        // What the frame loop's propagation adds; world matrices go only through parents with one.
        world.Add(entity, Components.Of<WorldTransform>());
        world.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        world.SetManaged(entity, new EntityName { Value = name });
        if (!parent.IsNull)
        {
            world.SetParent(entity, parent);
        }
        return entity;
    }

    private static string Names(World world, Entity parent) =>
        string.Join(",", SceneIds.ChildrenOf(world, parent).Select(child => world.GetManaged<EntityName>(child).Value));

    private static readonly string _transform = typeof(LocalTransform).FullName!;
    private static readonly string _name = typeof(EntityName).FullName!;

    [Test]
    public async Task ComponentEditsUndoTheirJsonAndMergeInAGroup()
    {
        using World world = new();
        Entity house = Make(world, "House");
        ulong id = SceneIds.IdOf(world, house);
        UndoHistory history = new();
        history.BeginGroup("Move");
        for (int step = 1; step <= 3; ++step)
        {
            JsonNode? before = SceneIds.Read(world, house, _transform);
            world.Set(house, LocalTransform.Identity with { Position = new Vector3(step, 0, 0) });
            history.Push(new ComponentEdit("Move", id, _transform, before, SceneIds.Read(world, house, _transform)));
        }
        history.EndGroup();
        history.Undo(world);
        await Assert.That(world.Read<LocalTransform>(house).Position).IsEqualTo(Vector3.Zero);
        history.Redo(world);
        await Assert.That(world.Read<LocalTransform>(house).Position).IsEqualTo(new Vector3(3, 0, 0));

        // A name added, then undone away: null JSON is no component.
        Entity bare = world.CreateEntity();
        world.Set(bare, new SceneEntityId { Value = SceneIds.NewId() });
        await Assert.That(SceneIds.Read(world, bare, _name)).IsNull();
        world.SetManaged(bare, new EntityName { Value = "Well" });
        history.Push(new ComponentEdit("Rename", SceneIds.IdOf(world, bare), _name, null, SceneIds.Read(world, bare, _name)));
        history.Undo(world);
        await Assert.That(world.Has(bare, Components.Of<EntityName>())).IsFalse();
        history.Redo(world);
        await Assert.That(world.GetManaged<EntityName>(bare).Value).IsEqualTo("Well");
    }

    [Test]
    public async Task DeletedSubtreesComeBackWithTheirIdsAndPlace()
    {
        using World world = new();
        Entity village = Make(world, "Village");
        Make(world, "Well", village);
        Entity house = Make(world, "House", village, new Vector3(4, 0, 0));
        Entity door = Make(world, "Door", house);
        Make(world, "Tree", village);
        ulong houseId = SceneIds.IdOf(world, house), doorId = SceneIds.IdOf(world, door);

        SubtreeEdit delete = new("Delete", houseId, SceneIds.Capture(world, house, null), SceneIds.IdOf(world, village),
            SceneIds.SiblingIndex(world, house), created: false, null);
        UndoHistory history = new();
        world.Destroy(house);
        history.Push(delete);
        await Assert.That(Names(world, village)).IsEqualTo("Well,Tree");
        await Assert.That(SceneIds.Find(world, doorId).IsNull).IsTrue();

        history.Undo(world);
        await Assert.That(Names(world, village)).IsEqualTo("Well,House,Tree");
        Entity back = SceneIds.Find(world, houseId);
        await Assert.That(back.IsNull).IsFalse();
        await Assert.That(world.Read<LocalTransform>(back).Position).IsEqualTo(new Vector3(4, 0, 0));
        await Assert.That(world.ParentOf(SceneIds.Find(world, doorId)).Value).IsEqualTo(back.Value);
        history.Redo(world);
        await Assert.That(Names(world, village)).IsEqualTo("Well,Tree");

        // Made (a duplicate): undo removes it, redo brings it back.
        Entity copy = SceneIds.Restore(world, SceneIds.Capture(world, village, null), 0, -1, null, newIds: true);
        await Assert.That(SceneIds.IdOf(world, copy)).IsNotEqualTo(SceneIds.IdOf(world, village));
        ulong copyId = SceneIds.IdOf(world, copy);
        history.Push(new SubtreeEdit("Duplicate", copyId, SceneIds.Capture(world, copy, null), 0, -1, created: true, null));
        history.Undo(world);
        await Assert.That(SceneIds.Find(world, copyId).IsNull).IsTrue();
        history.Redo(world);
        await Assert.That(Names(world, SceneIds.Find(world, copyId))).IsEqualTo("Well,Tree");
    }

    [Test]
    public async Task MovesKeepTheWorldTransformAndUndoToTheOldPlace()
    {
        using World world = new();
        Entity village = Make(world, "Village");
        Make(world, "Well", village);
        Entity house = Make(world, "House", village, new Vector3(4, 0, 0));
        Entity tree = Make(world, "Tree", village, new Vector3(0, 0, 2));

        // Reordered within its parent: an index among the others.
        world.SetParent(tree, village, 0);
        await Assert.That(Names(world, village)).IsEqualTo("Tree,Well,House");
        world.SetParent(tree, village, 2);
        await Assert.That(Names(world, village)).IsEqualTo("Well,House,Tree");

        // Under the house: its world position stays (2 along Z from the village, -4 along X from the house).
        (ulong, int, LocalTransform) before = (SceneIds.IdOf(world, village), SceneIds.SiblingIndex(world, tree),
            world.Read<LocalTransform>(tree));
        LocalTransform local = ParentEdit.Keeping(world, tree, house);
        world.SetParent(tree, house);
        world.Set(tree, local);
        await Assert.That(Vector3.Distance(world.ComputeWorldTransform(tree).Translation, new Vector3(0, 0, 2))).IsLessThan(1e-4f);
        await Assert.That(Vector3.Distance(local.Position, new Vector3(-4, 0, 2))).IsLessThan(1e-4f);
        UndoHistory history = new();
        history.Push(new ParentEdit("Move in Hierarchy", SceneIds.IdOf(world, tree), before,
            (SceneIds.IdOf(world, house), 0, local)));
        history.Undo(world);
        await Assert.That(world.ParentOf(tree).Value).IsEqualTo(village.Value);
        await Assert.That(Names(world, village)).IsEqualTo("Well,House,Tree");
        history.Redo(world);
        await Assert.That(world.ParentOf(tree).Value).IsEqualTo(house.Value);
        await Assert.That(SceneIds.IsInsideInstance(world, tree)).IsFalse();
    }
}
