# 0035 — The ECS from C#

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

ADR 0012 puts blittable components in native memory, shared with C# without
copies, and managed components (C# classes, or structs holding references)
in per-type C# pools that the archetype indexes with an `int`. ADR 0023
requires components registered at runtime, from C# too, surviving layout
changes across hot reload. M3 decision H settles how.

## Options

**Registering C# component types** (the native registry needs a name,
size, and alignment; C# needs each type's `ComponentId` at hand for typed
spans):

- A Roslyn source generator that finds component types and emits their
  registration and typed helpers at build time (ADR 0006 plans generators;
  Unity's Entities uses them heavily). NativeAOT-safe; a generator project
  to build and learn.
- A reflection scan for `[Component]` types at startup: simplest to use,
  but generic reflection conflicts with NativeAOT.
- An explicit generic API, `Ecs.RegisterComponent<T>()`, `T : unmanaged`:
  NativeAOT-safe, no tooling; a generator can emit the calls later.

**Freeing managed component slots** when native code removes the component
(`destroyEntity`, hierarchy destruction, command buffer playback):

- A native-to-C# callback with (component, slot), like the job trampoline.
- A lazy sweep for slots whose entity died or lost the component.

**Hot reload changing a component's layout** (the native registry refuses a
name re-registered with a different size, so nothing is corrupted
silently):

- Field-matching migration: compare old and new fields by name and type,
  register the new layout, and move every value over.
- Reload through scene serialization (needs M4's serializers).
- Refuse changed layouts until play mode restarts.

## Decision

1. **An explicit generic registration API now:** `Ecs.RegisterComponent<T>()`
   with `T : unmanaged`, the ID cached in a static generic class. A source
   generator can emit these calls, and typed iteration sugar, later, with
   the editor's generators.
2. **Managed components are freed through a native-to-C# callback:** native
   code calls one engine trampoline with (component, slot) whenever it
   removes a managed component or destroys its entity, by any path, and C#
   frees the slot at once. Native code still holds only integers. Pools
   cover C# classes and structs holding references alike.
3. **Hot reload migrates changed layouts by matching fields:** old and new
   fields are compared by name and type; the new layout is registered, and
   every value moves over (matching fields copied, new ones zeroed, removed
   ones dropped, type changes logged). Hot reload exists only in JIT and
   editor builds, so reflection is allowed there. Managed pools migrate the
   same way.

## Why

The explicit API gives typed spans and NativeAOT safety today without
building generator tooling ahead of the editor that needs it most. The
callback keeps pools exact (no lingering slots, no sweeps) whichever path
removed a component. Field matching keeps the edit-run loop fast: changing
a component while the game runs keeps the world, instead of restarting it.

Given up, for now: automatic registration (a call per component type), and
migration of field renames (a renamed field is a removed one plus a new
one).

## As built (M3 step 7a)

Settled with the project owner along the way: native objects cross as
opaque handles (ADR 0017's amendment), chunk jobs are `IChunkJob` structs
with a `ref struct ChunkView`, and step 7 is split (7b: migration).

- `Components.Register<T>()`, `RegisterTag<T>()` (C# gives an empty struct
  size 1, so tags are explicit), `RegisterSparse<T>()`, and
  `RegisterManaged<T>()`; components are registered by their full C# type
  name.
- `World` (`IDisposable`): entities; `Get<T>` returns a `ref` into native
  memory (stamping the change version), `Read<T>` a `ref readonly`; `Set`,
  `SetManaged`, `GetManaged`; the hierarchy; versions; queries; command
  buffers.
- `Query.ForEachChunk` and `ParallelForEachChunk` take a struct
  `IChunkJob`; the parallel form gathers the query's chunks once and runs
  them through `JobSystem.ParallelFor`, so no second trampoline was needed.
  `ChunkView.Read<T>()` and `Write<T>()` are spans over native columns.
- Managed components: a pool per type holding values in an array, the
  column holding slot + 1. The release callback is a second
  `[UnmanagedCallersOnly]` trampoline; native code calls it from
  `destroyEntity`, moves to an archetype without the column, and
  `destroyWorld`. The engine's table of pools holds them weakly, so a
  game's pool never keeps the game from unloading.
- Tests (JIT and NativeAOT) cover typed access, tags, sparse components,
  sequential and parallel chunk jobs, change versions, every managed
  release path, command buffers recorded from parallel chunk jobs, the
  hierarchy, and allocation-free iteration; the hot reload test game uses
  its own component, managed component, and chunk job, and still unloads.

## As built (M3 step 7b): layout migration on hot reload

Settled with the project owner: migration is an explicit reload step,
blittable components are matched by flattened leaf fields, managed fields
holding the game's own types are reset, and a changed layout gets a new
`ComponentId` (the old one is retired).

- `GameLoader.Reload(current, path, out notes)` loads the new version
  beside the old, migrates, and only then creates the new version's entry,
  so its first `Register` calls return the migrated IDs (migration sets
  them in the new types' static caches). The caller unloads the old
  version afterwards. Old values are still readable during migration
  because the old version is still loaded.
- Native: the registry lists live worlds, so one call reaches all of them.
  `retireComponent` frees a name for a new layout. `migrateComponent(from,
  to, copies)` gives every entity with `from` a `to`, copying
  (from offset, to offset, size) triples and zeroing the rest; it moves
  each entity through the archetype holding both, which is simple, and a
  reload is rare. `removeComponentEverywhere` handles deleted component
  types. The copies are passed as a `uint32_t` span rather than a struct,
  because the generator only makes exposed structs public in C#.
- Which types migrate: `Components` keeps every registration by name, with
  a weak reference to collectible types. A component migrates if its type
  comes from the old version's load context, including generic
  instantiations over the game's types. Its counterpart is found by
  assembly and full name. Types from the engine or tests are left alone.
- Blittable components are flattened to leaf fields: primitives, enums
  (name and underlying type), pointers, and fixed buffers and inline arrays
  (copied whole). Each leaf's offset is the runtime's own layout, read from
  a small `DynamicMethod` that takes the field's address with `ldflda`.
  `Marshal.OffsetOf` gives the marshaled layout, where a `bool` is 4 bytes.
  Unchanged layouts keep their ID. A blittable type that now holds
  references is removed: the new version must register it as managed.
- Managed components keep their ID, since native code holds only slot
  numbers. The old pool's slots are exported, every value is rebuilt as
  the new type, and the result is imported at the same slots. Each new
  value comes from the parameterless constructor, so added fields get their
  initializers. Fields are matched by name (base classes included) and
  exact type. A field whose type involves the game is reset. So is a value
  whose runtime type is the game's, held behind `object` or an interface.
- What a reload dropped or reset comes back as `MigrationNote`s
  (component, field, change), for the editor to show. The engine has no
  logger yet.
- Not handled:
  - **Storage changes.** Registering a type with a different kind than
    before (for example sparse instead of archetype) throws, in the new
    version's `Register` call. Migration can't see which call the new
    version makes, so changing storage means a restart.
  - **Hidden game objects.** A game object nested inside an engine or .NET
    collection (`List<object>`) isn't seen, and keeps the old version
    loaded. The unload check reports it.
  - **Single assembly.** Games are one assembly, since the loader resolves
    nothing else.
  - **Stale objects.** Queries and command buffers the old version made are
    stale after a reload.
- Tests:
  - **Native:** migration across two worlds, sparse migration, and removal.
  - **Hot reload:** a V1→V2 test game whose components change every way
    listed above. The test checks values, notes, a query over the migrated
    archetypes, and that both versions still unload. Breaking the copy plan
    on purpose makes it fail.
