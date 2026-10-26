# 0023 — ECS storage

**Status:** Accepted (2026-10-25).

## Context

ADR 0012 put blittable components in native memory, exposed to C# as
typed spans, with managed components as index columns. ADR 0013 defers
structural changes to phase ends; ADR 0014 parallelizes as loops over ECS
chunks. C# game code defines components the C++ core never sees at compile
time.

## Options

**Storage:**

- **Archetypes** (Unity DOTS, flecs, Bevy's default): entities with the
  same component set share a table, one column per component, split into
  fixed-size chunks. Fast multi-component iteration; adding or removing a
  component moves the entity to another table.
- **Sparse sets** (EnTT, Bevy's opt-in): one packed array per component
  plus an entity index. Cheap add/remove; multi-component queries iterate
  the smallest set and look up the rest by index.

**Build or library:**

| Option | License | Fit |
|---|---|---|
| Our own | zlib | Built to ADRs 0012–0014 |
| flecs | MIT | Mature, runtime components, relationships, prefabs; brings its own systems, pipeline, scheduler, reflection, and serialization |
| EnTT | MIT | Sparse sets built around compile-time C++ types and heavy templates |

## Decision

1. **Chunked archetype tables,** with **opt-in sparse storage per
   component type** for components added and removed constantly
   (event-like markers).
2. **Component types are registered at runtime** (size, alignment, ID),
   from C# metadata as well as C++. Hard requirement, including layout
   changes across hot reload (ADR 0012).
3. **Crucible builds its own ECS,** with flecs as the reference design and
   fallback (its storage already supports runtime-registered components).
4. Hierarchy and relationships, and change detection, get later ADRs.

## Why

An archetype chunk is a set of contiguous spans in matching order, which
is exactly what ADR 0012 hands to C# and what ADR 0014 splits across
cores. Archetypes' main cost, moving entities on structural changes, is
already batched at phase ends (ADR 0013). Opt-in sparse storage covers
the churny components that would otherwise move entities between tables
every frame.

Our own fits the earlier decisions more tightly than any library and is an
instructive part of the engine. flecs would overlap ADRs 0013, 0014, 0017,
and 0021; EnTT fits runtime-registered C# components and the C-shaped
boundary poorly.

Given up: a mature ECS on day one. A basic archetype core is a few
thousand lines; cached queries, relationships, and change detection come
on top. If it becomes a time sink, flecs's storage is the fallback.
