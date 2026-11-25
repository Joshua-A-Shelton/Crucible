# Review 1: the core

The engine's foundations: how C++ and C# split the work, the ECS that holds
the world, the frame and its systems, and the job system. Everything else
is built on these. Decisions: ADRs 0001, 0003, 0011, 0012, 0013, 0014,
0023, 0033, 0034, 0035, 0036, 0037.

## What it does

A game is entities with components, changed every frame by systems that
run in a fixed order of phases. The data lives in native memory where both
C++ and C# can reach it without copying; the frame is driven from C#; heavy
work (rendering, physics, animation, audio) is native.

```
 launcher (C++ main)            the C++ core (library)           C# engine layer
 ───────────────────            ──────────────────────           ───────────────
 platform, window, Slag  ──►    ECS worlds, jobs, render,  ◄──►  Host → FrameLoop → Schedule
 starts .NET, calls one         physics, animation, audio        systems (C# and C++)
 managed entry point            native systems registry          game code (reloadable)
```

## How it works

### C++ and C# (ADR 0001, 0011)

- **C++ owns the process:** `main()`, the window, the GPU device, crash
  handling. It's a thin launcher around the core library.
- **C# owns the frame:** the launcher starts .NET and calls one entry point
  in the engine's C# layer, which runs the loop until quit.
- **Two ways to run C#:** the editor and JIT-shipped games run CoreCLR (so
  game code can be unloaded and reloaded, and JIT games can load mods);
  AOT games compile everything, C++ and C#, into one native executable.
  The same game code must work in both, so .NET's AOT analyzers are errors
  everywhere.
- **The hot reload rule:** native code only ever points into the engine's
  own C# layer, which is never unloaded. It identifies game objects by IDs.
  Callbacks into game code (contacts, asset events, managed jobs) go
  through engine-side dispatchers. That's what lets a game's assembly be
  unloaded cleanly.
- **Bindings are generated:** C++ functions and structs marked
  `CRUCIBLE_EXPOSE` get C# twins written by the binding generator
  (`tools/Crucible.BindGen`, ADR 0017), so the two sides can't drift.

### The ECS (ADR 0012, 0023, 0033, 0034, 0035)

- **Archetype chunks.** Entities with the same set of components are
  stored together, one array per component type (a column), in fixed-size
  chunks. Iterating a component is walking arrays, which is fast.
- **Sparse storage, opt-in.** A component added and removed constantly
  (an event marker) can live in a per-world sparse set instead, so adding
  it doesn't move the entity between chunks.
- **Components registered at run time**, from C# as well as C++, with
  their size and alignment. A component whose layout changes across a hot
  reload is migrated field by field (matching names copied, new ones
  zeroed).
- **Blittable components** (plain structs) live in native memory, and C#
  sees them as spans: no copies, one crossing per chunk, not per entity.
  **Managed components** (classes, structs holding references) live in C#
  pools; the chunk holds just a slot number, and native code tells C# when
  to free one.
- **Hierarchy** is a `Parent` component plus an ordered child list kept by
  the ECS. Destroying an entity destroys what's under it. Other links are
  entity IDs in components; an ID carries a generation, so a stale one is
  detectable.
- **Change detection:** every column has a version, stamped when it's
  written (asking for write access counts as writing). A system can skip
  chunks that haven't changed since it last ran. The renderer uses this to
  update only the transforms that moved.

### The frame (ADR 0003, 0013, 0036, 0037)

- **Phases in order:** BeginFrame (frame pacing), Input, FixedTick
  (PrePhysics, Physics, PostPhysics, run 0 to 4 times at 60 Hz from an
  accumulator), Update, Animation, LateUpdate, RenderPrepare, RenderSubmit
  (present), EndFrame. Games can add, replace, or remove phases at start.
- **Everything is a system in a phase**, the engine's own work included
  (the pacing wait, the input poll, present), so a game orders its
  systems around them.
- **C# systems** are classes with `Run`, optional `Start` and `Stop`,
  registered by a `[System]` attribute that a source generator turns into
  plain registration code (safe for NativeAOT). **C++ systems** register a
  function pointer by name into a native registry; the C# schedule builder
  merges them.
- **Built once, then fixed.** Sorting by before/after constraints happens
  when the schedule is built; ties break by name, so order never depends on
  load order. Each frame runs flat arrays with no allocation.
- **Structural changes are deferred.** Creating or destroying entities, or
  adding and removing components, during iteration goes into a command
  buffer, one per world, applied at the end of every phase.
- **Gameplay systems** (physics stepping, animation's clock, audio, a
  game's own) are marked as such: the editor runs the engine's other
  systems on the scene all the time, and the gameplay ones only while
  playing.
- **Latency first:** just-in-time frame pacing is on by default, present
  is Mailbox where available, and frame generation is never on by
  default.

### Jobs (ADR 0014)

- **One native job system for everything:** about one worker thread per
  core, each with its own queue, stealing from others when idle. A thread
  waiting on jobs runs other jobs meanwhile.
- **Two priorities:** frame jobs before background jobs.
- **C# reaches it** through a parallel loop over chunks or a range
  (`JobSystem.ParallelFor`); a managed job body runs through the engine's
  dispatcher, so native code never holds game code.
- **Parallelism is inside systems:** systems run one after another, each
  splitting its own work. Running systems in parallel with each other was
  left for later.
- **The exception:** audio mixing has its own real-time thread.

## Why it was built this way

- **Native data, C# logic:** game code in C# for iteration speed and hot
  reload, the heavy lifting in C++ for speed; sharing the component memory
  avoids the copy cost that usually comes with two languages.
- **ECS, but not for everything:** the world is entities, because that's
  what engines do well and it's what the editor shows as a hierarchy; game
  rules and flow (inventory, quests, dialogue) stay plain C# referring to
  entities by ID.
- **Built, not adopted:** the ECS (flecs as the reference) and the job
  system (enkiTS as the fallback) were written for Crucible, mostly for
  run-time registration, hot reload, and a C# side with no copies.
- **Deterministic order:** a schedule that never depends on load order
  makes bugs reproducible.

## What I'd question

Things that work but that I'd raise, several found while building the
editor extensions:

1. **Running a world outside the loop is hand work.** A preview world
   (ADR 0125) had to call the animation steps itself, add missing
   `WorldTransform`s itself, and advance the world's version itself, or
   the renderer saw no change. Any second world (previews now, perhaps
   streaming or simulation worlds later) repeats that. A way to run a
   chosen set of systems on a world with its own clock would remove it.
2. **`World.UpdateWorldTransforms` isn't the propagation system.** It
   updates existing world transforms but doesn't give new entities one;
   the system does both. The same name for less work surprised me.
3. **The editor doesn't show the schedule.** ADR 0013 says the editor
   shows the phases, their systems, the order, and per-system timing; I
   didn't find that view. With gameplay and non-gameplay systems running at
   different times in the editor, it would help to see what runs when.
4. **Per-process state and parallel tests.** Two engine tests have each
   failed once in a full parallel run and passed on rerun (open items);
   component registration and some registries are per process, not per
   world, which is a likely place for tests to step on each other.
5. **Systems run one at a time.** Fine at this size; it's the first thing
   to revisit if a game's frame is CPU-bound on the main thread.
