using System.Collections.Concurrent;
using Crucible.Jobs;

namespace Crucible.Tests;

// C# jobs on the native workers (ADR 0014's amendment), under JIT and
// NativeAOT. The job system is global, so these don't run alongside each other.
[NotInParallel("jobs")]
public class JobSystemTests
{
    [Before(Class)]
    public static void StartWorkers() => Crucible.Jobs.Api.Initialize(4);

    [After(Class)]
    public static void StopWorkers() => Crucible.Jobs.Api.Shutdown();

    private struct CountVisits(int[] visits) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            for (int i = begin; i < end; ++i)
            {
                Interlocked.Increment(ref visits[i]);
            }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(997)]
    [Arguments(1_000_000)]
    public async Task EveryIndexIsVisitedOnce(int batchSize)
    {
        int[] visits = new int[100_003];
        CountVisits job = new(visits);

        JobSystem.ParallelFor(ref job, visits.Length, batchSize);

        await Assert.That(visits.All(v => v == 1)).IsTrue();
        await Assert.That(JobSystem.WorkerCount).IsEqualTo(4);
    }

    private struct RecordThreads(ConcurrentDictionary<int, bool> threads) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            threads[Environment.CurrentManagedThreadId] = true;
            Thread.SpinWait(200_000); // long enough that other threads take batches
        }
    }

    [Test]
    public async Task BatchesRunOnSeveralThreads()
    {
        ConcurrentDictionary<int, bool> threads = new();
        RecordThreads job = new(threads);

        JobSystem.ParallelFor(ref job, 64, 1);

        // The workers are native threads, attached to the runtime on their
        // first call into C#.
        await Assert.That(threads.Count).IsGreaterThan(1);
    }

    private struct FailAt(int failing, int[] visits) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            for (int i = begin; i < end; ++i)
            {
                if (i == failing)
                {
                    throw new InvalidOperationException($"index {i} failed");
                }
                Interlocked.Increment(ref visits[i]);
            }
        }
    }

    [Test]
    public async Task AFailingBatchRethrowsOnTheCaller()
    {
        int[] visits = new int[1000];
        FailAt job = new(500, visits);

        InvalidOperationException? exception =
            Assert.Throws<InvalidOperationException>(() => JobSystem.ParallelFor(ref job, visits.Length, 10));

        await Assert.That(exception!.Message).IsEqualTo("index 500 failed");
        // The original stack: thrown in the job body, not by ParallelFor.
        await Assert.That(exception.StackTrace!.Contains(nameof(FailAt.Execute))).IsTrue();
        // Every other batch still ran; only the failing batch stopped early.
        await Assert.That(visits.Take(500).All(v => v == 1)).IsTrue();
        await Assert.That(visits.Skip(510).All(v => v == 1)).IsTrue();

        // The system is still usable.
        int[] after = new int[100];
        CountVisits again = new(after);
        JobSystem.ParallelFor(ref again, after.Length);
        await Assert.That(after.All(v => v == 1)).IsTrue();
    }

    private struct Outer(int[] totals) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            for (int i = begin; i < end; ++i)
            {
                Inner inner = new(totals, i);
                JobSystem.ParallelFor(ref inner, 100, 10);
            }
        }
    }

    private struct Inner(int[] totals, int slot) : IParallelForJob
    {
        public readonly void Execute(int begin, int end) => Interlocked.Add(ref totals[slot], end - begin);
    }

    [Test]
    public async Task JobsCanRunJobs()
    {
        int[] totals = new int[16];
        Outer job = new(totals);

        JobSystem.ParallelFor(ref job, totals.Length, 1);

        await Assert.That(totals.All(t => t == 100)).IsTrue();
    }

    private struct Sum(float[] values, float[] partials) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            float sum = 0;
            for (int i = begin; i < end; ++i)
            {
                sum += values[i];
            }
            partials[begin / 1024] = sum;
        }
    }

    [Test]
    public async Task ParallelForMakesNoGarbageOnceWarm()
    {
        float[] values = new float[1024 * 64];
        float[] partials = new float[64];
        Sum job = new(values, partials);
        JobSystem.ParallelFor(ref job, values.Length, 1024); // warm-up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i)
        {
            JobSystem.ParallelFor(ref job, values.Length, 1024);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }
}
