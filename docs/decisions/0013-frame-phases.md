# 0013 — Frame phases

**Status:** Accepted (2026-10-25). Amended 2026-10-26 (M2): the minimal
loop driver.

## Context

ADR 0003 made the frame a set of ordered phases a game can reorder,
replace, or extend, with a variable-rate loop, fixed-tick physics, the
camera and input path on the latest state, pacing, and late latching. ADR
0011 put the loop driver in C#. ADR 0012 has systems register into phases
and applies structural changes at safe points. This ADR is the mechanism.

## Options

How engines specify ordering:

- **(a) Fixed named phases plus order numbers** (Unity's PlayerLoop).
  Simple, but numbers are fragile: two plugins pick the same one and the
  result depends on load order.
- **(b) A dependency graph** (Bevy, flecs): before/after constraints and
  sometimes read/write sets, with automatic parallel scheduling. Robust and
  powerful, but the order is implicit and automatic parallelism is complex
  to build and debug.
- **(c) The loop as code** (MonoGame's `Update`/`Draw`). Maximally
  hackable, with no structure for plugins or the editor to hook into.

## Decision

A layered mix:

1. **The frame is an ordered list of phases.** Crucible provides a default
   list; at startup, games and plugins insert phases before or after
   others, replace them, or remove them.
2. **Systems register into a phase** with optional before/after
   constraints by name within it. The order is sorted once, when the
   schedule is built, not every frame; ties break deterministically, so
   order never depends on load order.
3. **The fixed tick is a phase group** that runs its sub-phases 0..N times
   per frame from a time accumulator. The tick rate is configurable, with
   a maximum number of steps per frame.
4. **Each phase ends in a sync point** where deferred structural changes
   (ADR 0012) are applied. Entity layout is stable within a phase.
5. **Parallelism in v1 is inside systems:** systems run in order, and each
   can split its work across cores through the job system. Automatic
   parallel scheduling between systems is deferred.
6. **The loop driver is replaceable,** as an escape hatch (e.g. a game
   that pipelines rendering onto another thread for throughput, the opt-in
   from ADR 0003).
7. **The editor shows the schedule:** phases, their systems, the resolved
   order, and per-system timing, from generated metadata (ADR 0006).

### Default phases (names are placeholders)

1. **BeginFrame:** pacing wait (`waitForNextFrame`, ADR 0003).
2. **Input:** sample and timestamp input (ADR 0010).
3. **FixedTick** (0..N times): PrePhysics, Physics, PostPhysics.
4. **Update:** variable-rate gameplay; the camera and player path use the
   latest input.
5. **Animation.**
6. **LateUpdate:** final camera; physics bodies' render transforms
   (interpolated by default, ADR 0003).
7. **RenderPrepare:** culling, extracting what the renderer needs.
8. **RenderSubmit:** record and submit through Slag, late-latch write,
   present.
9. **EndFrame:** deferred destruction, stats.

## Why

Named phases give plugins and the editor a structure; before/after
constraints are robust where order numbers aren't; sorting at build time
keeps the per-frame cost at zero. The step cap prevents the "spiral of
death" (a slow frame causes more physics steps, which slow the next frame,
which causes more steps).

Most of the multi-core gain for ECS loops comes from inside systems, for a
fraction of the complexity of automatic scheduling between them.

The pacing wait comes first so input is sampled right after it: sampling
before the wait would make every frame start with input already stale by
the length of the wait.

Given up, for now: automatic parallelism between systems, and changing the
schedule every frame.

## Amendment (2026-10-26): the M2 loop driver

Settled with the project owner in M2 step 4. Names (`Crucible.Loop`,
`FrameLoop`, `FramePhase`, `FrameContext`) are placeholders.

- **The loop is already a list of phase objects,** fixed in M2: a
  `FrameLoop` runs `FramePhase` objects in order, each with
  `Run(FrameContext)`, so M3's editable list, constraints, and fixed-tick
  group extend this shape instead of replacing a hand-written loop.
- **M2's default list** is BeginFrame (the pacing wait), Input (poll events;
  an OS quit or closing the main window ends the loop after the frame), and
  RenderSubmit (clear and present).
- **Phases never throw** (ADR 0030): a phase reports a failure status
  through the context, which ends the frame at that phase and the loop with
  exit code 1.
- **No garbage per frame:** the context and its event buffer are allocated
  once; a test checks that a thousand headless frames allocate nothing.
