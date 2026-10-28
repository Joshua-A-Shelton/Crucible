using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// An entity's ID inside scene files (ADR 0041): random, kept across saves,
/// so references and diffs stay put. Saving gives one to entities without it.
/// </summary>
public struct SceneEntityId
{
    public ulong Value;
}

/// <summary>What loading a scene made.</summary>
/// <param name="Id">The scene's own asset ID.</param>
/// <param name="Roots">The entities it created at the top of the hierarchy, in file order.</param>
/// <param name="SkippedComponents">Component names in the file this build doesn't save, which were left out.</param>
public sealed record SceneLoad(AssetId Id, IReadOnlyList<Entity> Roots, IReadOnlyList<string> SkippedComponents);

/// <summary>
/// Scenes (ADR 0021, 0041): a world's entities as authored JSON, with their
/// saved components and their hierarchy, children nested in order.
/// <code>
/// {
///   "$guid": "…",
///   "Entities": [
///     { "Id": "6c1e9a03d7b4f210", "Components": { "Game.Health": { "Value": 10 } }, "Children": [ … ] }
///   ]
/// }
/// </code>
/// Only <c>[Component, Serialized]</c> types are saved. Entity references in
/// components are written as scene IDs and mapped back on load. Names are
/// placeholders (docs/naming.md).
/// </summary>
public static class Scene
{
    public const string Extension = ".scene";

    /// <summary>The property holding the scene's own asset ID, written first (ADR 0021: Crucible's files keep their GUID inside).</summary>
    public const string GuidProperty = "$guid";

    private static ComponentId IdComponent => Components.Register<SceneEntityId>();

    // ---------------------------------------------------------------------
    // Saving
    // ---------------------------------------------------------------------

