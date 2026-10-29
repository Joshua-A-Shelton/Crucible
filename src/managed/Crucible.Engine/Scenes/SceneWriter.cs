using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

// Writes entities as a scene or prefab file (ADR 0041, 0045): plain
// entities with their saved components and children, and prefab instances
// as their prefab plus what they change.
internal sealed class SceneWriter
{
    private readonly World _world;
    private readonly Map _map;
    private readonly Dictionary<Entity, SavedComponent[]> _saved;
    private readonly PrefabLibrary? _library;
    private readonly Utf8JsonWriter _writer;
    private readonly bool _unpack;
    private readonly ComponentId _instanceComponent = Components.Of<PrefabInstance>();

    private SceneWriter(World world, Map map, Dictionary<Entity, SavedComponent[]> saved, PrefabLibrary? library,
        Utf8JsonWriter writer, bool unpack)
    {
        _unpack = unpack;
        _world = world;
        _map = map;
        _saved = saved;
        _library = library;
        _writer = writer;
    }

    /// <summary>
    /// Writes <paramref name="roots"/> and their descendants, or every entity
    /// if null; with <paramref name="unpack"/>, instances as plain entities.
    /// </summary>
    public static byte[] Write(World world, IReadOnlyList<Entity>? roots, AssetId id, PrefabLibrary? library,
        bool unpack = false)
    {
        EngineComponents.Register();
        List<(Entity Entity, ComponentId[] Components)> contents = world.Contents();
        Map map = AssignIds(world, contents);
        Dictionary<Entity, SavedComponent[]> saved = SavedOf(world, contents);
        roots ??= [.. contents.Select(item => item.Entity)
            .Where(entity => world.ParentOf(entity).IsNull)
            .OrderBy(entity => map.SceneIdOf(entity))];

        ArrayBufferWriter<byte> buffer = new();
        using (EntityReferences.Use(map))
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            SceneWriter scene = new(world, map, saved, library, writer, unpack);
            writer.WriteStartObject();
            writer.WritePropertyName(Scene.GuidProperty);
            JsonValues.WriteAssetId(writer, id);
            writer.WriteStartArray("Entities"u8);
            foreach (Entity root in roots)
            {
                scene.WriteEntity(root);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    // Scene IDs: kept where they exist and are unique, new otherwise.
    private static Map AssignIds(World world, List<(Entity Entity, ComponentId[] Components)> contents)
    {
        ComponentId idComponent = Components.Of<SceneEntityId>();
        Map map = new();
        List<Entity> needIds = [];
        foreach ((Entity entity, ComponentId[] components) in contents.OrderBy(item => item.Entity.Value))
        {
            if (components.Contains(idComponent) && map.TryAdd(entity, world.Read<SceneEntityId>(entity).Value))
            {
                continue;
            }
            needIds.Add(entity);
        }
        foreach (Entity entity in needIds)
        {
            ulong sceneId;
            do
            {
                sceneId = PrefabIds.NewId();
            } while (!map.TryAdd(entity, sceneId));
            world.Set(entity, new SceneEntityId { Value = sceneId });
        }
        return map;
    }

    // Each entity's saved components, in name order.
    private static Dictionary<Entity, SavedComponent[]> SavedOf(World world,
        List<(Entity Entity, ComponentId[] Components)> contents)
    {
        SavedComponent[] sparse = [.. SavedComponents.All().Where(saved => (saved.Id.Value & 0x8000_0000u) != 0)];
        Dictionary<Entity, SavedComponent[]> saved = [];
        foreach ((Entity entity, ComponentId[] components) in contents)
        {
            saved[entity] =
            [
                .. components.Select(SavedComponents.Find).OfType<SavedComponent>()
                    .Concat(sparse.Where(component => world.Has(entity, component.Id)))
                    .OrderBy(component => component.Name, StringComparer.Ordinal),
            ];
        }
        return saved;
    }

    private static Entity[] ChildrenOf(World world, Entity entity)
    {
        Entity[] children = new Entity[16];
        int count = world.ChildrenOf(entity, children);
        if (count > children.Length)
        {
            children = new Entity[count];
            count = world.ChildrenOf(entity, children);
        }
        return children[..count];
    }

    private static string Hex(ulong id) => id.ToString("x16", CultureInfo.InvariantCulture);

    private void WriteEntity(Entity entity)
    {
        if (!_unpack && _world.Has(entity, _instanceComponent))
        {
            WriteInstance(entity);
            return;
        }
        _writer.WriteStartObject();
        _writer.WriteString("Id"u8, Hex(_map.SceneIdOf(entity)));
        _writer.WriteStartObject("Components"u8);
        foreach (SavedComponent component in _saved[entity])
        {
            _writer.WritePropertyName(component.Name);
            component.Write(_writer, _world, entity);
        }
        _writer.WriteEndObject();
        Entity[] children = ChildrenOf(_world, entity);
        if (children.Length > 0)
        {
            _writer.WriteStartArray("Children"u8);
            foreach (Entity child in children)
            {
                WriteEntity(child);
            }
            _writer.WriteEndArray();
        }
        _writer.WriteEndObject();
    }

    // An instance: its prefab, created again in a scratch world under the
    // same derived IDs, compared with what the world holds.
    private void WriteInstance(Entity root)
    {
        AssetId prefab = _world.Read<PrefabInstance>(root).Prefab;
        if (_library is null)
        {
            throw new InvalidOperationException($"saving an instance of the prefab {prefab} needs a prefab library");
        }
        ulong instance = _map.SceneIdOf(root);
        SceneFile file = _library.Get(prefab);
        using World scratch = new();
        Frame inner = new SceneLoader(scratch, _library).Load(file, Entity.Null, outermost: false);
        ulong innerRoot = file.Roots[0].Id;
        Map expected = new();
        foreach (Frame.Entry entry in inner.Entries)
        {
            expected.TryAdd(entry.Entity, entry.Id == innerRoot ? instance : PrefabIds.Derive(instance, entry.Id));
        }
        Dictionary<Entity, SavedComponent[]> scratchSaved = SavedOf(scratch, scratch.Contents());

        SortedDictionary<ulong, (JsonObject Components, List<string> Removed)> overrides = [];
        SortedDictionary<ulong, Entity[]> added = [];
        foreach ((Entity original, ulong id) in expected.Pairs.OrderBy(pair => pair.Value))
        {
            Entity live = _map.ToWorld(id);
            if (live.IsNull || !_world.IsAlive(live))
            {
                throw new InvalidOperationException(
                    $"entity {Hex(id)} of an instance of {prefab} is gone: an instance can't remove its prefab's entities");
            }
            if (id != instance)
            {
                expected.TryToScene(scratch.ParentOf(original), out ulong parent);
                Entity liveParent = _world.ParentOf(live);
                if (liveParent.IsNull || !_map.TryToScene(liveParent, out ulong actualParent) || actualParent != parent)
                {
                    throw new InvalidOperationException(
                        $"entity {Hex(id)} of an instance of {prefab} has moved: an instance can't reparent its prefab's entities");
                }
            }

            JsonObject changed = [];
            List<string> removed = [];
            Dictionary<string, SavedComponent> before = scratchSaved[original].ToDictionary(saved => saved.Name);
            Dictionary<string, SavedComponent> after = _saved[live].ToDictionary(saved => saved.Name);
            foreach (string name in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
            {
                if (!after.TryGetValue(name, out SavedComponent? component))
                {
                    removed.Add(name);
                    continue;
                }
                JsonNode? now = ComponentDiff.ToNode(component, _world, live); // with this file's IDs
                if (!before.ContainsKey(name))
                {
                    changed[name] = now;
                    continue;
                }
                JsonNode? then;
                using (EntityReferences.Use(expected))
                {
                    then = ComponentDiff.ToNode(component, scratch, original);
                }
                if (ComponentDiff.TryDiff(then, now, out JsonNode? difference))
                {
                    changed[name] = difference;
                }
            }
            if (changed.Count > 0 || removed.Count > 0)
            {
                overrides[id] = (changed, removed);
            }

            Entity[] extra = [.. ChildrenOf(_world, live)
                .Where(child => !(_map.TryToScene(child, out ulong childId) && expected.Contains(childId)))];
            if (extra.Length > 0)
            {
                added[id] = extra;
            }
        }

        _writer.WriteStartObject();
        _writer.WriteString("Id"u8, Hex(instance));
        _writer.WritePropertyName("Prefab"u8);
        JsonValues.WriteAssetId(_writer, prefab);
        if (overrides.Count > 0)
        {
            _writer.WriteStartObject("Overrides"u8);
            foreach ((ulong id, (JsonObject components, List<string> removed)) in overrides)
            {
                _writer.WriteStartObject(Hex(id));
                if (components.Count > 0)
                {
                    _writer.WritePropertyName("Components"u8);
                    components.WriteTo(_writer);
                }
                if (removed.Count > 0)
                {
                    _writer.WriteStartArray("Removed"u8);
                    foreach (string name in removed)
                    {
                        _writer.WriteStringValue(name);
                    }
                    _writer.WriteEndArray();
                }
                _writer.WriteEndObject();
            }
            _writer.WriteEndObject();
        }
        if (added.Count > 0)
        {
            _writer.WriteStartObject("AddedChildren"u8);
            foreach ((ulong id, Entity[] children) in added)
            {
                _writer.WriteStartArray(Hex(id));
                foreach (Entity child in children)
                {
                    WriteEntity(child);
                }
                _writer.WriteEndArray();
            }
            _writer.WriteEndObject();
        }
        _writer.WriteEndObject();
    }

    // Entities and their scene IDs, both ways.
    private sealed class Map : IEntityMap
    {
        private readonly Dictionary<Entity, ulong> _toScene = [];
        private readonly Dictionary<ulong, Entity> _toWorld = [];

        public IEnumerable<KeyValuePair<Entity, ulong>> Pairs => _toScene;

        // The entity keeps sceneId, unless another already has it.
        public bool TryAdd(Entity entity, ulong sceneId)
        {
            if (!_toWorld.TryAdd(sceneId, entity))
            {
                return false;
            }
            _toScene[entity] = sceneId;
            return true;
        }

        public bool Contains(ulong sceneId) => _toWorld.ContainsKey(sceneId);

        public ulong SceneIdOf(Entity entity) => _toScene[entity];

        public bool TryToScene(Entity entity, out ulong sceneId) => _toScene.TryGetValue(entity, out sceneId);

        public Entity ToWorld(ulong sceneId) => _toWorld.GetValueOrDefault(sceneId);
    }
}
