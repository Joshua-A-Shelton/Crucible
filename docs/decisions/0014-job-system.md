# 0014 — Job system

**Status:** Accepted (2026-10-25). Amended 2026-10-27 (M3): the API shape,
worker count, and what waiting threads run.

## Context

Work that runs in parallel: ECS systems splitting work over chunks (ADR
0013), native subsystems (culling, animation, physics), render command
recording on several threads (Slag allows recording different command
buffers on different threads), and background work (asset loading,
decompression, streaming).

The trap is **oversubscription**: if the .NET thread pool, a native pool,
and a physics library each start a thread per core, several times as many
threads compete for the same cores, the OS switches between them
unpredictably, frame jobs get descheduled behind background work, and
frame times spike (goal 4).

## Options

- **(a) Native job system only;** C# submits through it.
- **(b) C# job system** on the .NET thread pool or `Parallel.For`. Native
  subsystems would call into C# to schedule work, against the direction
  of ADR 0011, and the thread pool's thread-count heuristics are tuned for
  servers, not frame-sized jobs.
- **(c) Both, separately:** oversubscription.

Build or library:

| Option | License | Notes |
|---|---|---|
| Our own | zlib | Instructive; shaped to the rules below; the work-stealing queue is the subtle part |
| enkiTS | zlib | Small, proven in games; priorities; external threads can join |
| Taskflow | MIT | Task graphs, more than needed (0013 deferred graph scheduling) |
| oneTBB | Apache 2.0 | Large, general-purpose |
| marl | Apache 2.0 | Fiber-based, conflicts with point 4 |

## Decision

1. **One native job system, shared by everything.** A fixed set of worker
   threads (about one per core, leaving room for the main thread). C++
   uses it directly; C# gets a wrapper API over it, mainly a parallel loop
   over ECS chunks. Third-party libraries are plugged into it through
   their task interfaces where they have one, instead of starting their
   own threads.
2. **Managed job bodies go through the engine's C# dispatcher** (ADR
   0011): a worker calls the engine's C# trampoline with a job ID, and the
   engine calls the game code. Native code never holds a pointer into a
   game assembly.
3. **Two priority levels, frame and background.** Frame jobs always run
   before background jobs.
4. **Work-stealing worker threads, no fibers in v1.** Each worker has its
   own queue and steals when idle. Any thread waiting on a job, the main
   thread included, runs other jobs while it waits.
5. **The .NET thread pool** is kept small, for `async` I/O and other
   non-frame work only.
6. **Crucible builds its own,** with **enkiTS** (zlib) as the fallback if
   ours doesn't work out.
7. **Exception (added 2026-10-25, ADR 0028):** audio mixing runs on its own
   real-time audio thread, because audio deadlines can't wait behind frame
   jobs.

## Why

One pool means one thread per core and no competition between schedulers.
Native placement lets C++ subsystems schedule without crossing the
boundary, and the dispatcher keeps hot reload safe. Priorities stop
streaming from delaying a frame. Fibers let jobs pause and wait mid-job,
but they are complex and interact badly with the .NET runtime.