    /// <summary>Writes every entity in the world as the scene <paramref name="id"/>.</summary>
    /// <remarks>
    /// Entities without a scene ID get one, so call it between frames: it is
    /// a structural change.
    /// </remarks>
    public static byte[] Save(World world, AssetId id)
    {
        ComponentId idComponent = IdComponent;
        List<(Entity Entity, ComponentId[] Components)> contents = world.Contents();
        SavedComponent[] sparse =
            [.. SavedComponents.All().Where(saved => (saved.Id.Value & 0x8000_0000u) != 0)];

        // Scene IDs: kept where they exist and are unique, new otherwise.
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
                sceneId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
            } while (!map.TryAdd(entity, sceneId));
            world.Set(entity, new SceneEntityId { Value = sceneId });
        }

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

        ArrayBufferWriter<byte> buffer = new();
        using (EntityReferences.Use(map))
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(GuidProperty);
            JsonValues.WriteAssetId(writer, id);
            writer.WriteStartArray("Entities"u8);
            Entity[] children = new Entity[16];
            IEnumerable<Entity> roots = contents.Select(item => item.Entity)
                .Where(entity => world.ParentOf(entity).IsNull)
                .OrderBy(entity => map.SceneIdOf(entity));
            foreach (Entity root in roots)
            {
                WriteEntity(writer, world, root, map, saved, ref children);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteEntity(Utf8JsonWriter writer, World world, Entity entity, Map map,
        Dictionary<Entity, SavedComponent[]> saved, ref Entity[] children)
    {
        writer.WriteStartObject();
        writer.WriteString("Id"u8, map.SceneIdOf(entity).ToString("x16", CultureInfo.InvariantCulture));
        writer.WriteStartObject("Components"u8);
        foreach (SavedComponent component in saved[entity])
        {
            writer.WritePropertyName(component.Name);
            component.Write(writer, world, entity);
        }
        writer.WriteEndObject();

        int count = world.ChildrenOf(entity, children);
        if (count > children.Length)
        {
            children = new Entity[count];
            count = world.ChildrenOf(entity, children);
        }
        if (count > 0)
        {
            Entity[] mine = children[..count]; // the buffer is reused below
            writer.WriteStartArray("Children"u8);
            foreach (Entity child in mine)
            {
                WriteEntity(writer, world, child, map, saved, ref children);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    // ---------------------------------------------------------------------
    // Loading
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates the scene's entities in <paramref name="world"/>, beside any it
    /// has. With <paramref name="newIds"/>, they get new scene IDs (loading
    /// one file twice into a world, as instancing will).
    /// </summary>
    public static SceneLoad Load(World world, ReadOnlySpan<byte> json, bool newIds = false)
    {
        ComponentId idComponent = IdComponent;
        Map map = new();
        List<Entity> roots = [];
        AssetId sceneId = AssetId.None;

        // Pass 1: every entity and the hierarchy, so references in pass 2
        // can name entities anywhere in the file.
        Utf8JsonReader reader = new(json);
        ForEachEntity(ref reader, ref sceneId, (ref Utf8JsonReader entityReader, Entity parent) =>
        {
            ulong fileId = ReadId(ref entityReader);
            if (map.HasFileId(fileId))
            {
                throw JsonValues.Error(entityReader, $"two entities have the ID {fileId:x16}");
            }
            Entity entity = world.CreateEntity(idComponent);
            ulong id = fileId;
            while (!map.TryAddFileId(fileId, entity, id))
            {
                id = (ulong)Random.Shared.NextInt64(1, long.MaxValue); // only with newIds can it collide
            }
            if (newIds)
            {
                // A fresh ID, so a second copy of the scene doesn't share them.
                ulong fresh;
                do
                {
                    fresh = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
                } while (!map.TryRenumber(entity, fresh));
                id = fresh;
            }
            world.Set(entity, new SceneEntityId { Value = id });
            if (parent.IsNull)
            {
                roots.Add(entity);
            }
            else
            {
                world.SetParent(entity, parent);
            }
            return entity;
        });

        // Pass 2: components, with entity references mapped.
        List<string> skipped = [];
        AssetId ignored = AssetId.None;
        reader = new Utf8JsonReader(json);
        using (EntityReferences.Use(map))
        {
            ForEachEntity(ref reader, ref ignored, (ref Utf8JsonReader entityReader, Entity _) =>
            {
                Entity entity = map.ToWorldFromFile(ReadId(ref entityReader));
                ReadComponents(ref entityReader, world, entity, skipped);
                return entity;
            });
        }
        return new SceneLoad(sceneId, roots, [.. skipped.Distinct()]);
    }

    /// <summary>A scene file's own asset ID, from its first property; None if it has none.</summary>
    public static AssetId ReadId(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject || !reader.Read() ||
            reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(GuidProperty) || !reader.Read())
        {
            return AssetId.None;
        }
        return reader.TokenType == JsonTokenType.String && AssetId.TryParse(reader.GetString(), out AssetId id)
            ? id
            : AssetId.None;
    }

    private delegate Entity EntityVisitor(ref Utf8JsonReader reader, Entity parent);

    // Walks the file, calling the visitor at each entity object (reader on
    // its StartObject; the visitor reads what it needs from a copy), then
    // descending into its children with the entity as their parent.
    private static void ForEachEntity(ref Utf8JsonReader reader, ref AssetId sceneId, EntityVisitor visit)
    {
        reader.Read();
        JsonValues.Expect(reader, JsonTokenType.StartObject, "a scene object");
        bool sawEntities = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals(GuidProperty))
            {
                reader.Read();
                sceneId = JsonValues.ReadAssetId(ref reader);
            }
            else if (reader.ValueTextEquals("Entities"u8))
            {
                reader.Read();
                ReadEntityList(ref reader, Entity.Null, visit);
                sawEntities = true;
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
        if (!sawEntities)
        {
            throw new JsonException("Scene: no Entities list");
        }
    }

    private static void ReadEntityList(ref Utf8JsonReader reader, Entity parent, EntityVisitor visit)
    {
        JsonValues.Expect(reader, JsonTokenType.StartArray, "a list of entities");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            JsonValues.Expect(reader, JsonTokenType.StartObject, "an entity object");
            Utf8JsonReader copy = reader;
            Entity entity = visit(ref copy, parent);
            // Then the children, from the original position.
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals("Children"u8))
                {
                    reader.Read();
                    ReadEntityList(ref reader, entity, visit);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
    }

    // An entity's "Id", read from a copy of the reader at its StartObject.
    private static ulong ReadId(ref Utf8JsonReader reader)
    {
        Utf8JsonReader probe = reader;
        while (probe.Read() && probe.TokenType != JsonTokenType.EndObject)
        {
            bool isId = probe.ValueTextEquals("Id"u8);
            probe.Read();
            if (isId)
            {
                return probe.TokenType == JsonTokenType.String &&
                       ulong.TryParse(probe.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id) &&
                       id != 0
                    ? id
                    : throw JsonValues.Error(probe, "an entity's Id is 16 hex digits");
            }
            probe.Skip();
        }
        throw JsonValues.Error(reader, "an entity has no Id");
    }

    private static void ReadComponents(ref Utf8JsonReader reader, World world, Entity entity, List<string> skipped)
    {
        Utf8JsonReader probe = reader;
        while (probe.Read() && probe.TokenType != JsonTokenType.EndObject)
        {
            bool isComponents = probe.ValueTextEquals("Components"u8);
            probe.Read();
            if (!isComponents)
            {
                probe.Skip();
                continue;
            }
            JsonValues.Expect(probe, JsonTokenType.StartObject, "an object of components");
            while (probe.Read() && probe.TokenType != JsonTokenType.EndObject)
            {
                string name = probe.GetString()!;
                probe.Read();
                if (SavedComponents.Find(name) is not { } component)
                {
                    skipped.Add(name);
                    probe.Skip();
                    continue;
                }
                try
                {
                    component.Read(ref probe, world, entity);
                }
                catch (JsonException exception)
                {
                    throw new JsonException($"{name}: {exception.Message}", exception);
                }
            }
            return;
        }
    }

    // Entities and their scene IDs, both ways; while loading, also the
    // file's IDs (which differ when the scene is loaded with new IDs).
    private sealed class Map : IEntityMap
    {
        private readonly Dictionary<Entity, ulong> _toScene = [];
        private readonly HashSet<ulong> _sceneIds = [];
        private readonly Dictionary<ulong, Entity> _fromFile = [];

        // Saving: the entity keeps sceneId, unless another already has it.
        public bool TryAdd(Entity entity, ulong sceneId)
        {
            if (!_sceneIds.Add(sceneId))
            {
                return false;
            }
            _fromFile[sceneId] = entity;
            _toScene[entity] = sceneId;
            return true;
        }

        // Loading: the file's ID maps to the entity, which gets sceneId.
        public bool TryAddFileId(ulong fileId, Entity entity, ulong sceneId)
        {
            if (_fromFile.ContainsKey(fileId) || !_sceneIds.Add(sceneId))
            {
                return false;
            }
            _fromFile[fileId] = entity;
            _toScene[entity] = sceneId;
            return true;
        }

        public bool HasFileId(ulong fileId) => _fromFile.ContainsKey(fileId);

        // Gives an entity another scene ID, keeping its file ID's mapping.
        public bool TryRenumber(Entity entity, ulong sceneId)
        {
            if (!_sceneIds.Add(sceneId))
            {
                return false;
            }
            _sceneIds.Remove(_toScene[entity]);
            _toScene[entity] = sceneId;
            return true;
        }

        public ulong SceneIdOf(Entity entity) => _toScene[entity];

        public Entity ToWorldFromFile(ulong fileId) => _fromFile[fileId];

        public bool TryToScene(Entity entity, out ulong sceneId) => _toScene.TryGetValue(entity, out sceneId);

        public Entity ToWorld(ulong sceneId) => _fromFile.GetValueOrDefault(sceneId);
    }
}
