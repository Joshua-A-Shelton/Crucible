using System.Runtime.CompilerServices;
using Crucible.Jobs;

namespace Crucible.Ecs;

/// <summary>
/// A chunk job (ADR 0035): a struct whose <see cref="Execute"/> runs once per
/// chunk a query matches, sequentially or as parallel jobs. The name is a
/// placeholder (docs/naming.md).
/// </summary>
/// <remarks>
/// In parallel, chunks run at the same time on one copy of the struct: write
/// through the chunk's spans, not the struct's own fields.
/// </remarks>
public interface IChunkJob
{
    void Execute(in ChunkView chunk);
}

/// <summary>
/// One chunk during iteration: <see cref="Count"/> entities and their columns
/// in native memory (ADR 0023). Valid only inside <see cref="IChunkJob.Execute"/>.
/// </summary>
public readonly unsafe ref struct ChunkView
{
    private readonly WorldHandle _world;
    private readonly ChunkHandle _chunk;

    internal ChunkView(WorldHandle world, ChunkHandle chunk)
    {
        _world = world;
        _chunk = chunk;
        Count = (int)Api.ChunkEntityCount(chunk);
    }

    public int Count { get; }

    public ReadOnlySpan<Entity> Entities => new((void*)Api.ChunkEntities(_chunk), Count);

    public bool Has(ComponentId component) => Api.ChunkColumn(_world, _chunk, component.Value, false) != 0;

    /// <summary>The column read-only; empty if the chunk lacks it.</summary>
    public ReadOnlySpan<T> Read<T>() where T : unmanaged
    {
        nint column = Api.ChunkColumn(_world, _chunk, Components.Of<T>().Value, false);
        return column == 0 ? default : new ReadOnlySpan<T>((void*)column, Count);
    }

    /// <summary>The column writable, stamping its change version (ADR 0034); empty if the chunk lacks it.</summary>
    public Span<T> Write<T>() where T : unmanaged
    {
        nint column = Api.ChunkColumn(_world, _chunk, Components.Of<T>().Value, true);
        return column == 0 ? default : new Span<T>((void*)column, Count);
    }

    /// <summary>Whether the column was written, or the chunk's entities changed, after <paramref name="version"/>.</summary>
    public bool ChangedSince(ComponentId component, uint version) =>
        Api.ChunkChangedSince(_chunk, component.Value, version);
}

/// <summary>
/// The entities with all of some components and none of others (ADR 0023),
/// iterated chunk by chunk. Tracks archetypes created after it. The name is a
/// placeholder (docs/naming.md).
/// </summary>
public sealed class Query : IDisposable
{
    private readonly World _world;
    private QueryHandle _handle;
    // Reused, so iterating allocates nothing once warm.
    private ulong[] _chunks = new ulong[64];

    internal Query(World world, QueryHandle handle)
    {
        _world = world;
        _handle = handle;
    }

    public void Dispose()
    {
        if (!_handle.IsNull)
        {
            Api.DestroyQuery(_handle);
            _handle = default;
        }
    }

    /// <summary>Runs the job on each matching chunk in order, on this thread.</summary>
    public void ForEachChunk<TJob>(ref TJob job) where TJob : struct, IChunkJob
    {
        int count = Gather();
        WorldHandle world = _world.Live;
        Api.BeginIteration(world);
        try
        {
            for (int i = 0; i < count; ++i)
            {
                job.Execute(new ChunkView(world, new ChunkHandle((nint)_chunks[i])));
            }
        }
        finally
        {
            Api.EndIteration(world);
        }
    }

    /// <summary>
    /// Runs the job on each matching chunk as parallel jobs (ADR 0014),
    /// returning when all are done. A throwing chunk is rethrown here.
    /// </summary>
    public void ParallelForEachChunk<TJob>(ref TJob job) where TJob : struct, IChunkJob
    {
        int count = Gather();
        WorldHandle world = _world.Live;
        ChunkRunner<TJob> runner = new() { Job = job, Chunks = _chunks, World = world };
        Api.BeginIteration(world);
        try
        {
            JobSystem.ParallelFor(ref runner, count, 1);
        }
        finally
        {
            Api.EndIteration(world);
        }
    }

    private int Gather()
    {
        if (_handle.IsNull)
        {
            throw new ObjectDisposedException(nameof(Query));
        }
        int count = (int)Api.GatherChunks(_handle, _chunks);
        if (count > _chunks.Length)
        {
            _chunks = new ulong[Math.Max(count, _chunks.Length * 2)];
            count = (int)Api.GatherChunks(_handle, _chunks);
        }
        return count;
    }

    private struct ChunkRunner<TJob> : IParallelForJob where TJob : struct, IChunkJob
    {
        public TJob Job;
        public ulong[] Chunks;
        public WorldHandle World;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int begin, int end)
        {
            for (int i = begin; i < end; ++i)
            {
                Job.Execute(new ChunkView(World, new ChunkHandle((nint)Chunks[i])));
            }
        }
    }
}
