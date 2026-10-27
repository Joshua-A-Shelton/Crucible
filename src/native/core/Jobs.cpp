#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/SmallVector.h>
#include <crucible/Vector.h>

#include <algorithm>
#include <cstdio>
#include <mutex>
#include <thread>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#else
#include <pthread.h>
#endif

namespace crucible::jobs {

namespace {

constexpr int priorityCount = 2;

// Polls before sleeping: a fork-join wait is often over in microseconds, far
// shorter than going to sleep and being woken.
constexpr int spinCount = 64;

struct QueuedJob
{
    Job job;
    JobCounter* counter;
};

// A locked double-ended queue on a ring buffer (ADR 0014: locked queues
// first). The owner takes its newest job, still in cache; others take the
// oldest. `size` lets a thread skip an empty queue without the lock.
class JobQueue
{
public:
    void push(std::span<const Job> jobs, JobCounter* counter)
    {
        std::lock_guard lock(mutex);
        if (count + jobs.size() > slots.size())
        {
            grow(count + jobs.size());
        }
        const size_t mask = slots.size() - 1;
        for (const Job& job : jobs)
        {
            slots[(head + count) & mask] = {job, counter};
            ++count;
        }
        size.store(count, std::memory_order_release);
    }

    bool popNewest(QueuedJob& out)
    {
        if (size.load(std::memory_order_acquire) == 0)
        {
            return false;
        }
        std::lock_guard lock(mutex);
        if (count == 0)
        {
            return false;
        }
        --count;
        out = slots[(head + count) & (slots.size() - 1)];
        size.store(count, std::memory_order_release);
        return true;
    }

    bool popOldest(QueuedJob& out)
    {
        if (size.load(std::memory_order_acquire) == 0)
        {
            return false;
        }
        std::lock_guard lock(mutex);
        if (count == 0)
        {
            return false;
        }
        out = slots[head];
        head = (head + 1) & (slots.size() - 1);
        --count;
        size.store(count, std::memory_order_release);
        return true;
    }

private:
    // Keeps the capacity a power of two, so the ring wraps with a mask.
    void grow(size_t needed)
    {
        size_t capacity = slots.size() < 64 ? 64 : slots.size();
        while (capacity < needed)
        {
            capacity *= 2;
        }
        Vector<QueuedJob> grown(MemoryTag::Jobs);
        grown.resize(capacity);
        for (size_t i = 0; i < count; ++i)
        {
            grown[i] = slots[(head + i) & (slots.size() - 1)];
        }
        slots = std::move(grown);
        head = 0;
    }

