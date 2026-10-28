using System.ComponentModel;
using System.Runtime.Loader;
using System.Text.Json;
using Crucible.Serialization;

namespace Crucible.Ecs;

/// <summary>
/// The component types scenes save (ADR 0041): each <c>[Component]</c> that
/// is also <c>[Serialized]</c>, added by the generated registration. Public
/// because that code lives in game assemblies; not meant to be called by
/// hand.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SavedComponents
{
    private static readonly Dictionary<ComponentId, SavedComponent> _byId = [];
    private static readonly Dictionary<string, SavedComponent> _byName = new(StringComparer.Ordinal);
    private static readonly Lock _lock = new();

    public static void Add<T>() where T : unmanaged, IJsonSerialized<T> => Remember(new Blittable<T>(Components.Of<T>()));

    public static void AddTag<T>() where T : unmanaged => Remember(new Tag(Components.Of<T>(), typeof(T)));

    public static void AddManaged<T>() where T : IJsonSerialized<T> => Remember(new Managed<T>(Components.Of<T>()));

    internal static SavedComponent? Find(ComponentId id)
    {
        lock (_lock)
        {
            return _byId.GetValueOrDefault(id);
        }
    }

    /// <summary>By the name scenes store: the component's registered name (its type's full name).</summary>
    internal static SavedComponent? Find(string name)
    {
        lock (_lock)
        {
            return _byName.GetValueOrDefault(name);
        }
    }

    internal static SavedComponent[] All()
    {
        lock (_lock)
        {
            return [.. _byId.Values];
        }
    }

    /// <summary>
    /// Drops the entries for types in an unloading game: they would keep it
    /// loaded (ADR 0011's hot-reload rule). The reloaded version adds its own.
    /// </summary>
    internal static void Forget(AssemblyLoadContext context)
    {
        lock (_lock)
        {
            foreach (SavedComponent saved in _byId.Values.Where(saved => AssemblyLoadContext.GetLoadContext(saved.Type.Assembly) == context).ToArray())
            {
                _byId.Remove(saved.Id);
                _byName.Remove(saved.Name);
            }
        }
    }

    private static void Remember(SavedComponent saved)
    {
        lock (_lock)
        {
            if (_byName.Remove(saved.Name, out SavedComponent? old))
            {
                _byId.Remove(old.Id);
            }
            _byId[saved.Id] = saved;
            _byName[saved.Name] = saved;
        }
    }

    private sealed class Blittable<T>(ComponentId id) : SavedComponent(id, typeof(T))
        where T : unmanaged, IJsonSerialized<T>
    {
        public override void Write(Utf8JsonWriter writer, World world, Entity entity) =>
            T.WriteJson(writer, world.Read<T>(entity));

        public override void Read(ref Utf8JsonReader reader, World world, Entity entity) =>
            world.Set(entity, T.ReadJson(ref reader));
    }

    private sealed class Tag(ComponentId id, Type type) : SavedComponent(id, type)
    {
        public override void Write(Utf8JsonWriter writer, World world, Entity entity)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        public override void Read(ref Utf8JsonReader reader, World world, Entity entity)
        {
            reader.Skip();
            world.Add(entity, Id);
        }
    }

    private sealed class Managed<T>(ComponentId id) : SavedComponent(id, typeof(T)) where T : IJsonSerialized<T>
    {
        public override void Write(Utf8JsonWriter writer, World world, Entity entity)
        {
            if (world.TryGetManaged(entity, out T? value) && value is not null)
            {
                T.WriteJson(writer, value);
            }
            else
            {
                writer.WriteNullValue(); // added, never set
            }
        }

        public override void Read(ref Utf8JsonReader reader, World world, Entity entity)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                world.Add(entity, Id);
                return;
            }
            world.SetManaged(entity, T.ReadJson(ref reader));
        }
    }
}

/// <summary>How one saved component type is written and read.</summary>
internal abstract class SavedComponent(ComponentId id, Type type)
{
    public ComponentId Id { get; } = id;

    public Type Type { get; } = type;

    public string Name { get; } = Components.NameOf(type);

    public abstract void Write(Utf8JsonWriter writer, World world, Entity entity);

    /// <summary>Reads the value at the reader's current token and sets it on the entity.</summary>
    public abstract void Read(ref Utf8JsonReader reader, World world, Entity entity);
}
