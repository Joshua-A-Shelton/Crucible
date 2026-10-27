# 0023 — ECS storage

**Status:** Accepted (2026-10-25). Amended 2026-10-27 (M3): IDs, chunks,
columns, and queries (decision E).

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

## Amendment (2026-10-27): IDs, chunks, columns, and queries (M3 decision E)

Settled with the project owner in M3 step 6. Names are placeholders.

- **Entity IDs are 64 bits: a 32-bit index and a 32-bit generation,** as in
  Unity and Bevy. The generation is bumped when an index is reused, so a
  stale reference is detected (it would take 4 billion reuses of one slot to
  fool it); 0 is the null entity. EnTT's 32-bit IDs (20 + 12 bits) were
  given up: its version wraps after 4096 reuses of a slot.
- **Archetypes store entities in fixed 16 KB chunks,** as in Unity: stable
  addresses, one pool of equal blocks (tagged `Ecs`), chunks as natural job
  batches, and a chunk's columns small enough to stay in cache while a
  system streams through them. Growable per-archetype tables (flecs, Bevy)
  were given up: growing moves memory.
- **Columns are per component:** a `Vector3` column is an array of
  `Vector3`, so C# gets `Span<Vector3>` and components keep their C++
  layout. Splitting fields into x, y, z arrays would spare wide math its
  transpose, but after step 3 that transpose is cheap (wide math is still 2×
  scalar), and it would cost typed spans everywhere
  (`docs/learning/simd-and-data-layout.md`).
- **The core query API is chunk iteration:** a query (required, optional,
  and excluded components) yields chunks, each with a column pointer per
  component and a count; a parallel form runs one job per chunk. C-shaped,
  so C# crosses the boundary once per chunk; typed C# iteration is built on
  it (decision H, step 7). Per-entity callbacks were given up: not
  C-shaped, and too costly per entity across the boundary.
- **Worlds are objects:** a `World` holds entities, archetypes, and chunks,
  and there can be several (the editor's edited and playing scenes; tests),
  as in Unity, flecs, and Bevy. Components are registered once and shared
  by all worlds.
- **Structural changes apply immediately when no query is iterating the
  world** (setup, tools, tests, sync points); during iteration, systems
  record them in command buffers applied at the phase's sync point (ADR
  0013). Validation builds report an immediate change during iteration.

As built (`crucible/Ecs.h`, M3 step 6a):

- A chunk is one 16 KB block: the entity IDs, then one column per component,
  each aligned for its type; an archetype's capacity is the most entities
  whose columns fit. Tags (empty components) take no column.
- Archetypes stay compact: every chunk is full but the last. Destroying an
  entity, or moving it to another archetype, moves the archetype's last
  entity into the hole, and an emptied last chunk returns to the world's
  block pool. Archetypes are found by a hash of their sorted components and
  cache their one-component neighbours.
- Queries track archetypes as they appear; iteration hands out chunks, with
  read and write column access (write stamps the change version) and a
  parallel form that runs chunks as jobs.
- Debug builds report a structural change while a query iterates the world
  and abort; until Crucible has a validation layer, that path has no
  automated test.
- Tests include a model check (20,000 random creates, destroys, adds,
  removes, and writes against a reference map) and run clean under
  AddressSanitizer, UndefinedBehaviorSanitizer, and ThreadSanitizer.
- Measured (release, AMD with 23 workers): creating an entity takes about
  40 ns; integrating 100,000 positions takes about 0.2 ns per entity
  sequentially (~25 µs in all), and the parallel form is no faster at that
  size: waking the workers and handing out ~200 chunks costs about as much
  as the work. Parallelism pays for heavier systems; batch size and wake-up
  cost are tuning points for later.

## Amendment (2026-10-27): command buffers and sparse storage (M3 step 6b)

Settled with the project owner. Names are placeholders.

- **Entities created in a command buffer get real IDs at once:** recording
  a create reserves a fresh index (an atomic counter, so any thread can),
  and the returned entity is final, so it can be stored in components
  before playback (flecs reserves IDs similarly). Unity's placeholder IDs,
  replaced at playback, were given up: a placeholder stored in a
  component's value would stay wrong. Reserved indices come only from fresh
  ones; a buffer cleared unapplied releases them with a bumped generation.
- **Playback goes segment by segment:** each worker records into its own
  segment without a lock (other threads share one behind a lock), and
  `apply` plays each segment in recorded order. Which chunks a worker ran
  varies, so entity order from parallel recording can differ between runs;
  determinism isn't a goal (ADR 0027). Unity's sort keys were given up for
  now. Commands on entities that died meanwhile are ignored.
- **Sparse storage is a sparse set per component per world** (as decided
  above): dense arrays of values and entities plus each entity's position,
  so adding and removing are O(1) and never move the entity. A sparse
  component's ID carries a marker bit, so every call knows where it lives
  without a lookup. Queries don't see sparse components (validation builds
  refuse them); they're read per entity or walked with `forEachSparse`.
  Unity's enableable components (a bit per entity in the chunk) were the
  alternative considered.
- Tested with randomized checks against reference maps, recording from
  parallel chunk jobs, and AddressSanitizer, UndefinedBehaviorSanitizer,
  and ThreadSanitizer runs.
