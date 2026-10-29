using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

// One file's entities by their IDs in that file (ADR 0045): its own plain
// entities, and every entity of the instances it contains under derived
// IDs. Entity references in the file are read and written through it.
internal sealed class Frame : IEntityMap
{
    // Where an ID came from: a plain entity of the file (Instance 0), an
    // instance's root (Inner 0), or an entity inside an instance.
    public readonly record struct Entry(ulong Id, Entity Entity, ulong Instance, ulong Inner);

    private readonly Dictionary<ulong, Entity> _byId = [];
    private readonly Dictionary<Entity, ulong> _toId = [];

    public readonly List<Entry> Entries = [];

    public void Add(ulong id, Entity entity, ulong instance = 0, ulong inner = 0)
    {
        if (!_byId.TryAdd(id, entity))
        {
            throw new JsonException($"two entities have the ID {id:x16}");
        }
        _toId[entity] = id;
        Entries.Add(new Entry(id, entity, instance, inner));
    }

    public bool TryToScene(Entity entity, out ulong sceneId) => _toId.TryGetValue(entity, out sceneId);

    public Entity ToWorld(ulong sceneId) => _byId.GetValueOrDefault(sceneId);
}

// Creates a file's entities in a world, instancing the prefabs it names,
// each from its own file with its own frame.
internal sealed class SceneLoader(World world, PrefabLibrary? library)
{
    private readonly Stack<AssetId> _loading = [];

    public readonly List<string> Skipped = [];

    /// <summary>
    /// Creates the file's entities under <paramref name="parent"/>. Instances
    /// it contains directly get <see cref="PrefabInstance"/> when
    /// <paramref name="outermost"/>: the file being opened, not a prefab
    /// inside one.
    /// </summary>
    public Frame Load(SceneFile file, Entity parent, bool outermost, List<Entity>? roots = null)
    {
        Frame frame = new();
        List<(EntityNode Node, Entity Entity)> plain = [];
        List<(EntityNode Node, HashSet<ulong> Members)> instances = [];

        Entity Create(EntityNode node, Entity under)
        {
            if (!node.IsInstance)
            {
                Entity entity = world.CreateEntity();
                if (!under.IsNull)
                {
                    world.SetParent(entity, under);
                }
                frame.Add(node.Id, entity);
                plain.Add((node, entity));
                foreach (EntityNode child in node.Children)
                {
                    Create(child, entity);
                }
                return entity;
            }

            SceneFile prefab = Open(node.Prefab);
            _loading.Push(node.Prefab);
            Frame inner = Load(prefab, under, outermost: false);
            _loading.Pop();
            ulong innerRoot = prefab.Roots[0].Id;
            HashSet<ulong> members = [];
            foreach (Frame.Entry entry in inner.Entries)
            {
                bool isRoot = entry.Id == innerRoot;
                ulong id = isRoot ? node.Id : PrefabIds.Derive(node.Id, entry.Id);
                frame.Add(id, entry.Entity, node.Id, isRoot ? 0 : entry.Id);
                members.Add(id);
            }
            Entity root = frame.ToWorld(node.Id);
            if (outermost)
            {
                world.Set(root, new PrefabInstance { Prefab = node.Prefab });
            }
            foreach ((ulong target, List<EntityNode> children) in node.AddedChildren)
            {
                if (!members.Contains(target))
                {
                    throw new JsonException($"entity {node.Id:x16} adds children to {target:x16}, which isn't in its prefab");
                }
                foreach (EntityNode child in children)
                {
                    Create(child, frame.ToWorld(target));
                }
            }
            instances.Add((node, members));
            return root;
        }

        foreach (EntityNode node in file.Roots)
        {
            Entity created = Create(node, parent);
            roots?.Add(created);
        }

        using (EntityReferences.Use(frame))
        {
            foreach ((EntityNode node, Entity entity) in plain)
            {
                foreach (ComponentJson component in node.Components)
                {
                    Read(component, entity);
                }
            }
            foreach ((EntityNode node, HashSet<ulong> members) in instances)
            {
                foreach ((ulong target, OverrideNode change) in node.Overrides)
                {
                    if (!members.Contains(target))
                    {
                        throw new JsonException($"entity {node.Id:x16} overrides {target:x16}, which isn't in its prefab");
                    }
                    Apply(change, frame.ToWorld(target));
                }
            }
        }
        return frame;
    }

