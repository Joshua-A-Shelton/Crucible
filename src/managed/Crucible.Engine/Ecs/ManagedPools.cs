using System.Runtime.InteropServices;

namespace Crucible.Ecs;

// Values of managed components (ADR 0012, 0035): one pool per type; a chunk
// column holds slot + 1 (0 is empty), so native code never holds a reference.
internal interface IManagedPool
{
    void Release(int slot);

    // Hot reload (ADR 0035): every slot's value, boxed, and the free slots, so
    // a new version's pool can take the same slots (chunks hold slot numbers).
    object?[] Export(out int[] free);

    void Import(object?[] values, int[] free);
}

internal sealed class ManagedPool<T> : IManagedPool
{
    // Static per type: for a game's type, an instantiation over a collectible
    // type, so it unloads with the game.
    public static readonly ManagedPool<T> Instance = new();

    private readonly Lock _lock = new();
    private T[] _values = new T[16];
    private int[] _free = new int[16];
    private int _freeCount;
    private int _used;

    public int Allocate(T value)
    {
        lock (_lock)
        {
            int slot;
            if (_freeCount > 0)
            {
                slot = _free[--_freeCount];
            }
            else
            {
                if (_used == _values.Length)
                {
                    Array.Resize(ref _values, _values.Length * 2);
                }
                slot = _used++;
            }
            _values[slot] = value;
            return slot;
        }
    }

    // Valid until the pool grows, so for immediate use, as with Span.
    public ref T Get(int slot) => ref _values[slot];

    public void Release(int slot)
    {
        lock (_lock)
        {
            _values[slot] = default!; // drop the reference for the GC
            if (_freeCount == _free.Length)
            {
                Array.Resize(ref _free, _free.Length * 2);
            }
            _free[_freeCount++] = slot;
        }
    }

    public object?[] Export(out int[] free)
    {
        lock (_lock)
        {
            free = _free[.._freeCount];
            object?[] values = new object?[_used];
            for (int slot = 0; slot < _used; ++slot)
            {
                values[slot] = _values[slot];
            }
            return values;
        }
    }

    public void Import(object?[] values, int[] free)
    {
        lock (_lock)
        {
            _values = new T[Math.Max(16, values.Length)];
            for (int slot = 0; slot < values.Length; ++slot)
            {
                _values[slot] = values[slot] is T value ? value : default!;
            }
            _used = values.Length;
            _free = new int[Math.Max(16, free.Length)];
            free.CopyTo(_free, 0);
            _freeCount = free.Length;
        }
    }

    // Slots in use, for tests.
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _used - _freeCount;
            }
        }
    }
}

internal static unsafe class ManagedPools
{
    // Weak, so this engine-side table never keeps a game's pool (and the
    // game) alive across an unload.
    private static readonly Dictionary<uint, WeakReference<IManagedPool>> _pools = [];
    private static readonly Lock _lock = new();
    private static int _callbackSet;

    public static void Add(ComponentId component, IManagedPool pool)
    {
        lock (_lock)
        {
            _pools[component.Value] = new WeakReference<IManagedPool>(pool);
        }
    }

    public static IManagedPool? Get(ComponentId component)
    {
        lock (_lock)
        {
            return _pools.TryGetValue(component.Value, out WeakReference<IManagedPool>? reference) &&
                   reference.TryGetTarget(out IManagedPool? pool)
                ? pool
                : null;
        }
    }

    public static void EnsureCallback()
    {
        if (Interlocked.Exchange(ref _callbackSet, 1) == 0)
        {
            Api.SetManagedReleaseCallback((ulong)(delegate* unmanaged<uint, int, void>)&Release);
        }
    }

    // Called by native code for every managed slot the ECS drops: removed
    // component, destroyed entity, or destroyed world.
    [UnmanagedCallersOnly]
    private static void Release(uint component, int slotPlusOne)
    {
        IManagedPool? pool = null;
        lock (_lock)
        {
            if (_pools.TryGetValue(component, out WeakReference<IManagedPool>? reference))
            {
                reference.TryGetTarget(out pool);
            }
        }
        pool?.Release(slotPlusOne - 1);
    }
}
