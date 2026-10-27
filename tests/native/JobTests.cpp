// The job system (ADR 0014 and its amendment). Each test starts its own
// workers and stops them, so they don't depend on the launcher.

#include <crucible/Jobs.h>

#include <doctest/doctest.h>

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

using namespace crucible::jobs;

namespace {

struct JobScope
{
    explicit JobScope(uint32_t workers)
    {
        REQUIRE(static_cast<bool>(initialize(workers)));
    }
    ~JobScope()
    {
        shutdown();
    }
};

void increment(void* data)
{
    static_cast<std::atomic<int>*>(data)->fetch_add(1);
}

// Marks each index it covers; a second visit is counted as an error.
struct Coverage
{
    std::vector<std::atomic<uint8_t>> visits;
    std::atomic<int> repeats{0};

    explicit Coverage(size_t count)
        : visits(count)
    {}

    static void visit(void* data, uint32_t begin, uint32_t end)
    {
        auto* self = static_cast<Coverage*>(data);
        for (uint32_t i = begin; i < end; ++i)
        {
            if (self->visits[i].exchange(1) != 0)
            {
                self->repeats.fetch_add(1);
            }
        }
    }

    bool complete() const
    {
        for (const auto& visit : visits)
        {
            if (visit.load() == 0)
            {
                return false;
            }
        }
        return repeats.load() == 0;
    }
};

} // namespace

TEST_CASE("Every job of a large batch runs exactly once")
{
    JobScope scope(4);
    CHECK(workerCount() == 4);
    std::atomic<int> total{0};
    std::vector<Job> jobs(10'000, Job{increment, &total});
    JobCounter counter;
    run(jobs, JobPriority::Frame, counter);
    wait(counter);
    CHECK(isDone(counter));
    CHECK(total.load() == 10'000);
}

TEST_CASE("parallelFor covers its range exactly once in any batch size")
{
    JobScope scope(3);
    for (uint32_t batch : {0u, 1u, 7u, 1000u, 50'000u})
    {
        Coverage coverage(10'007);
        parallelFor(10'007, batch, Coverage::visit, &coverage);
        CHECK(coverage.complete());
    }
    parallelFor(0, 0, Coverage::visit, nullptr); // nothing to do
}

TEST_CASE("Nested parallelFor inside jobs finishes: waiting threads run jobs")
{
    JobScope scope(2); // fewer workers than outer jobs, so waiting must help
    std::atomic<int> total{0};
    parallelFor(
        16, 1,
        [](void* data, uint32_t, uint32_t) {
            parallelFor(
                100, 10,
                [](void* inner, uint32_t begin, uint32_t end) {
                    static_cast<std::atomic<int>*>(inner)->fetch_add(static_cast<int>(end - begin));
                },
                data);
        },
        &total);
    CHECK(total.load() == 1600);
}

TEST_CASE("Many threads can run and wait at once")
{
    JobScope scope(3);
    std::atomic<int> total{0};
    std::vector<std::thread> submitters;
    for (int t = 0; t < 4; ++t)
    {
        submitters.emplace_back([&total] {
            for (int round = 0; round < 200; ++round)
            {
                Job jobs[8];
                for (Job& job : jobs)
                {
                    job = {increment, &total};
                }
                JobCounter counter;
                run(jobs, JobPriority::Frame, counter);
                wait(counter);
            }
        });
    }
    for (std::thread& submitter : submitters)
    {
        submitter.join();
    }
    CHECK(total.load() == 4 * 200 * 8);
}

TEST_CASE("A blocked background job doesn't hold up frame work")
{
    JobScope scope(1); // the only worker gets stuck in the background job
    std::atomic<bool> started{false};
    std::atomic<bool> release{false};
    struct Blocker
    {
        std::atomic<bool>* started;
        std::atomic<bool>* release;
    } blocker{&started, &release};
    const Job background{[](void* data) {
                             auto* b = static_cast<Blocker*>(data);
                             b->started->store(true);
                             while (!b->release->load())
                             {
                                 std::this_thread::yield();
                             }
                         },
                         &blocker};
    JobCounter backgroundCounter;
    run({&background, 1}, JobPriority::Background, backgroundCounter);
    while (!started.load())
    {
        std::this_thread::yield();
    }

    // The worker is busy, so the waiting main thread does the frame work.
    Coverage coverage(1000);
    parallelFor(1000, 10, Coverage::visit, &coverage);
    CHECK(coverage.complete());
    CHECK_FALSE(isDone(backgroundCounter));

    release.store(true);
    wait(backgroundCounter);
}

TEST_CASE("A waiting thread never runs background jobs")
{
    JobScope scope(1);
    std::atomic<int32_t> ranOn{-2};
    const Job background{[](void* data) { static_cast<std::atomic<int32_t>*>(data)->store(currentWorker()); }, &ranOn};
    JobCounter backgroundCounter;
    // Queued by the main thread, then the main thread waits on frame work
    // over and over while it's pending: it must leave it to the worker.
    run({&background, 1}, JobPriority::Background, backgroundCounter);
    for (int i = 0; i < 100 && !isDone(backgroundCounter); ++i)
    {
        Coverage coverage(64);
        parallelFor(64, 1, Coverage::visit, &coverage);
    }
    wait(backgroundCounter);
    CHECK(ranOn.load() == 0); // worker 0, never the main thread (-1)
    CHECK(currentWorker() == -1);
}

TEST_CASE("Jobs run inline without initialize and the system restarts")
{
    std::atomic<int> total{0};
    Job job{increment, &total};
    JobCounter counter;
    run({&job, 1}, JobPriority::Frame, counter);
    CHECK(total.load() == 1); // already done
    CHECK(isDone(counter));
    CHECK(workerCount() == 0);

    for (int cycle = 0; cycle < 2; ++cycle)
    {
        JobScope scope(0); // the default count
        CHECK(workerCount() >= 1);
        CHECK(initialize(2).status() == crucible::Status::Unsupported); // already running
        parallelFor(100, 1, [](void*, uint32_t, uint32_t) {}, nullptr);
    }
    CHECK(workerCount() == 0);
}
