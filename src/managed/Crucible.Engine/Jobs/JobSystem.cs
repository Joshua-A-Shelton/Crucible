using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Crucible.Jobs;

/// <summary>
/// C# jobs on Crucible's native job system (ADR 0014 and its amendment): the
/// workers call one engine trampoline with a job ID, and the engine runs the
/// job body the ID stands for, so native code never holds a pointer into a
/// game assembly and games stay hot-reloadable. The name is a placeholder
/// (docs/naming.md).
/// </summary>
public static unsafe class JobSystem
{
    /// <summary>Worker threads; the calling thread also works while it waits.</summary>
    public static int WorkerCount => (int)Api.WorkerCount();

    /// <summary>
    /// Runs <c>job.Execute(begin, end)</c> over [0, <paramref name="count"/>)
    /// in batches of <paramref name="batchSize"/> (0: an automatic size), on
    /// the workers and the calling thread, and returns when every batch has
    /// finished. If a batch throws, the others still finish, and the first
    /// exception is rethrown here with its original stack. Allocates nothing
    /// once warmed up.
    /// </summary>
    /// <remarks>
    /// Each batch crosses from native code into C# once (tens of
    /// nanoseconds), so batches should hold real work.
    /// </remarks>
    public static void ParallelFor<T>(ref T job, int count, int batchSize = 0) where T : struct, IParallelForJob
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);
        if (count == 0)
        {
            return;
        }
        Trampoline.EnsureSet();

        Runner<T> runner = RunnerPool<T>.Rent();
        runner.Job = job;
        int id = Slots.Add(runner);
        try
        {
            Api.ParallelForManaged((ulong)id, (uint)count, (uint)batchSize);
        }
        finally
        {
            Slots.Remove(id);
        }
        ExceptionDispatchInfo? failure = runner.TakeFailure();
        runner.Job = default;
        RunnerPool<T>.Return(runner);
        failure?.Throw();
    }

    // A job body in flight, behind its ID.
    private abstract class JobRunner
    {
        private ExceptionDispatchInfo? _failure;

        public abstract void Run(int begin, int end);

        // Keeps the first failure; batches may fail on several threads at once.
        public void RecordFailure(Exception exception) =>
            Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);

        public ExceptionDispatchInfo? TakeFailure() => Interlocked.Exchange(ref _failure, null);
    }

    private sealed class Runner<T> : JobRunner where T : struct, IParallelForJob
    {
        public T Job;

        // A constrained call on the struct: no boxing, and the compiler can
        // inline Execute.
        public override void Run(int begin, int end) => Job.Execute(begin, end);
    }

    // Runners per job type, reused so a ParallelFor allocates nothing once
    // warm. For a game's job type this is an instantiation over a collectible
    // type, so it unloads with the game.
    private static class RunnerPool<T> where T : struct, IParallelForJob
    {
        private static readonly Lock _lock = new();
        private static Runner<T>[] _free = new Runner<T>[4];
        private static int _count;

        public static Runner<T> Rent()
        {
            lock (_lock)
            {
                if (_count > 0)
                {
                    Runner<T> runner = _free[--_count];
                    _free[_count] = null!;
                    return runner;
                }
            }
            return new Runner<T>();
        }

        public static void Return(Runner<T> runner)
        {
            lock (_lock)
            {
                if (_count == _free.Length)
                {
                    Array.Resize(ref _free, _free.Length * 2);
                }
                _free[_count++] = runner;
            }
        }
    }

    // Job IDs: indices into a fixed table of the runners in flight. Taken and
    // released once per ParallelFor (not per batch), under a short lock; the
    // trampoline reads its slot without one, since a slot doesn't change while
    // its job runs.
    private static class Slots
    {
        // In-flight jobs are bounded by nesting depth times threads; far more
        // than any frame needs.
        private const int Capacity = 4096;

        private static readonly JobRunner?[] _runners = new JobRunner?[Capacity];
        private static readonly int[] _free = CreateFreeList();
        private static readonly Lock _lock = new();
        private static int _freeCount = Capacity;

        private static int[] CreateFreeList()
        {
            int[] free = new int[Capacity];
            for (int i = 0; i < Capacity; ++i)
            {
                free[i] = Capacity - 1 - i;
            }
            return free;
        }

        public static int Add(JobRunner runner)
        {
            lock (_lock)
            {
                if (_freeCount == 0)
                {
                    throw new InvalidOperationException($"More than {Capacity} parallel jobs are in flight at once");
                }
                int id = _free[--_freeCount];
                Volatile.Write(ref _runners[id], runner);
                return id;
            }
        }

        public static void Remove(int id)
        {
            lock (_lock)
            {
                _runners[id] = null;
                _free[_freeCount++] = id;
            }
        }

        public static JobRunner Get(ulong id) => Volatile.Read(ref _runners[id])!;
    }

    private static class Trampoline
    {
        private static int _set;

        public static void EnsureSet()
        {
            if (Volatile.Read(ref _set) == 0 && Interlocked.Exchange(ref _set, 1) == 0)
            {
                Api.SetManagedTrampoline((ulong)(delegate* unmanaged<ulong, uint, uint, void>)&Execute);
            }
        }

        // Called by native workers (and waiting threads) for each batch. An
        // exception can't cross into native code, so it's recorded for
        // ParallelFor to rethrow.
        [UnmanagedCallersOnly]
        private static void Execute(ulong id, uint begin, uint end)
        {
            JobRunner runner = Slots.Get(id);
            try
            {
                runner.Run((int)begin, (int)end);
            }
            catch (Exception exception)
            {
                runner.RecordFailure(exception);
            }
        }
    }
}
