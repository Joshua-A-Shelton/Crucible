# 0050 — Transforms and cameras

**Status:** Accepted (2026-10-29). Names settled by the owner (docs/naming.md).

## Context

Step 5 draws meshes, so every drawn entity needs a world matrix, and the
frame needs a camera. Scenes and prefabs already give entities a C#
`LocalTransform` (ADR 0045) and a hierarchy (ADR 0033); C++ render systems
read components directly (ADR 0012). ADR 0003 left late latching
"tentatively yes". M5 step 4. Decision R covers reverse-Z with an infinite
far plane, late latching, and where `LocalTransform` is declared.

## Options

**Depth:** standard Z (near 0, far 1); reverse-Z with a far plane; or
reverse-Z with an infinite far plane.

**Late latching:** build it now; build for it but implement it later; or
drop it.

**LocalTransform:** stays a C# struct, read by C++ through a hand-kept copy
of its layout (or propagated in C#); or declared in C++ and mirrored into
C#.

**Which camera renders:** the highest priority; or a camera set per surface.

## Decision

1. **Reverse-Z with an infinite far plane.** Near maps to depth 1 and
   infinity to 0. Floats are densest near 0, which offsets the perspective
   divide's crowding of far depths, so precision is even across distance and
   there is no far plane to tune. Depth buffers clear to 0 and test Greater.
   Orthographic cameras are reverse-Z too, with a far plane.
2. **Late latching: ready, not built.** Each frame's camera data is written
   to one buffer at one point, just before the frame's passes are added, so
   latching can later overwrite it without changing the render-step API.
   Today a frame is recorded and submitted at once, so latching would gain
   almost nothing; revisit when input feeds the camera.
3. **`LocalTransform` is declared in C++** (`crucible/Transform.h`) and
   mirrored into C# as `Crucible.Scenes.LocalTransform`. A C# partial adds
   `[Serialized]` and `Identity`. `WorldTransform` and `Camera` are declared
   the same way.
4. **The camera with the highest `priority` renders;** among equals, the one
   with the lowest entity index. Cameras for particular surfaces
   (splitscreen, render to texture) come later.

## Details

- **`WorldTransform { Matrix4x4 matrix; }`:** object to world for row
  vectors, `local * parent`. It is derived, so it is never saved.
- **Propagation** (`crucible.TransformPropagation`, C++, first in
  RenderPrepare of the default schedule) runs two passes over entities that
  have both components, one job per chunk:
  1. Each world matrix is set to the entity's local matrix (scale, then
     rotation, then translation). Writing the columns stamps their change
     versions for the whole update.
  2. Each root walks its subtree depth first, multiplying children by their
     parent. A root is an entity with no parent, or whose parent lacks the
     components. Subtrees are disjoint, so they run in parallel. Writes in
     this pass skip the stamp, which pass 1 already made and which would race
     if made from many jobs.

  Everything is recomputed every frame. Skipping unchanged subtrees waits for
  a profile that asks for it: 100k entities take 0.64 ms in a release build
  on 23 workers (the "benchmark" suite).
- **Missing world transforms:** the system gives an entity that has a
  `LocalTransform` but no `WorldTransform` one through the command buffer,
  already placed under its parent. It applies at RenderPrepare's sync point,
  before RenderSubmit draws.
- **On demand:** `updateWorldTransforms(world)` (C# `World.UpdateWorldTransforms`)
  for gameplay that reads a parent it just moved. Otherwise world matrices
  are last frame's until RenderPrepare.
- **Not in the C++ system registry:** every schedule includes every
  registered C++ system (ADR 0036), and a custom schedule may have no
  RenderPrepare. So `ScheduleBuilder.CreateDefault` adds the system through
  its function address (`transformPropagationFunction`), as it adds the C#
  engine systems.
- **`Camera`** (`crucible/Camera.h`, `Crucible.Render.Camera`, saved in
  scenes):
  - `projection` (`CameraProjection`: `Perspective`, `Orthographic`);
  - `fieldOfViewY` (the full vertical angle, in radians);
  - `orthographicHeight` (the full height, in meters);
  - `nearPlane`;
  - `farPlane` (orthographic only);
  - `priority`.

  The camera looks along its entity's +Z with Y up. The view matrix is the
  inverse of its world matrix with scale removed, and the aspect ratio
  comes from the surface.
- **No camera** (or `renderFrame` given no world): a default perspective
  camera at the origin, 60 degrees high, near plane 0.1 m.
- **`ViewConstants`** (`[GpuShared]` in `shaders/crucible/shared.slang`,
  whose C++ mirror is generated for the renderer) holds:
  - `view`, `projection`, `viewProjection`, and their inverses;
  - `cameraPosition` and `time` (seconds since the renderer started);
  - `viewportSize`.

  Each surface keeps one upload buffer per frame in flight, used in the
  render graph's frame-slot order, so `beginFrame`'s wait guarantees the GPU
  is done with it. The frame's buffer is imported and published under the
  blackboard key `View` (`viewKey()`). Steps bind it as the root constant
  buffer (`register(b1)`).
- **Math:** `Matrix4x4::perspectiveReverseZInfinite(fieldOfViewY,
  aspectRatio, nearPlane)` and `orthographicReverseZ(width, height,
  nearPlane, farPlane)`.
- **`renderFrame(surface, world, ...)`** takes the world (nullable) whose
  camera it draws from.

## As built (M5 step 4)

- Tests:
  - Propagation composes three levels, places entities under
    transform-less parents as roots, stamps change versions, adds missing
    world transforms, and matches a serial walk over 200 chains of 20 on 4
    workers (also clean under ThreadSanitizer).
  - Camera choice follows priority and index; the view ignores scale;
    perspective and orthographic depth run 1 to 0.
  - On every backend, a compute shader copies the bound `ViewConstants`
    back over three frames (every frame slot), and the copy is compared
    with the CPU's.
  - C#: on-demand updates, and scenes save `Camera` and `LocalTransform`
    but not `WorldTransform`.
