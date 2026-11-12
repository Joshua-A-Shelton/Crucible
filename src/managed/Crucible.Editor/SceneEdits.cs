using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Editor;

/// <summary>
/// The scene's entities by scene ID (ADR 0068), which undo steps and the
/// hierarchy go by: found by a scan of the world, as the editor's scenes are
/// small. It also maps entity references while components are read and
/// written as JSON.
/// </summary>
internal sealed class SceneIds : IEntityMap
{
    private readonly Dictionary<ulong, Entity> _toWorld = [];
    private readonly Dictionary<Entity, ulong> _toScene = [];

    static SceneIds() => EngineComponents.Register();

    private SceneIds()
    {
    }

    /// <summary>The engine's saved components, registered (scenes do it on first use; tests ask first).</summary>
    public static void RegisterComponents() => EngineComponents.Register();

    /// <summary>Every entity with a scene ID, now.</summary>
    public static SceneIds Of(World world)
    {
        SceneIds ids = new();
        using Query query = world.CreateQuery([Components.Of<SceneEntityId>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        foreach (Entity entity in collector.Found)
        {
            ulong id = world.Read<SceneEntityId>(entity).Value;
            ids._toWorld.TryAdd(id, entity);
            ids._toScene.TryAdd(entity, id);
        }
        return ids;
    }

    public IEnumerable<Entity> Entities => _toScene.Keys;

    public bool TryToScene(Entity entity, out ulong sceneId) => _toScene.TryGetValue(entity, out sceneId);

    public Entity ToWorld(ulong sceneId) => _toWorld.GetValueOrDefault(sceneId);

    public static Entity Find(World world, ulong sceneId) => sceneId == 0 ? Entity.Null : Of(world).ToWorld(sceneId);

    /// <summary>An entity's scene ID; 0 without one.</summary>
    public static ulong IdOf(World world, Entity entity) =>
        entity.Value != 0 && world.IsAlive(entity) && world.Has(entity, Components.Of<SceneEntityId>())
            ? world.Read<SceneEntityId>(entity).Value
            : 0;

    /// <summary>A fresh scene ID, for an entity the editor makes.</summary>
    public static ulong NewId() => PrefabIds.NewId();

    /// <summary>A saved component's value as JSON; null when the entity hasn't it (or it isn't saved).</summary>
    public static JsonNode? Read(World world, Entity entity, string component)
    {
        if (SavedComponents.Find(component) is not { } saved || !world.Has(entity, saved.Id))
        {
            return null;
        }
        using (EntityReferences.Use(Of(world)))
        {
            return ComponentDiff.ToNode(saved, world, entity);
        }
    }

    /// <summary>Sets a saved component from JSON, adding it if missing; null removes it.</summary>
    public static void Write(World world, Entity entity, string component, JsonNode? value)
    {
        if (SavedComponents.Find(component) is not { } saved)
        {
            return;
        }
        if (value is null)
        {
            world.Remove(entity, saved.Id);
            return;
        }
        byte[] json = ComponentDiff.ToBytes(value);
        Utf8JsonReader reader = new(json);
        reader.Read();
        using (EntityReferences.Use(Of(world)))
        {
            saved.Read(ref reader, world, entity);
        }
    }

    /// <summary>The entity's place among its parent's children (or -1 for a root).</summary>
    public static int SiblingIndex(World world, Entity entity)
    {
        Entity parent = world.ParentOf(entity);
        if (parent.IsNull)
        {
            return -1;
        }
        Entity[] children = ChildrenOf(world, parent);
        return Array.IndexOf(children, entity);
    }

    public static Entity[] ChildrenOf(World world, Entity parent)
    {
        Entity[] children = new Entity[16];
        int count = world.ChildrenOf(parent, children);
        if (count > children.Length)
        {
            children = new Entity[count];
            count = world.ChildrenOf(parent, children);
        }
        return children[..count];
    }

    /// <summary>Whether it's part of a prefab instance below the instance's root (whose shape the prefab owns).</summary>
    public static bool IsInsideInstance(World world, Entity entity)
    {
        ComponentId instance = Components.Of<PrefabInstance>();
        for (Entity parent = world.ParentOf(entity); !parent.IsNull; parent = world.ParentOf(parent))
        {
            if (world.Has(parent, instance))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>An entity and what's under it, as a scene file with one root (instances as instances).</summary>
    public static byte[] Capture(World world, Entity root, PrefabLibrary? prefabs) =>
        Prefab.Save(world, root, AssetId.None, prefabs);

    /// <summary>Brings back what <see cref="Capture"/> wrote, with its scene IDs, under a parent at an index.</summary>
    public static Entity Restore(World world, byte[] subtree, ulong parentId, int index, PrefabLibrary? prefabs,
        bool newIds = false)
    {
        SceneLoad load = Scene.Load(world, subtree, newIds, prefabs);
        Entity root = load.Roots[0];
        Entity parent = Find(world, parentId);
        if (!parent.IsNull)
        {
            world.SetParent(root, parent, index < 0 ? uint.MaxValue : (uint)index);
        }
        return root;
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}

/// <summary>A saved component's value changed, or it was added or removed (null JSON for absent).</summary>
internal sealed class ComponentEdit(string title, ulong entity, string component, JsonNode? before, JsonNode? after)
    : UndoStep(title)
{
    private JsonNode? _after = after;

    public ulong Entity { get; } = entity;

    public string Component { get; } = component;

    public override void Undo(World world) => Apply(world, before);

    public override void Redo(World world) => Apply(world, _after);

    private void Apply(World world, JsonNode? value)
    {
        Entity entity = SceneIds.Find(world, Entity);
        if (!entity.IsNull)
        {
            SceneIds.Write(world, entity, Component, value?.DeepClone());
        }
    }

    internal override bool TryMerge(UndoStep later)
    {
        if (later is not ComponentEdit edit || edit.Entity != Entity || edit.Component != Component)
        {
            return false;
        }
        _after = edit._after;
        return true;
    }
}

/// <summary>Entities made (Create Empty, Duplicate) or deleted: the subtree as a scene file, and where it goes.</summary>
internal sealed class SubtreeEdit(string title, ulong root, byte[] subtree, ulong parent, int index, bool created,
    PrefabLibrary? prefabs) : UndoStep(title)
{
    public ulong Root { get; } = root;

    public override void Undo(World world)
    {
        if (created)
        {
            Remove(world);
        }
        else
        {
            SceneIds.Restore(world, subtree, parent, index, prefabs);
        }
    }

    public override void Redo(World world)
    {
        if (created)
        {
            SceneIds.Restore(world, subtree, parent, index, prefabs);
        }
        else
        {
            Remove(world);
        }
    }

    private void Remove(World world)
    {
        Entity entity = SceneIds.Find(world, Root);
        if (!entity.IsNull)
        {
            world.Destroy(entity);
        }
    }
}

/// <summary>An entity moved to another parent or place among its siblings, its world transform kept.</summary>
internal sealed class ParentEdit(string title, ulong entity, (ulong Parent, int Index, LocalTransform Local) before,
    (ulong Parent, int Index, LocalTransform Local) after) : UndoStep(title)
{
    public override void Undo(World world) => Apply(world, before);

    public override void Redo(World world) => Apply(world, after);

    private void Apply(World world, (ulong Parent, int Index, LocalTransform Local) place)
    {
        SceneIds ids = SceneIds.Of(world);
        Entity child = ids.ToWorld(entity);
        if (child.IsNull)
        {
            return;
        }
        Entity parent = ids.ToWorld(place.Parent);
        world.SetParent(child, parent, place.Index < 0 ? uint.MaxValue : (uint)place.Index);
        // A 2D entity (ADR 0093) keeps its Transform2D as it is.
        if (world.Has(child, Components.Of<LocalTransform>()))
        {
            world.Set(child, place.Local);
        }
    }

    /// <summary>The local transform that keeps an entity's world transform under a new parent.</summary>
    public static LocalTransform Keeping(World world, Entity entity, Entity parent)
    {
        Matrix4x4 worldMatrix = world.ComputeWorldTransform(entity);
        if (!parent.IsNull && Matrix4x4.Invert(world.ComputeWorldTransform(parent), out Matrix4x4 inverse))
        {
            worldMatrix *= inverse;
        }
        Matrix4x4.Decompose(worldMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 position);
        return new LocalTransform { Position = position, Rotation = rotation, Scale = scale };
    }
}
