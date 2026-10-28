using System.Runtime.InteropServices;

namespace Crucible.Ecs;

/// <summary>
/// Registers C# component types with the native ECS (ADR 0035): explicitly,
/// once per type, before use. A source generator can emit these calls later.
/// Names are placeholders (docs/naming.md).
/// </summary>
public static unsafe class Components
{
    /// <summary>
    /// A blittable component, stored in native chunks and read in place
    /// (ADR 0012). Registering again returns the same ID.
    /// </summary>
    public static ComponentId Register<T>() where T : unmanaged =>
        Register<T>(ComponentStorage.Archetype, tag: false);

    /// <summary>
    /// A tag: a component with no data, only membership (an empty struct).
    /// Registered explicitly because C# gives an empty struct a size of 1, the
    /// same as a struct with one byte.
    /// </summary>
    public static ComponentId RegisterTag<T>() where T : unmanaged => Register<T>(ComponentStorage.Archetype, tag: true);

    /// <summary>A blittable component toggled often, kept in a sparse set (ADR 0023).</summary>
    public static ComponentId RegisterSparse<T>() where T : unmanaged =>
        Register<T>(ComponentStorage.Sparse, tag: false);

    /// <summary>
    /// A C# class, or a struct holding references (ADR 0012, 0035): the value
    /// lives in a C# pool and the chunk holds its slot.
    /// </summary>
    public static ComponentId RegisterManaged<T>()
    {
        if (Existing<T>(ComponentKind.Managed) is { } existing)
        {
            return existing;
        }
        ManagedPools.EnsureCallback();
        ComponentId id = RegisterNative<T>(sizeof(int), sizeof(int), ComponentStorage.Managed);
        ManagedPools.Add(id, ManagedPool<T>.Instance);
        Remember<T>(id, ComponentKind.Managed);
        return id;
    }

    /// <summary>T's ID; throws if T isn't registered.</summary>
    public static ComponentId Of<T>() =>
        ComponentType<T>.Id ?? throw new InvalidOperationException($"{typeof(T)} isn't a registered component");

    private static ComponentId Register<T>(ComponentStorage storage, bool tag) where T : unmanaged
    {
        ComponentKind kind = tag ? ComponentKind.Tag : storage == ComponentStorage.Sparse ? ComponentKind.Sparse : ComponentKind.Archetype;
        if (Existing<T>(kind) is { } existing)
        {
            return existing;
        }
        uint size = tag ? 0u : (uint)sizeof(T);
        ComponentId id = RegisterNative<T>(size, AlignmentOf<T>(), storage);
        Remember<T>(id, kind);
        return id;
    }

    // T's ID if it has one: registered before, or carried over by a hot
    // reload, which can't see which Register call the new version makes.
    private static ComponentId? Existing<T>(ComponentKind kind)
    {
        if (ComponentType<T>.Id is not { } existing)
        {
            return null;
        }
        if (ComponentType<T>.Kind != kind)
        {
            throw new InvalidOperationException(
                $"{typeof(T)} is registered as {ComponentType<T>.Kind}, not {kind}: a component's storage can't " +
                "change while the game runs, across a hot reload either; restart to change it");
        }
        return existing;
    }

    private static void Remember<T>(ComponentId id, ComponentKind kind)
    {
        ComponentType<T>.Kind = kind;
        ComponentType<T>.Id = id;
        lock (_lock)
        {
            // Weak, so the table never keeps a game's type (and the game) loaded.
            _registered[NameOf(typeof(T))] =
                new Registration(id, kind, typeof(T).IsCollectible ? new WeakReference<Type>(typeof(T)) : null);
        }
    }

    private static ComponentId RegisterNative<T>(uint size, uint alignment, ComponentStorage storage) =>
        new(Api.RegisterComponent(NameOf(typeof(T)), size, alignment, storage));

    internal static string NameOf(Type type) => type.FullName ?? type.Name;

    // Every registered component by name, for hot reload (ADR 0035).
    private static readonly Dictionary<string, Registration> _registered = [];
    private static readonly Lock _lock = new();

    internal static KeyValuePair<string, Registration>[] Registered()
    {
        lock (_lock)
        {
            return [.. _registered];
        }
    }

    // Hot reload: the component's name now means `id`, or nothing (null).
    internal static void Replace(string name, Registration? registration)
    {
        lock (_lock)
        {
            if (registration is { } value)
            {
                _registered[name] = value;
            }
            else
            {
                _registered.Remove(name);
            }
        }
    }

    // The CLR has no alignof; a struct after a byte lands on its alignment.
    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        public byte Padding;
        public T Value;
    }

    internal static uint AlignmentOf<T>() where T : unmanaged =>
        (uint)(sizeof(AlignmentProbe<T>) - sizeof(T));
}

// How a component was registered: hot reload keeps it (ADR 0035).
internal enum ComponentKind
{
    Archetype,
    Tag,
    Sparse,
    Managed,
}

// Type is set for types that can unload: the ones hot reload migrates.
internal readonly record struct Registration(ComponentId Id, ComponentKind Kind, WeakReference<Type>? Type);

// Each type's ID, cached where generic code finds it without a lookup. For a
// game's type this is an instantiation over a collectible type, so it
// unloads with the game.
internal static class ComponentType<T>
{
    public static ComponentId? Id;
    public static ComponentKind Kind;
}
