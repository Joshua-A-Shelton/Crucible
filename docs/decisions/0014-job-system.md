# 0014 — Job system

**Status:** Accepted (2026-10-25).

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
