using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.Ecs;

/// <summary>
/// An ECS world (ADR 0012, 0023, 0035): entities whose blittable components
/// live in native chunks, read and written in place, and managed components
/// in C# pools. Disposing it destroys every entity. The name is a placeholder
/// (docs/naming.md).
/// </summary>
/// <remarks>
/// Like the native world: structural changes (create, destroy, add, remove,
/// SetParent) happen on one thread at a time and not during iteration; record
/// them in a <see cref="CommandBuffer"/> instead.
/// </remarks>
public sealed unsafe class World : IDisposable
{
    public World() => Handle = Api.CreateWorld();

    internal WorldHandle Handle { get; private set; }

    public void Dispose()
    {
        if (!Handle.IsNull)
        {
            Api.DestroyWorld(Handle);
            Handle = default;
        }
    }

    public int EntityCount => (int)Api.EntityCount(Live);

    public Entity CreateEntity(params ReadOnlySpan<ComponentId> components) =>
        Api.CreateEntity(Live, Ids(components));

    /// <summary>Destroys the entity and its descendants (ADR 0033).</summary>
    public void Destroy(Entity entity) => Api.DestroyEntity(Live, entity);

    public bool IsAlive(Entity entity) => Api.IsAlive(Live, entity);

    public void Add(Entity entity, ComponentId component) => Api.AddComponent(Live, entity, component.Value);

    public void Remove(Entity entity, ComponentId component) => Api.RemoveComponent(Live, entity, component.Value);

    public bool Has(Entity entity, ComponentId component) => Api.HasComponent(Live, entity, component.Value);

    /// <summary>
    /// The component's value in native memory, for reading and writing;
    /// stamps its change version (ADR 0034). Throws if the entity is dead or
    /// lacks it. Valid until the next structural change.
    /// </summary>
    public ref T Get<T>(Entity entity) where T : unmanaged
    {
        nint address = Api.WriteComponent(Live, entity, Components.Of<T>().Value);
        if (address == 0)
        {
            ThrowMissing<T>(entity);
        }
        return ref Unsafe.AsRef<T>((void*)address);
    }

    /// <summary>Like <see cref="Get{T}"/>, read-only: doesn't stamp the version.</summary>
    public ref readonly T Read<T>(Entity entity) where T : unmanaged
    {
        nint address = Api.ReadComponent(Live, entity, Components.Of<T>().Value);
        if (address == 0)
        {
            ThrowMissing<T>(entity);
        }
        return ref Unsafe.AsRef<T>((void*)address);
    }

    /// <summary>Adds the component if missing, then sets its value.</summary>
    public void Set<T>(Entity entity, in T value) where T : unmanaged
    {
        Add(entity, Components.Of<T>());
        Get<T>(entity) = value;
    }

    /// <summary>Sets a managed component's value, adding the component if missing (ADR 0035).</summary>
    public void SetManaged<T>(Entity entity, T value)
    {
        ComponentId component = Components.Of<T>();
        Add(entity, component);
        int* slot = (int*)Api.WriteComponent(Live, entity, component.Value);
        if (slot == null)
        {
            ThrowMissing<T>(entity);
        }
        if (*slot == 0)
        {
            *slot = ManagedPool<T>.Instance.Allocate(value) + 1;
        }
        else
        {
            ManagedPool<T>.Instance.Get(*slot - 1) = value;
        }
    }

    /// <summary>A managed component's value; throws if the entity lacks it.</summary>
    public ref T GetManaged<T>(Entity entity)
    {
        int* slot = (int*)Api.ReadComponent(Live, entity, Components.Of<T>().Value);
        if (slot == null || *slot == 0)
        {
            ThrowMissing<T>(entity);
        }
        return ref ManagedPool<T>.Instance.Get(*slot - 1);
    }

    /// <summary>
    /// Makes <paramref name="child"/> a child of <paramref name="parent"/> at
    /// <paramref name="index"/> (default last), or a root for
    /// <see cref="Entity.Null"/>. Throws for dead entities and cycles.
    /// </summary>
    public void SetParent(Entity child, Entity parent, uint index = uint.MaxValue) =>
        Api.SetParent(Live, child, parent, index);

    public Entity ParentOf(Entity child) => Api.ParentOf(Live, child);

    /// <summary>Copies up to children.Length children, in order; returns how many there are.</summary>
    public int ChildrenOf(Entity parent, Span<Entity> children) => (int)Api.ChildrenInto(Live, parent, children);

    /// <summary>Starts a new change version (ADR 0034) and returns it.</summary>
    public uint AdvanceVersion() => Api.AdvanceVersion(Live);

    public uint CurrentVersion => Api.CurrentVersion(Live);

    public Query CreateQuery(ReadOnlySpan<ComponentId> required, ReadOnlySpan<ComponentId> excluded = default) =>
        new(this, Api.CreateQueryFrom(Live, Ids(required), Ids(excluded)));

    public CommandBuffer CreateCommandBuffer() => new(this, Api.CreateCommandBuffer(Live));

    internal WorldHandle Live => Handle.IsNull ? throw new ObjectDisposedException(nameof(World)) : Handle;

    // ComponentId is one uint, so a span of them is a span of native IDs.
    internal static ReadOnlySpan<uint> Ids(ReadOnlySpan<ComponentId> components) =>
        MemoryMarshal.Cast<ComponentId, uint>(components);

    private static void ThrowMissing<T>(Entity entity) =>
        throw new InvalidOperationException($"{entity} is dead or has no {typeof(T).Name}");
}