    private SceneFile Open(AssetId prefab)
    {
        if (_loading.Contains(prefab))
        {
            throw new JsonException($"the prefab {prefab} contains itself");
        }
        return library?.Get(prefab) ?? throw new JsonException($"the scene has an instance of {prefab}, but no prefab library was given");
    }

    private void Read(ComponentJson component, Entity entity)
    {
        if (SavedComponents.Find(component.Name) is not { } saved)
        {
            Skipped.Add(component.Name);
            return;
        }
        ReadValue(saved, component.Name, component.Json.Span, entity);
    }

    // An override: changed fields merged into the current value (in this
    // file's IDs, so references on both sides agree), added components in
    // full, removed ones removed.
    private void Apply(OverrideNode change, Entity entity)
    {
        foreach (ComponentJson component in change.Components)
        {
            if (SavedComponents.Find(component.Name) is not { } saved)
            {
                Skipped.Add(component.Name);
                continue;
            }
            if (!world.Has(entity, saved.Id))
            {
                ReadValue(saved, component.Name, component.Json.Span, entity);
                continue;
            }
            JsonNode? merged = ComponentDiff.Merge(ComponentDiff.ToNode(saved, world, entity),
                JsonNode.Parse(component.Json.Span));
            ReadValue(saved, component.Name, ComponentDiff.ToBytes(merged), entity);
        }
        foreach (string name in change.Removed)
        {
            if (SavedComponents.Find(name) is { } saved && world.Has(entity, saved.Id))
            {
                world.Remove(entity, saved.Id);
            }
        }
    }

    private void ReadValue(SavedComponent saved, string name, ReadOnlySpan<byte> json, Entity entity)
    {
        Utf8JsonReader reader = new(json);
        reader.Read();
        try
        {
            saved.Read(ref reader, world, entity);
        }
        catch (JsonException exception)
        {
            throw new JsonException($"{name}: {exception.Message}", exception);
        }
    }
}

// Field-level differences between component values, as JSON (ADR 0045):
// objects compare member by member, recursively; arrays and values whole.
internal static class ComponentDiff
{
    public static JsonNode? ToNode(SavedComponent saved, World world, Entity entity)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            saved.Write(writer, world, entity);
        }
        return JsonNode.Parse(buffer.WrittenSpan);
    }

    public static byte[] ToBytes(JsonNode? node)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                node.WriteTo(writer);
            }
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>What <paramref name="actual"/> changes from <paramref name="expected"/>; false if nothing.</summary>
    public static bool TryDiff(JsonNode? expected, JsonNode? actual, out JsonNode? difference)
    {
        if (expected is JsonObject before && actual is JsonObject after)
        {
            JsonObject changed = [];
            foreach ((string key, JsonNode? value) in after)
            {
                if (!before.TryGetPropertyValue(key, out JsonNode? old))
                {
                    changed[key] = value?.DeepClone();
                }
                else if (TryDiff(old, value, out JsonNode? inner))
                {
                    changed[key] = inner;
                }
            }
            difference = changed;
            return changed.Count > 0;
        }
        difference = actual?.DeepClone();
        return !JsonNode.DeepEquals(expected, actual);
    }

    /// <summary><paramref name="value"/> with <paramref name="changes"/> applied: objects merge, anything else replaces.</summary>
    public static JsonNode? Merge(JsonNode? value, JsonNode? changes)
    {
        if (value is not JsonObject into || changes is not JsonObject from)
        {
            return changes?.DeepClone();
        }
        foreach ((string key, JsonNode? change) in from)
        {
            into[key] = into.TryGetPropertyValue(key, out JsonNode? old) ? Merge(old?.DeepClone(), change) : change?.DeepClone();
        }
        return into;
    }
}
