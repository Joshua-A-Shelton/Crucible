# 0036 — Systems and the schedule

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

ADR 0013 made the frame an ordered, editable list of phases. Systems
register into a phase with before/after constraints, sorted once when the
schedule is built. A fixed-tick group runs 0..N times per frame with a step
cap, and every phase ends in a sync point. M2 built the loop as a fixed
array of `FramePhase` objects (ADR 0013's amendment). M3 decision I settles
the phase names, what a system is, how systems are registered from C# and
C++, and the details ADR 0013 left open.

## Options

**What a C# system is:**

- **An abstract class** with a name and `Run(context)`: one virtual call per
  system per frame. State lives in ordinary fields, and the base class can
  hold engine state (enabled, timing).
- **An interface:** the same call, and any base class is allowed. Engine
  state must live in the schedule, not on the system.
- **Structs through generics** (Unity's `ISystem`): the call can be
  devirtualized, but a schedule of different system types needs a generic
  wrapper class per type, which brings the virtual call back. Unity's
  reason for structs (Burst compiles the system) doesn't apply here.
- **Delegates:** the lightest to write. State hides in closures, there are
  no lifecycle hooks, and the name must be passed separately.

A system runs once per frame, so tens to hundreds of calls, each a few
nanoseconds against microseconds of work. Dispatch cost doesn't decide this.
Chunk jobs differ: `IChunkJob` is a struct because it runs per chunk inside
hot loops (ADR 0035).

**Registration:**

- An explicit builder call per system, made at startup.
- Attributes found by reflection: this conflicts with NativeAOT.
- Attributes read by a source generator at build time, which emits the
  builder calls.

**C++ systems:**

- A native registry of function pointers that the C# builder pulls in, so
  C++ and C# systems sort into one order.
- A native block per phase that C# calls at one point: simpler, but C#
  can't order systems around individual C++ systems.

**Sync points:** buffers owned by the loop (one per world), buffers owned by
systems and registered with the loop, or no automatic application.

**A throwing C# system:** report it and disable it; report it and keep
calling it (Unity); or end the loop (ADR 0030's rule for phases).

## Decision

1. **The default phases are ADR 0013's list:** BeginFrame, Input, FixedTick
   (PrePhysics, Physics, PostPhysics), Update, Animation, LateUpdate,
   RenderPrepare, RenderSubmit, EndFrame. Empty phases cost nothing.
2. **Everything in a frame is a system in a phase.** A phase is a named,
   ordered list of systems followed by its sync point. M2's pacing wait,
   input poll, and present become engine systems in BeginFrame, Input, and
   RenderSubmit, so a game can order its own systems around them or replace
   them.
3. **A C# system is an abstract class:** `Run(context)`, an optional
   `Start` and `Stop` for setup and teardown, and a name that defaults to
   the full type name. The base class holds an `Enabled` flag and timing
   for the editor. Only parameterless constructors are allowed.
4. **The schedule is built once, then immutable.** A builder takes phases
   (insert before or after, replace, remove) and systems. Building sorts
   each phase by its constraints; ties break by name, ordinally, so the
   order never depends on registration order. The loop then runs flat
   arrays, with no sorting, lookups, or garbage per frame. A constraint
   naming a system that doesn't exist fails the build, with an error naming
   it.
5. **C# systems register through attributes and a source generator, now.**
   The builder stays the underlying mechanism: the generator writes builder
   calls, and engine systems, C++ systems, and hand-written registration
   use the builder directly.
   - `[System(Phase = "Update", After = new[] { typeof(MoveSystem) })]`
     marks a class: `typeof` constraints catch typos at compile time, and
     strings name C++ systems.
   - Per game assembly, the generator emits a registration method with one
     `AddSystem` call per marked class (`new T()`), which ADR 0011's
     assembly attribute points at (JIT, editor) and its generated bootstrap
     calls (AOT). Plain code, so it's NativeAOT-safe and reloads with the
     game.
   - Misuse is a compile error: the attribute on a class that isn't a
     system, is abstract, or has no parameterless constructor.
   - The generator is a Roslyn incremental generator in
     `tools/Crucible.Generators` (netstandard2.0, as Roslyn requires). Its
     one dependency is `Microsoft.CodeAnalysis.CSharp` (MIT), used at build
     time only and never shipped in a game. Tests drive it with Roslyn's
     own `CSharpGeneratorDriver`, so testing adds no package.
6. **C++ systems register into a native registry:** a name, a phase, a
   function pointer, user data, and before/after names. The C# builder
   pulls them in, sorts them with the C# systems, and calls each through a
   `delegate* unmanaged`. C++ systems are in-engine for now; how C++ code
   from outside the engine registers stays with the C++ extension mechanism
   (M7, docs/open-items.md).
7. **The loop owns one command buffer per world:** the main world by
   default, plus any world registered with the loop. Systems record through
   the context. Every phase end applies them all, and so does every step of
   a fixed-tick sub-phase. The native buffers already take recording from
   every worker at once (ADR 0023's amendment), so one per world is enough.
8. **The fixed tick defaults to 60 Hz, with at most 4 steps per frame;**
   both can be changed per game. Time owed beyond the cap is dropped: the
   game slows briefly instead of spiraling. The context gives systems the
   fixed step and how far the frame is between two ticks, for interpolation
   (ADR 0003).
9. **A C# system that throws is reported and kept running:** each exception
   is caught at the system's boundary (a `try` costs nothing when nothing
   throws), reported with the system's name, and the frame continues with
   the next system. The system runs again next frame, so it recovers on its
   own once the cause is gone, as in Unity. Exceptions from parallel jobs
   inside a system reach it the same way, rethrown on the caller (ADR
   0014's amendment). Engine systems keep ADR 0030's rule: they never throw,
   and they report failure through the context.

## Why

One mechanism for every step of the frame keeps the loop small and lets
games replace any part of it, the hackability goal (ADR 0006). Classes fit
what systems are: objects with state, a lifetime, and fields the editor can
show. Building once keeps the per-frame cost at a loop over arrays.

The generator gives games declarative registration without reflection, so
it works the same under JIT, hot reload, and NativeAOT, and it starts ADR
0011's generated plugin registration. Keeping the builder underneath means
nothing depends on the generator. Sorting C++ and C# systems together is
what lets the M3 demonstration order systems freely across languages.

One buffer per world, applied at every phase end, gives ADR 0013's sync
points without per-system bookkeeping. Keeping a throwing system running
favours an editor where a mistake in one system shouldn't stop the whole
game. The cost is noise: a system failing every frame reports every frame.

Given up, for now: run conditions (predicates deciding whether a system
runs), optional constraints for soft dependencies between plugins,
automatic parallelism between systems (ADR 0013), and systems with
constructor arguments.
