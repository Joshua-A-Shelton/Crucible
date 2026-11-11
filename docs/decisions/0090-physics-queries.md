# 0090 — Physics queries

**Status:** Accepted (2026-11-11); M9 step 3 built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

## Context

The physics contract (ADR 0075) declared `raycast`, `shapeCast`, and
`overlap`, each taking a layer mask; until now the null physics answered
them, finding nothing. Box3D (ADR 0085) has world queries for all three:
`b3World_CastRay`, `b3World_CastShape` (a point cloud with a radius), and
`b3World_OverlapShape`, filtered by category and mask bits, with a
callback per shape found.

## Decision

Decided by the owner (2026-11-11):

1. **A `QueryFilter` in place of the layers parameter** (a contract
   change): `layers` (the layers it hits), `ignore` (an entity left out,
   the one casting it), and `triggers` (whether trigger colliders count).
   `defaultQueryFilter()` (C# `QueryFilter.Default`): all layers, nothing
   left out, no triggers.
2. **Triggers aren't hit unless asked**, so a line-of-sight ray isn't
   stopped by a pickup's zone (Godot's default; Unity's is the other way).
3. **Queries see the last tick's world**, what Box3D holds: a body the game
   moves this frame is seen there after the next Physics.Sync (Unity's
   default). No world, no hits: while editing there's nothing to query.
4. **The editor draws them:** View > Queries, off by default.

## How it works

- **Layers.** A shape's Box3D category bits hold its layers (low 32) and
  mask (high 32), with every mask bit set (ADR 0085's "either" rule runs in
  the custom filter). A query's Box3D filter has all category bits and its
  layers as its mask, so Box3D's query test reduces to "the shape is on one
  of the query's layers"; the shape's own mask doesn't count.
- **Triggers and the ignored entity** are filtered in the callbacks
  (`b3Shape_IsSensor`, the shape's user data, its entity).
- **Rays.** `b3World_CastRay` with a closest-hit callback (returning the
  fraction to clip the ray). Box3D reports a ray starting inside a collider
  as a hit at fraction 0; those are skipped, so the ray goes on to the next
  collider (as `b3World_CastRayClosest` does). A mesh's back faces are
  never hit (Box3D).
- **Shape casts.** Box, Sphere, or Capsule as Box3D's proxy: a box's eight
  corners, a sphere's center, a capsule's two cap centers along its Y, each
  rotated, with the radius. One touching something at its start hits it at
  distance 0. Mesh and Convex aren't query shapes (nothing hit).
- **Overlaps.** Each entity once (an entity's shapes found together), the
  count all of them, as many written as fit.
- **The view.** While View > Queries is on, the editor tells the core to
  keep the frame's queries (`recordQueries`*), and after the game's systems
  draws them (`drawQueries`*): a ray's or cast's path in green, red where it
  hit, the hit point and its normal, a cast's shape where it starts and
  stops, an overlap's shape. Up to 4096 a frame are kept.
- **C#.** `Physics.Raycast`, `ShapeCast`, and `Overlap` take the `World`,
  normalize the direction, and default the filter.

## As built

- crucible/Physics.h (`QueryFilter`, `defaultQueryFilter`, the calls'
  `filter`, `recordQueries`*, `drawQueries`*); Box3dPhysics.cpp's queries
  (`queryFilter`*, `admits`*, `makeProxy`*, `Closest`*, `keepClosest`*,
  `Overlaps`*, `gather`*, `QueryRecord`*).
- C#: `Physics` (`Raycast`, `ShapeCast`, `Overlap`), `QueryFilter.Default`;
  the editor's `View.Queries`, `Shell.ShowsQueries`.
- Tests: `RaycastTests`, `ShapeCastTests`, `OverlapTests`,
  `QueryFilterTests` (native), `PhysicsQueryTests` (C#, JIT and AOT).
