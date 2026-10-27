namespace Crucible.Jobs;

/// <summary>
/// A job body for <see cref="JobSystem.ParallelFor{T}"/>: a struct carrying
/// what the job works on (arrays, memory, handles), with <see cref="Execute"/>
/// called for disjoint ranges of the index space, in parallel. A range rather
/// than one index per call lets the body loop tightly and use wide math.
/// The name is a placeholder (docs/naming.md).
/// </summary>
/// <remarks>
/// Batches run at the same time on one copy of the struct, so Execute must not
/// write the struct's own fields; write through what they refer to, each batch
/// only to its own range.
/// </remarks>
public interface IParallelForJob
{
    void Execute(int begin, int end);
}
