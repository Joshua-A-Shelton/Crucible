# 0032 — C++ memory and containers

**Status:** Accepted (2026-10-26). Names are placeholders (docs/naming.md).

## Context

Everything M3 builds allocates: job queues, ECS chunks, command buffers,
system lists. An engine usually wants control over where that memory comes
from, for three reasons:

- **Visibility:** per-subsystem numbers in the profiler ("the ECS uses
  340 MB").
- **Policy:** frame arenas or fixed-size pools where profiling says the
  general heap is the bottleneck.
- **Predictable performance, debug builds included:** MSVC's debug
  standard library checks every iterator, which can make an ECS loop 10–50
  times slower than in release, and games must stay playable in debug.

Exceptions are off (ADR 0030), so a standard container that runs out of
memory can't report it: it terminates.

## Options

How engines handle it:

- **Unreal:** a replaceable global allocator (`FMalloc`) and its own
  containers (`TArray`, `TMap`) with allocator policies; no STL in engine
  code.
- **Godot:** its own containers (`Vector`, `HashMap`) and
  `memnew`/`memdelete` hooks.
- **EASTL:** STL-shaped containers with simple allocator instances, written
  because standard allocators were awkward for games.
- **flecs:** OS-API hooks for `malloc`/`free`, small containers of its own.
- **Slag (its ADR 0066):** `AllocatorCallbacks` passed at creation; its own
  `Vector` and friends, whose growing operations return `false` on failure
  (`OutOfHostMemory`); only non-allocating standard headers in `src/`.
- **Smaller engines:** the standard containers, sometimes `std::pmr`.

The choices, each with its alternatives:

- **Containers:** Crucible's own in the core runtime and `std` elsewhere;
  own everywhere; `std::pmr` everywhere (a virtual call per allocation,
  slow debug iterators, still terminating on failure); plain `std`.
- **Allocator:** one tagged allocator; untagged hooks; the global heap.
- **Out of memory:** fatal; or a `Result` from every growing operation, as
  in Slag.
- **Where containers come from:** start from Slag's code; write fresh; or
  make Slag export them publicly.

## Decision

1. **The core runtime uses Crucible's own containers.** In
   `src/native/core` (jobs, ECS, renderer, platform): Crucible's small set
   (`Vector`, `SmallVector`, `HashMap`, pools, added as needed), allocating
   through Crucible's allocator, plus non-allocating standard headers
   (`<cstdint>`, `<cstddef>`, `<cstring>`, `<span>`, `<string_view>`,
   `<type_traits>`, `<utility>`, `<new>`, `<bit>`, `<atomic>`, `<mutex>`,
   `<thread>`, `<algorithm>`, `<cstdio>`, and similar). Tools, tests, and the
   launcher may use the whole standard library.
2. **One allocator, tagged by subsystem.** Every core allocation goes
   through Crucible's allocator interface with a size, an alignment, and a
   tag (`Jobs`, `Ecs`, `Render`, ...). The launcher sets it once at
   startup; the default is aligned `malloc`/`free` with per-tag counters for
   the profiler. Slag's `AllocatorCallbacks` are routed into it under
   `Render`, so Slag's memory is counted too. Arenas and pools are built on
   it later, where profiling asks for them.
3. **Running out of memory is fatal.** The allocator never returns null:
   on failure it reports the size and tag and terminates (through the crash
   path, ADR 0011, once it exists). Containers therefore can't fail to grow,
   and their operations need no checks. Large requests that are expected to
   fail sometimes (loading a huge asset) use a separate `tryAllocate` that
   returns null. Slag keeps its own rule (it reports `OutOfHostMemory`),
   which Crucible's allocator never triggers.
4. **The containers start from Slag's.** Slag's `Vector` (and its other
   containers as they come) is copied into the core and adapted to
   Crucible's allocator and fatal out-of-memory, with a note of where it
   came from (zlib, the same author). Each library stays self-contained, and
   Slag's public API stays C-shaped (Slag ADR 0007).

## Why

Owning the containers where the engine spends its time gives fast debug
builds, per-subsystem memory numbers, and room for arenas later, at the
cost of a few hundred lines of code. Restricting the rule to the core
keeps tools and tests free to use what's familiar.

Making out-of-memory fatal matches what Unreal and Godot do: a game rarely
recovers from running out of memory mid-frame, and a check on every
`pushBack` in gameplay code would be noise. The `tryAllocate` escape covers
the cases where failure is genuinely expected.

Given up: the standard containers' familiarity in core code, recovery
from out-of-memory in general, and a single shared copy of the containers
across Slag and Crucible.
