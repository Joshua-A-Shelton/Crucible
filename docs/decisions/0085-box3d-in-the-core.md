# 0085 — Box3D in the core

**Status:** Accepted (2026-11-11); M9 step 1 built, then tried by hand by
the owner in the Diorama (a prop given a collider and body falling onto
the ground's, View > Colliders, Stop putting it back). Names, including
those added while building, settled by the owner as drafted
(docs/naming.md).

## Context

ADR 0027 chose Box3D for 3D physics, behind Crucible's physics API, stepped
in the fixed tick with native ECS sync. ADR 0075 defined that API (the
components, "Physics.Sync", "Physics.Step", "Physics.WriteBack", and a
service) with a null implementation. Step 1 puts Box3D behind it for
bodies, primitive colliders, contacts, and sensors; meshes, queries, the
character mover, and joints follow in steps 2 to 5.

## Decision

Decided by the owner (2026-11-11):

1. **Box3D pinned at a master commit,** `f36d061` (2026-10-20), at least
   two weeks old, by commit and SHA-256: v0.1.0 (2026-06-30) is the only
   release and four months behind. Rechecked before every release
   (docs/open-items.md).
2. **Interpolation into the transform.** "Physics.WriteBack" keeps each
   dynamic body's last two tick poses; "Physics.Interpolate"* (Update, every
   frame) sets its LocalTransform between them by the frame's fixed alpha.
   Rendering needs nothing new; gameplay reading the transform sees it up to
   a tick behind, and asks `bodyPose` for the tick's exact pose.
3. **Contacts hold the frame's ticks.** `contacts()` returns the begin and
   end events of every tick since the last frame (the first tick of a frame
   starts the list afresh), so a once-a-frame system misses none.
4. **What the game writes moves bodies by kind:** a dynamic body is
   teleported (its velocities kept); a kinematic body is given the new pose
   as a target, reached over the tick so it pushes what it meets
   (`b3Body_SetTargetTransform`), and held still when it isn't moved; a
   static body is moved in place.

## How it works

- **Build.** Box3D through its own CMake (single precision; its samples,
  tests, and validation are top-level only), position-independent for the
  shared core, its warnings silenced, and listed for NativeAOT links. All
  its memory goes through `b3SetAllocator` to Crucible's allocator under
  `MemoryTag::Physics`*. Its tasks run as jobs at frame priority: each
  `enqueueTask` runs one, each `finishTask` waits for it (up to 64 a step;
  past that, inline).
- **Worlds.** One Box3D world per ECS world, made by the first sync, gravity
  -9.81 m/s², freed by `releasePhysics`* (C#'s `World.Dispose` calls it).
- **Bodies.** Every entity with a `Collider` and a `LocalTransform` has one:
  static without a `RigidBody`, else of its kind. Made at the entity's world
  pose (computed from the LocalTransforms then, since world transforms
  propagate later in the frame); removed when the entity or its Collider
  goes.
- **Shapes.** Box (an offset box hull), sphere, capsule, with friction,
  restitution, and `isTrigger` as a Box3D sensor; sizes multiplied by the
  entity's world scale (spheres and capsules by the largest axis), the shape
  rebuilt when the Collider, the scale, or the layers change. Mesh and
  Convex make no shape yet (step 2).
- **Mass.** Box3D's mass from the shapes at density 1, scaled to the
  RigidBody's mass, inertia with it; recomputed whenever shapes change, for
  every kind (Box3D won't step a body whose mass is out of date).
- **Telling writes apart.** Each body remembers what the physics last wrote
  into its LocalTransform and velocities; a value that differs is the
  game's, and is pushed in. So the interpolation's own writes never read as
  teleports.
- **Layers.** The contract's rule is that two bodies collide when either
  one's mask has a layer the other is on; Box3D's own test needs both. Each
  shape's 64 category bits carry its layers (low 32) and mask (high 32), its
  Box3D mask passes everything, and a custom filter applies the contract's
  rule. Queries (step 3) test the low 32 bits.
- **Write-back.** Dynamic bodies' poses and velocities into `RigidBody` and
  `LocalTransform`, through the parent's world transform when the entity
  has a parent (its own scale kept).
- **Events.** Contact begin (with the first manifold point and normal, A to
  B) and end; sensor begin and end, marked `trigger`.
- **The rest of the service** answers as before until its step: queries find
  nothing, and `moveCharacter` moves straight through.
- **`drawColliders`*** (C# `Collider.DrawColliders`), from the components and
  world transforms, so it works while editing; the editor's View >
  Colliders draws them in green.

## Found while building

- Box3D at this commit has a spherical joint (`b3SphericalJointDef`, with
  cone and twist limits): ball-and-socket and swing-twist, which its README
  doesn't list. Step 5's design revisits "unsupported for now". (Step 5, ADR 0092, built them on it.)

## As built

- src/native/core/Box3dPhysics.h* and .cpp; Physics.cpp's default service;
  crucible/Physics.h (`interpolateSystemName`*, `BodyPose`*, `bodyPose`,
  `drawColliders`, `releasePhysics`*, the reworded `contacts`).
- C#: `RigidBody.Pose`, `Collider.DrawColliders`; the editor's View >
  Colliders (`View.Colliders`*, `Shell.ShowsColliders`*).
- Tests: `PhysicsBodyTests`, `PhysicsSyncTests`, `ContactEventTests`,
  `PhysicsInterpolationTests`, on tests/native/PhysicsTestSupport.h*.
