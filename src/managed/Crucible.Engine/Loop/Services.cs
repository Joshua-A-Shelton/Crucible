using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Crucible.Serialization;

namespace Crucible.Loop;

/// <summary>
/// Marks a plain C# service (ADR 0012, 0041): game rules, bookkeeping, or
/// flow that isn't about world entities, such as an inventory or a quest log.
/// The generated registration creates one with its parameterless
/// constructor; systems find it by type through
/// <see cref="FrameContext.Services"/>. A service that is also
/// <c>[Serialized]</c> is saved with the game's state. Names are placeholders
/// (docs/naming.md).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute;

/// <summary>An assembly's services, created and added (ADR 0041); the generator implements it.</summary>
public interface IServiceRegistration
{
    void Register(Services services);
}

/// <summary>Names an assembly's <see cref="IServiceRegistration"/>, for the game loader (ADR 0011).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ServiceRegistrationAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type registrationType)
    : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type RegistrationType { get; } = registrationType;
}

/// <summary>
/// A game's services, one per type (ADR 0041). Found through a per-type
/// index into an array, so <see cref="Get{T}"/> is a bounds check and a
/// cast, with no dictionary and no allocation.
/// </summary>
public sealed class Services
{
    private static int _nextIndex;
    private object?[] _items = new object?[8];
    private readonly List<SavedService> _saved = [];

    // Each service type's index, the same in every Services.
    private static class Index<T>
    {
        public static readonly int Value = Interlocked.Increment(ref _nextIndex) - 1;
    }

    /// <summary>Adds the service, replacing one of the same type.</summary>
    public void Add<T>(T service) where T : class
    {
        int index = Index<T>.Value;
        if (index >= _items.Length)
        {
            Array.Resize(ref _items, Math.Max(index + 1, _items.Length * 2));
        }
        _items[index] = service;
    }

    /// <summary>The service of type T; throws if there is none.</summary>
    public T Get<T>() where T : class =>
        TryGet(out T? service)
            ? service
            : throw new InvalidOperationException($"There is no {typeof(T).Name} service: is it marked [Service]?");

    public bool TryGet<T>([NotNullWhen(true)] out T? service) where T : class
    {
        int index = Index<T>.Value;
        service = index < _items.Length ? _items[index] as T : null;
        return service is not null;
    }

    /// <summary>Includes T in <see cref="Save"/> and <see cref="Load"/>; the generated registration calls it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddSaved<T>() where T : class, IJsonSerialized<T>
    {
        string name = typeof(T).FullName ?? typeof(T).Name;
        _saved.RemoveAll(saved => saved.Name == name);
        _saved.Add(new SavedService<T>(name));
    }

    /// <summary>
    /// The saved services' state as one JSON object, keyed by type name in
    /// ordinal order: <c>{ "Game.Inventory": { ... }, ... }</c>.
    /// </summary>
    public byte[] Save()
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            foreach (SavedService saved in _saved.OrderBy(saved => saved.Name, StringComparer.Ordinal))
            {
                if (saved.Has(this))
                {
                    writer.WritePropertyName(saved.Name);
                    saved.Write(writer, this);
                }
            }
            writer.WriteEndObject();
        }
        stream.Write("\n"u8);
        return stream.ToArray();
    }

    /// <summary>
    /// Replaces the saved services with ones read from <paramref name="json"/>
    /// (find services through the context each time rather than keeping them
    /// across a load). Returns the names it didn't know, which are skipped.
    /// </summary>
    public IReadOnlyList<string> Load(ReadOnlySpan<byte> json)
    {
        List<string> unknown = [];
        Utf8JsonReader reader = new(json);
        reader.Read();
        JsonValues.Expect(reader, JsonTokenType.StartObject, "an object of services");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            SavedService? saved = _saved.Find(candidate => candidate.Name == name);
            if (saved is null)
            {
                unknown.Add(name);
                reader.Skip();
                continue;
            }
            try
            {
                saved.Read(ref reader, this);
            }
            catch (JsonException exception)
            {
                throw new JsonException($"{name}: {exception.Message}", exception);
            }
        }
        return unknown;
    }

    private abstract class SavedService(string name)
    {
        public string Name { get; } = name;

        public abstract bool Has(Services services);

        public abstract void Write(Utf8JsonWriter writer, Services services);

        public abstract void Read(ref Utf8JsonReader reader, Services services);
    }

    private sealed class SavedService<T>(string name) : SavedService(name) where T : class, IJsonSerialized<T>
    {
        public override bool Has(Services services) => services.TryGet(out T? _);

        public override void Write(Utf8JsonWriter writer, Services services) => T.WriteJson(writer, services.Get<T>());

        public override void Read(ref Utf8JsonReader reader, Services services) => services.Add(T.ReadJson(ref reader));
    }
}