    std::mutex mutex;
    Vector<QueuedJob> slots{MemoryTag::Jobs};
    size_t head = 0;
    size_t count = 0;
    std::atomic<size_t> size{0};
};

struct Worker
{
    JobQueue queues[priorityCount];
    std::thread thread;
};

struct System
{
    Vector<Worker*> workers{MemoryTag::Jobs};
    // Where threads that aren't workers (the main thread) put their jobs.
    JobQueue shared[priorityCount];
    std::atomic<bool> running{false};
    std::atomic<bool> stopping{false};
    // Bumped whenever jobs are queued or a counter reaches zero. Sleepers
    // (idle workers and waiting threads) read it before looking for work or
    // completion, then sleep until it changes, so they can't miss either.
    std::atomic<uint32_t> epoch{0};
};

System jobSystem;

thread_local int32_t workerIndex = -1;

void bumpEpoch()
{
    jobSystem.epoch.fetch_add(1);
    jobSystem.epoch.notify_all();
}

void execute(const QueuedJob& queued)
{
    queued.job.function(queued.job.data);
    if (queued.counter->remaining.fetch_sub(1) == 1)
    {
        bumpEpoch();
    }
}

// Frame work first everywhere, then (unless frameOnly) background work: the
// caller's own newest job, the shared queue's oldest, then other workers'
// oldest, starting after the caller so thieves spread out.
bool findJob(int32_t self, bool frameOnly, QueuedJob& out)
{
    const int32_t workerTotal = static_cast<int32_t>(jobSystem.workers.size());
    const int priorities = frameOnly ? 1 : priorityCount;
    for (int priority = 0; priority < priorities; ++priority)
    {
        if (self >= 0 && jobSystem.workers[self]->queues[priority].popNewest(out))
        {
            return true;
        }
        if (jobSystem.shared[priority].popOldest(out))
        {
            return true;
        }
        for (int32_t offset = 1; offset <= workerTotal; ++offset)
        {
            const int32_t victim = (self + offset + workerTotal) % workerTotal;
            if (victim != self && jobSystem.workers[victim]->queues[priority].popOldest(out))
            {
                return true;
            }
        }
    }
    return false;
}

// Named, so debuggers and profilers show "crucible-w3" instead of a number.
void nameThread(uint32_t index)
{
    char name[16];
    std::snprintf(name, sizeof(name), "crucible-w%u", index);
#if defined(_WIN32)
    wchar_t wide[16];
    for (int i = 0; i < 16; ++i)
    {
        wide[i] = static_cast<wchar_t>(name[i]);
    }
    SetThreadDescription(GetCurrentThread(), wide);
#elif defined(__APPLE__)
    pthread_setname_np(name);
#else
    pthread_setname_np(pthread_self(), name);
#endif
}

void workerLoop(uint32_t index)
{
    workerIndex = static_cast<int32_t>(index);
    nameThread(index);
    for (;;)
    {
        const uint32_t seen = jobSystem.epoch.load();
        QueuedJob job;
        bool found = false;
        for (int spin = 0; spin < spinCount && !found; ++spin)
        {
            found = findJob(workerIndex, false, job);
            if (!found)
            {
                std::this_thread::yield();
            }
        }
        if (found)
        {
            execute(job);
            continue;
        }
        // Stopping only once the queues are empty, so queued work still runs.
        if (jobSystem.stopping.load())
        {
            return;
        }
        jobSystem.epoch.wait(seen);
    }
}

struct Range
{
    void (*function)(void* data, uint32_t begin, uint32_t end);
    void* data;
    uint32_t begin;
    uint32_t end;
};

void runRange(void* data)
{
    const Range* range = static_cast<const Range*>(data);
    range->function(range->data, range->begin, range->end);
}

} // namespace

Result<void> initialize(uint32_t count)
{
    if (jobSystem.running.load())
    {
        return Status::Unsupported;
    }
    if (count == 0)
    {
        // hardware_concurrency is 0 when it can't tell.
        const uint32_t logical = std::thread::hardware_concurrency();
        count = logical > 1 ? logical - 1 : 1;
    }
    jobSystem.stopping.store(false);
    jobSystem.workers.reserve(count);
    for (uint32_t i = 0; i < count; ++i)
    {
        jobSystem.workers.pushBack(create<Worker>(MemoryTag::Jobs));
    }
    // Every worker exists before any starts: thieves index the whole list.
    for (uint32_t i = 0; i < count; ++i)
    {
        jobSystem.workers[i]->thread = std::thread(workerLoop, i);
    }
    jobSystem.running.store(true);
    return {};
}

void shutdown()
{
    if (!jobSystem.running.load())
    {
        return;
    }
    jobSystem.stopping.store(true);
    bumpEpoch();
    for (Worker* worker : jobSystem.workers)
    {
        worker->thread.join();
    }
    for (Worker* worker : jobSystem.workers)
    {
        destroy(MemoryTag::Jobs, worker);
    }
    jobSystem.workers.clear();
    jobSystem.running.store(false);
}

void run(std::span<const Job> jobs, JobPriority priority, JobCounter& counter)
{
    if (jobs.empty())
    {
        return;
    }
    // Counted before queued, so a fast job can't take the counter to zero
    // while others are still being added.
    counter.remaining.fetch_add(static_cast<uint32_t>(jobs.size()));
    if (!jobSystem.running.load())
    {
        for (const Job& job : jobs)
        {
            execute({job, &counter});
        }
        return;
    }
    const int index = priority == JobPriority::Frame ? 0 : 1;
    JobQueue& queue = workerIndex >= 0 ? jobSystem.workers[workerIndex]->queues[index] : jobSystem.shared[index];
    queue.push(jobs, &counter);
    bumpEpoch();
}

void wait(JobCounter& counter)
{
    for (;;)
    {
        const uint32_t seen = jobSystem.epoch.load();
        if (counter.remaining.load() == 0)
        {
            return;
        }
        QueuedJob job;
        bool found = false;
        for (int spin = 0; spin < spinCount && !found && counter.remaining.load() != 0; ++spin)
        {
            found = findJob(workerIndex, true, job);
            if (!found)
            {
                std::this_thread::yield();
            }
        }
        if (found)
        {
            execute(job);
        }
        else if (counter.remaining.load() != 0)
        {
            jobSystem.epoch.wait(seen);
        }
    }
}

bool isDone(const JobCounter& counter)
{
    return counter.remaining.load() == 0;
}

void parallelFor(uint32_t count, uint32_t batchSize, void (*function)(void* data, uint32_t begin, uint32_t end),
                 void* data)
{
    if (count == 0)
    {
        return;
    }
    if (batchSize == 0)
    {
        // About four batches per thread: enough to even out uneven work.
        const uint32_t threads = workerCount() + 1;
        batchSize = std::max<uint32_t>(1, count / (threads * 4));
    }
    const uint32_t batches = (count + batchSize - 1) / batchSize;
    if (batches == 1)
    {
        function(data, 0, count);
        return;
    }

    // On this thread's stack, which outlives the jobs: parallelFor waits.
    SmallVector<Range, 64> ranges(MemoryTag::Jobs);
    SmallVector<Job, 64> jobList(MemoryTag::Jobs);
    ranges.resize(batches);
    for (uint32_t batch = 0; batch < batches; ++batch)
    {
        const uint32_t begin = batch * batchSize;
        ranges[batch] = {function, data, begin, std::min(count, begin + batchSize)};
        jobList.pushBack({runRange, &ranges[batch]});
    }
    JobCounter counter;
    run(jobList.span(), JobPriority::Frame, counter);
    wait(counter);
}

uint32_t workerCount()
{
    return static_cast<uint32_t>(jobSystem.workers.size());
}

int32_t currentWorker()
{
    return workerIndex;
}

namespace {

using ManagedTrampoline = void (*)(uint64_t jobId, uint32_t begin, uint32_t end);

std::atomic<ManagedTrampoline> managedTrampoline{nullptr};

void runManagedRange(void* data, uint32_t begin, uint32_t end)
{
    managedTrampoline.load(std::memory_order_acquire)(*static_cast<const uint64_t*>(data), begin, end);
}

} // namespace

void setManagedTrampoline(uint64_t trampolineAddress)
{
    managedTrampoline.store(reinterpret_cast<ManagedTrampoline>(static_cast<uintptr_t>(trampolineAddress)),
                            std::memory_order_release);
}

void parallelForManaged(uint64_t jobId, uint32_t count, uint32_t batchSize)
{
    if (managedTrampoline.load(std::memory_order_acquire) == nullptr)
    {
        std::fprintf(stderr, "crucible: parallelForManaged before the C# trampoline was set\n");
        return;
    }
    parallelFor(count, batchSize, runManagedRange, &jobId);
}

} // namespace crucible::jobs
