# 0034 — Change detection

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

Most of the world doesn't change every frame. Transform propagation only
needs to recompute world matrices whose local transform (or a parent's)
changed; rendering only needs to re-upload changed data; the editor's
inspector only needs to refresh changed values. Without change detection,
every system processes everything every frame. ADR 0023 left it to a later
decision; this is it (M3 decision G). It must fit decision E: 16 KB chunks,
per-component columns, chunk iteration.

## Options

- **A version per column per chunk** (Unity Entities): requesting write
  access to a column stamps it with the current version; a query skips
  chunks whose column hasn't changed since the system last ran; a separate
  per-chunk structural version changes when entities are added or removed.
  Very cheap, but coarse: one changed entity marks its whole chunk.
- **Ticks on every component value** (Bevy): "added" and "changed" ticks
  per component instance, set on each write through a mutable reference.
  Precise; 8 bytes per component instance, a cost on every write, and
  wrapped access types, awkward for C# spans.
- **Dirty state per table column** (flecs).
- **Explicit dirty tags:** a `Dirty` component the user adds. Structural,
  so slow, and easy to forget.
- **None in v1.**

What counts as a change: requesting write access, or an explicit
`markChanged` call.

## Decision

1. **Each chunk column carries a change version,** and each chunk a
   structural version that changes when entities are added or removed. A
   query can ask for only the chunks whose columns changed since a given
   version (typically the system's last run).
2. **Requesting write access is the change:** a writable column (a C++
   non-const pointer, a C# `Span<T>`) stamps it; read-only access (a const
   pointer, `ReadOnlySpan<T>`) doesn't.

## Why

Per-chunk versions cost a few bytes per chunk and nothing per entity, and
fit chunk iteration exactly: the decision happens where a system already
asks for a column. Stamping on write access makes change detection
automatic, so a forgotten call can't hide a change, and C#'s span types
already distinguish read from write.

Given up: precision. One changed entity makes its whole chunk (up to a few
hundred entities) look changed, and a system that asks to write but writes
nothing still marks it; systems that need exact per-entity changes keep
their own state.

## Amendment (2026-10-29, ADR 0050): the loop advances versions

Nothing advanced the world's version until M5 step 4, so "changed since
my last run" couldn't work. Now:

- **The frame loop advances the main world's version after every
  system.** Each system runs at a version of its own. Whatever comes after
  it (other systems, sync points, edits between frames) is stamped newer.
- **Each system gets the version of its previous run on that world:**
  C# `FrameContext.LastRunVersion`, C++ `SystemContext::lastRunVersion`.
  It is 0 on the first run, which counts everything as changed. A system's
  own writes don't count as changes to it.
- **Stamps are atomic** (relaxed), so jobs may write different entities of
  one chunk through `writeComponent`.
- **`changedSince(world, entity, component, version)`** (C#
  `World.ChangedSince`) answers for an entity's chunk.
