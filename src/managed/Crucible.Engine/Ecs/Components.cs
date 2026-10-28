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
        if (ComponentType<T>.Id is { } existing)
        {
            return existing;
        }
        ManagedPools.EnsureCallback();
        ComponentId id = RegisterNative<T>(sizeof(int), sizeof(int), ComponentStorage.Managed);
        ManagedPools.Add(id, ManagedPool<T>.Instance);
        ComponentType<T>.Id = id;
        return id;
    }

    /// <summary>T's ID; throws if T isn't registered.</summary>
    public static ComponentId Of<T>() =>
        ComponentType<T>.Id ?? throw new InvalidOperationException($"{typeof(T)} isn't a registered component");

    private static ComponentId Register<T>(ComponentStorage storage, bool tag) where T : unmanaged
    {
        if (ComponentType<T>.Id is { } existing)
        {
            return existing;
        }
        uint size = tag ? 0u : (uint)sizeof(T);
        ComponentId id = RegisterNative<T>(size, AlignmentOf<T>(), storage);
        ComponentType<T>.Id = id;
        return id;
    }

    private static ComponentId RegisterNative<T>(uint size, uint alignment, ComponentStorage storage) =>
        new(Api.RegisterComponent(typeof(T).FullName ?? typeof(T).Name, size, alignment, storage));

    // The CLR has no alignof; a struct after a byte lands on its alignment.
    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        public byte Padding;
        public T Value;
    }

    private static uint AlignmentOf<T>() where T : unmanaged =>
        (uint)(sizeof(AlignmentProbe<T>) - sizeof(T));
}

// Each type's ID, cached where generic code finds it without a lookup. For a
// game's type this is an instantiation over a collectible type, so it
// unloads with the game.
internal static class ComponentType<T>
{
    public static ComponentId? Id;
}
