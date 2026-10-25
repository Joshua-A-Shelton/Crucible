# 0012 — Data model

**Status:** Accepted (2026-10-25).

## Context

How game-world state is represented, and how code acts on it, shapes
speed (goal 1), hackability (goal 7), the editor, serialization, and hot
reload. ADR 0011 already decided that native code identifies game objects
by IDs and never holds references into game assemblies, and ADR 0006 that
boundaries are crossed per frame or per batch, never per object.

## Options

- **(1) Object model / scene tree** (Godot nodes, Unity GameObjects,
  Unreal Actors). Intuitive, editor-friendly, natural for one-off logic.
  But objects are scattered in memory, updated through virtual calls, and
  GC-managed in C#; and if transforms live natively while scripts touch
  them per object, every access crosses the boundary (Unity's classic
  `transform.position` cost).
- **(2) ECS** (Bevy, flecs, EnTT, Unity DOTS). Entities are IDs,
  components plain data packed by type, systems loops over component
  sets. Cache-friendly, parallelizable, easy to serialize, and IDs match
  ADR 0011. But it has a learning curve, hierarchies need design work, and
  event-driven logic fits it poorly.
- **(3) Hybrid:** ECS for the world, with object-like authoring and plain
  C# for high-level logic.

## Decision

1. **Hybrid.** ECS is the world data model: everything that exists in a
   scene is an entity. The editor shows the world as a hierarchy built
   from parent/child relationships, so authoring feels like a scene tree.
2. **Blittable components live in native memory,** shared by C++ and C#
   systems with no copies. C# reaches them through source-generated typed
   views (`Span<T>`-style), so the boundary is crossed once per query or
   batch, not per entity.
3. **Managed components are allowed and pay-for-use.** The native
   archetype stores an `int` index into a per-type C# pool; native code
   never holds managed references. Unused managed components cost nothing
   measurable, and the profiler should be able to show that.
4. **Systems register into frame phases** (ADR 0003). Structural changes
   (adding or removing components, creating or destroying entities)
   during iteration are deferred through a command buffer and applied at a
   safe point.
5. **Logic that isn't about world entities is plain C#.** Crucible doesn't
   push it into ECS. Rule of thumb: if it has a place in the world
   (position, drawn, simulated every frame), it's an entity; if it's game
   rules, bookkeeping, or flow (turn order, inventory, quests, dialogue),
   it's ordinary C# that refers to entities by ID.

## Why

Native-memory blittable components let C++ and C# systems share data
without copying or per-entity calls, which satisfies goal 1 and ADR 0006
by construction. Plain C# for non-world logic avoids ECS's classic misuse:
polling, every frame, state that changes a few times a minute (a turn
queue as an entity plus a system).

**Managed components: index, not `GCHandle`.** Storing a `GCHandle` in
the native column also works and keeps managed components in the same
archetype tables, but every access is a handle-table lookup plus a cast,
every handle is a GC root that lengthens every GC pause, handles must be
allocated and freed, and a handle to a game type keeps the game assembly
from unloading (ADR 0011). An index into a per-type pool keeps the same
uniform storage with a typed array access, one GC root per type, slot
reuse instead of `Alloc`/`Free`, and native memory that only ever holds
integers.

Costs that remain when managed components are used: queries that include
one are slower (native arrays plus pool lookups, not packed together), GC
pressure, and on hot reload they must be serialized and recreated, while
blittable components survive in native memory if their layout is
unchanged.

Given up: the object model's directness for one-off logic, which is
covered instead by plain C# (point 5).

## Decided later

- Storage strategy: archetypes (fast iteration, slower add/remove) or
  sparse sets (the reverse).
- Build our own ECS or use a library (flecs and EnTT are MIT-licensed C++
  options).
- Pools that hold structs containing references (contiguous, no
  per-component allocation), alongside class components.
- How plain C# services are registered, found, and saved.
- Hierarchy and relationship representation.
- Blittable component layout changes across hot reload (migration).
