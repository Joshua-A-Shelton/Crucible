namespace Crucible.Ecs;

/// <summary>
/// Structural changes recorded while queries iterate a world, applied later
/// at a sync point (ADR 0013, 0023). Recording is thread-safe, from chunk jobs
/// too. The name is a placeholder (docs/naming.md).
/// </summary>
public sealed unsafe class CommandBuffer : IDisposable
{
    private readonly World _world;
    private CommandBufferHandle _handle;

    internal CommandBuffer(World world, CommandBufferHandle handle)
    {
        _world = world;
        _handle = handle;
    }

    /// <summary>Discards unapplied commands, releasing reserved entities.</summary>
    public void Dispose()
    {
        if (!_handle.IsNull)
        {
            Api.DestroyCommandBuffer(_handle);
            _handle = default;
        }
    }

    /// <summary>
    /// The entity is reserved now and final: it can be stored before the
    /// buffer is applied, and isn't alive until then.
    /// </summary>
    public Entity RecordCreate(params ReadOnlySpan<ComponentId> components) =>
        Api.RecordCreate(Live, World.Ids(components));

    public void RecordDestroy(Entity entity) => Api.RecordDestroy(Live, entity);

    public void RecordAdd(Entity entity, ComponentId component) => Api.RecordAdd(Live, entity, component.Value);

    public void RecordRemove(Entity entity, ComponentId component) => Api.RecordRemove(Live, entity, component.Value);

    /// <summary>Copies the value now; adds the component if missing when applied.</summary>
    public void RecordSet<T>(Entity entity, in T value) where T : unmanaged
    {
        fixed (T* pointer = &value)
        {
            Api.RecordSet(Live, entity, Components.Of<T>().Value, (nint)pointer);
        }
    }

    public void RecordSetParent(Entity child, Entity parent, uint index = uint.MaxValue) =>
        Api.RecordSetParent(Live, child, parent, index);

    /// <summary>Applies and clears the buffer; the world must not be iterating.</summary>
    public void Apply()
    {
        _ = _world.Live;
        Api.Apply(Live);
    }

    public void Clear() => Api.Clear(Live);

    internal CommandBufferHandle Handle => Live;

    private CommandBufferHandle Live =>
        _handle.IsNull ? throw new ObjectDisposedException(nameof(CommandBuffer)) : _handle;
}
