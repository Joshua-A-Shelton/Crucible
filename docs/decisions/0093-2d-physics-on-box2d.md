# 0093 — 2D physics on Box2D

**Status:** Accepted (2026-11-11); M9 step 6a built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

## Context

ADR 0027 chose Box2D v3 for 2D, behind Crucible's own API. M9's plan had
step 6 as "2D bodies, colliders, and queries behind a 2D contract of the
same shape, on the job system". Box2D v3.2.0 (2026-10-18, MIT) has the same
API style as Box3D: task callbacks, world ray and shape casts and
overlaps, a mover, and joints.

## Decision

Decided by the owner (2026-11-11):

1. **Box2D v3.2.0**, pinned by tag and SHA-256, built by its own CMake
   target `box2d`, its samples and tests off, rechecked before every
   release (open-items).
2. **2D entities have their own transform:** `Transform2D` (position,
   angle, scale, depth), parented like `LocalTransform`; the transform
   system makes the same `WorldTransform` from it, so everything that
   draws today draws 2D entities. An entity has one or the other
   (`LocalTransform` wins if both); either kind parents either. The
   editor's handles work in the XY plane for it.
3. **2D gets everything 3D has, plus chains**, split into steps: 6a (this
   one) Transform2D, Box2D in the core, bodies, colliders (box, circle,
   capsule, polygon of up to 8 points), queries, contacts, and the
   editor's drawing; 6b joints and characters; 6c chain colliders from a
   JSON asset.

## How it works

- **The contract** (crucible/Physics2D.h, `crucible::physics2d`, C#
  `Crucible.Physics2D`) mirrors 3D's: `RigidBody2D`, `Collider2D`, the
  systems "Physics2D.Sync" (PrePhysics), ".Step" (Physics), ".WriteBack"
  (PostPhysics), ".Interpolate" (Update), a replaceable `Physics2DService`
  (`raycast`, `shapeCast`, `overlap`, `contacts`), and `bodyPose`. It shares
  3D's `BodyKind`, `ContactKind`, `QueryFilter`, and the project's 32
  layers. The binding generator now capitalizes a letter after a digit in
  namespace names, so `physics2d` is `Physics2D` in C#.
- **The backend** (Box2dPhysics.cpp) is built as Box3D's is (ADR 0085):
  one Box2D world per ECS world, its tasks on the job system, its memory
  through Crucible's allocator under `MemoryTag::Physics`; bodies synced by
  kind (dynamic bodies teleported, kinematic ones sent with
  `b2Body_SetTargetTransform`, static ones moved), the game's own writes
  told apart from physics' by what was last written; dynamic bodies written
  back and interpolated between ticks (angles the short way round); layers
  through the same custom filter; queries and contacts as 3D's. A body's
  pose is its world matrix's XY position and its angle; its Transform2D's
  scale shapes its collider and its depth is left alone. Box2D updates a
  body's mass as shapes are added, so a RigidBody2D's mass scales Box2D's
  mass data afterwards.
- **Polygons** keep their points in the component (`Vector2 points[8]`,
  `pointCount`), Box2D making their convex hull; fewer than 3 make no
  shape. The serializer gained fixed arrays for this (the binding
  generator's `[InlineArray]` mirrors: a JSON array of all the items, read
  back up to the length; the inspector shows them, never resizing:
  `FieldKind.FixedArray`*).
- **The editor** draws 2D colliders with View > Colliders and 2D queries
  with View > Queries; its handles write a Transform2D's position, depth
  (Z), angle (the turn about Z), and scale.

## Known limitations

- Reparenting a 2D entity in the hierarchy keeps its Transform2D as it is
  (its world place moves with the new parent); 3D entities keep their
  world place.
- Create Empty makes a 3D entity; a 2D one is made by adding a
  Transform2D (and removing the LocalTransform).
- As in 3D, a dynamic body without a shape doesn't move.

## As built

- cmake/CrucibleDependencies.cmake (Box2D), crucible/Transform.h
  (`Transform2D`), crucible/Physics2D.h, Box2dPhysics.cpp; the serializer's
  fixed arrays (`FieldKind.FixedArray`*, `SerializedField.FixedLength`*);
  BindGen's `Names.Namespace`*.
- C#: `Transform2D` (a scale of 1), `RigidBody2D`, `Collider2D` (defaults
  as 3D's), `Physics2D` (`Raycast`, `ShapeCast`, `Overlap`, `Contacts`*),
  `RigidBody2D.Pose`*, `Collider2D.DrawColliders`*.
- Tests: `Transform2DTests`, `Physics2DBodyTests`, `Physics2DQueryTests`,
  `Physics2DContactTests` (native), `Physics2DTests` (C#, JIT and AOT).
- For trying it by hand: the Walker's Physics2D.crscene: a ground, a ramp,
  walls, a stack of crates, a ball rolling down the ramp, a capsule and a
  wedge falling and turning, a bouncy ball, and a trigger zone.