Building our own: the job system is one of the most instructive parts of an
engine (this is a learning project), and the design above is specific
enough (two priorities, the C# dispatcher, waiting threads helping) that
adapting a library could be as much work. It can start with simple locked
queues and move to lock-free work-stealing deques later.

A side effect: a .NET garbage collection pauses threads running managed
code, but native jobs keep running, so a GC stalls only the C# parts of a
frame.

Given up: fibers, and a ready-made, already-tuned scheduler (kept as the
fallback).

## Amendment (2026-10-27): the API (M3 decision D)

Settled with the project owner in M3 step 4. Names are placeholders.

How engines shape a job API:

- **Handles with dependencies** (Unity's C# Job System, Bevy):
  `schedule(job, dependsOn)` returns a handle; chains run without anyone
  waiting in between. Flexible; every job carries dependency bookkeeping.
- **Counters** (Naughty Dog's GDC 2015 talk, enkiTS's task sets): submit a
  batch with an atomic counter, each completion decrements it, and waiting
  runs other jobs until it reaches zero. Small, cheap, and exactly
  fork-join.
- **Parent/child jobs** (the "Job System 2.0" series): a job counts its
  unfinished children. Good for recursive splitting; a counter in every job.

Decisions:

1. **Counters, fork-join.** `run(jobs, priority, counter)`, `wait(counter)`,
   `isDone(counter)`, and `parallelFor` (split a range, run, wait). Jobs
   are C-shaped (a function pointer and a data pointer), so C# reaches them
   by ID (decision 2 above). This matches ADR 0013's "parallelism inside
   systems". Handles with dependencies are to be revisited
   (`docs/open-items.md`), likely with automatic scheduling between
   systems.
2. **Workers: logical cores minus one,** at least one, because the main
   thread joins in while it waits; overridable (`initialize`, the
   launcher's `--workers`). Whether SMT siblings help or hurt is measured
   with real systems later.
3. **A waiting thread runs frame jobs only,** so the main thread can't get
   stuck in a long background job (a texture decode) while its frame
   waits. Background jobs run only on workers with no frame work, so they
   should be split small.

As built (`crucible/Jobs.h`, M3 step 4):

- Each worker has a locked double-ended queue per priority; threads that
  aren't workers submit to a shared queue per priority. A thread takes its
  own newest job (still in cache), then the shared queue's oldest, then
  steals other workers' oldest; frame queues everywhere before any
  background queue.
- Idle workers and waiting threads spin briefly, then sleep on one
  "epoch" atomic bumped whenever jobs are queued or a counter reaches zero
  (C++20 `atomic::wait`: a futex on Linux, `WaitOnAddress` on Windows).
  Reading it before looking for work or completion means no wake-up is
  missed. Cost: a counter completing wakes sleeping workers briefly.
- Nesting is safe: jobs may run and wait on jobs, and a waiting worker runs
  other frame jobs. Without `initialize`, `run` executes inline.
- The launcher starts the job system after the platform and before the
  renderer (`--workers N` overrides the count); workers are named
  `crucible-wN` for debuggers and profilers.
- Tests cover exactly-once execution, parallelFor coverage, nesting,
  concurrent submitters, and the priority rule; they run clean 50 times
  under ThreadSanitizer.

## Amendment (2026-10-27): jobs from C# (M3 step 5)

Settled with the project owner. Names are placeholders.

- **Job bodies are structs implementing `IParallelForJob`**, with
  `Execute(int begin, int end)`, run by
  `JobSystem.ParallelFor(ref job, count, batchSize)`. The struct carries
  the data, so nothing is allocated, and the generic call lets the compiler
  call `Execute` directly. Unity's job system works this way, with one index
  per call; a range lets the body loop tightly and use wide math.
- **The trampoline** (decision 2): at first use, the engine gives native
  code the address of one `[UnmanagedCallersOnly]` method. A `ParallelFor`
  puts its body in a slot of a fixed managed table and calls native
  `parallelForManaged` with the slot's ID; each batch calls the trampoline
  with (ID, begin, end), which runs the body. Native code never holds a
  pointer into a game assembly, and the per-type runner pools are generic
  instantiations over the game's types, so they unload with it (the hot
  reload test runs a game-defined job, then unloads the game).
- **Exceptions:** the trampoline catches an exception from a body (it can't
  cross into native code); the other batches still finish, and
  `ParallelFor` rethrows the first one on the calling thread with its
  original stack.
- **Costs:** one native-to-managed transition per batch (tens of
  nanoseconds), so batches should hold real work; no allocation per call
  once warm. Worker threads become managed threads on their first call; idle
  in native code, they don't hold up the garbage collector.
- **Exposure:** the trampoline functions and the job system's
  `initialize`/`shutdown` are exposed as internal C# methods (ADR 0017's
  `CRUCIBLE_EXPOSE_INTERNAL`): the engine's tests start workers themselves,
  since they run without the launcher.
