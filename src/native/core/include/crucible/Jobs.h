#pragma once

// Crucible's job system (ADR 0014 and its amendment): worker threads, one per
// logical core but one, running C-shaped jobs. Fork-join with counters: run a
// batch with a counter, then wait on it; waiting threads run frame jobs in the
// meantime. Names are placeholders (docs/naming.md).
//
//     JobCounter counter;
//     run(jobs, JobPriority::Frame, counter);
//     wait(counter); // runs frame jobs until all of `jobs` have finished
//
// Thread-safe: any thread may run, wait, and parallelFor, jobs included
// (nesting can't deadlock: a waiting thread runs other jobs).

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <atomic>
#include <cstdint>
#include <span>

namespace crucible::jobs {

enum class JobPriority : uint8_t
{
    // This frame's work. Waiting threads run only these.
    Frame,
    // Work that may take frames (asset decoding, streaming): run only by
    // workers with no frame work, so keep each job short.
    Background,
};

// A function and its data, so C# can reach jobs by ID (ADR 0014).
struct Job
{
    void (*function)(void* data);
    void* data;
};

// Counts unfinished jobs. The caller owns it and keeps it alive until it
// reaches zero; one counter can collect several run calls.
struct JobCounter
{
    std::atomic<uint32_t> remaining{0};
};

// Starts the workers: `workerCount` of them, or logical cores minus one
// (at least one) for 0. Called by the launcher after the platform
// (ADR 0011). Without it, run executes jobs inline on the calling thread.
// Internal in C#, for the engine's tests, which run without the launcher.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL Result<void> initialize(uint32_t workerCount = 0);

// Runs every queued job, then joins the workers. Counters in use must have
// reached zero.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void shutdown();

// Queues `jobs` (copied) and adds them to `counter`.
void run(std::span<const Job> jobs, JobPriority priority, JobCounter& counter);

// Returns once `counter` reaches zero, running frame jobs meanwhile.
void wait(JobCounter& counter);

bool isDone(const JobCounter& counter);

// Calls function(data, begin, end) over [0, count) in batches of
// `batchSize` (0: an automatic size), in parallel, and waits for all of them.
// Exported: editor libraries beside the core (ADR 0042) run their work on
// the core's workers.
CRUCIBLE_EXPORT void parallelFor(uint32_t count, uint32_t batchSize,
                                 void (*function)(void* data, uint32_t begin, uint32_t end), void* data);

// Worker threads (0 before initialize).
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t workerCount();

// The calling thread's worker index, or -1 if it isn't a worker.
CRUCIBLE_EXPOSE int32_t currentWorker();

// C# jobs (ADR 0014's amendment, M3 step 5). The engine's C# layer sets its
// trampoline once: an [UnmanagedCallersOnly] method taking (job ID, begin,
// end), as an address, since the generator doesn't do function pointers.
// parallelForManaged is parallelFor with each batch calling the trampoline
// with `jobId`; the C# side maps the ID to the job body, so native code never
// holds a pointer into a game assembly. Both internal to the engine.
CRUCIBLE_EXPOSE_INTERNAL void setManagedTrampoline(uint64_t trampolineAddress);
CRUCIBLE_EXPOSE_INTERNAL void parallelForManaged(uint64_t jobId, uint32_t count, uint32_t batchSize);

} // namespace crucible::jobs
