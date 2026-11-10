# 0079 — The pose runtime

**Status:** Accepted (2026-11-10); M8 step 2 built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

## Context

Step 1 (ADR 0078) imports skeletons and clips. Step 2 plays them: a
character samples its clips into a pose every frame, blends crossfades and
layers, and turns the joints' local transforms into model-space matrices for
skinning (step 3) and for gameplay (a sword in a hand). ADR 0026 set the
outline: native pose buffers outside the ECS, one job per character in the
Animation phase, SoA wide types. Nothing is skinned yet, so poses are seen
through a skeleton overlay.

## Decision

Decided by the owner (2026-11-10):

1. **An explicit skeleton.** `Animator` names its skeleton (a model asset)
   and sits on any entity, normally the character's root; skinned meshes use
   the pose on themselves or their nearest ancestor (step 3). A body and
   armor from different files share one pose.
2. **Crossfades and layers now.** `play` fades the base from one clip to
   another. Up to eight layers over it: **override** (by weight, masked by
   joint subtrees given by name) or **additive** (the difference from the
   layer clip's first sample). State machines (step 4) build on these.
3. **Wide from the start.** Poses are structure of arrays, blocks of
   `wideLanes` joints, sampled and blended with `QuaternionWide` and
   `Vector3Wide`. The tests keep a plain scalar version of the same math and
   compare every joint against it.
4. **A skeleton overlay through debug lines,** in the editor (View >
   Skeletons) and in games: `debug::drawLine`, drawn by a DebugLines render
   step after Tonemap, with no depth test.
5. **Clips play only while the game plays.** "Animation.Update" (advancing
   clips and fades) is a gameplay system; while editing a character holds its
   clip's first sample.

## The runtime

- **Components.** `Animator { skeleton, clip, speed, loop }` (saved; C#
  starts it at speed 1, looping) and `PoseHandle { pose }` (the pool index
  plus one; never saved, like `LoadedMesh`).
- **Caches.** Skeletons (from the MESH file's JNTS, NODE, and NAME) and clips
  (.crclip) are read through the AssetSource (ADR 0076) and kept by ID; a
  clip on a skeleton gets a *binding*: each joint's track, found by name, and
  the clip's first sample for additive layers. `reloadAnimationAsset` (the
  editor after a reimport) forgets one, and poses read it again.
- **The pool.** Poses are kept by (world, entity), outside the ECS. A pose
  whose entity is gone or lost its Animator is freed on the next pass;
  `releasePoses` frees a world's (C#'s `World.Dispose` calls it).
- **Two systems,** both in the Animation phase:
  - "Animation.Update" (gameplay): advances each player's time (wrapping or
    clamping), its crossfade, and its layers' weight fades.
  - "Animation.Pose"* (always, after Update where both run): reads what's
    missing on the main thread, then one job per character samples the base
    (nlerp between the two nearest samples), crossfades, applies the
    layers in order, and runs the hierarchy pass: `model = local × offset ×
    parent's model`, where the offset folds in any nodes between a joint and
    its parent joint (an "Armature" above the root).
- **Schedules.** These are the first native systems that run outside
  play, so the schedule builder changed in two ways. A registered native
  system whose phase a schedule doesn't have is left out of it (a test's
  schedule of two phases), since native systems are registered for every
  schedule. And a constraint naming a native system the schedule leaves out
  (the gameplay "Animation.Update" in the editor's own schedule) is ignored
  rather than an error, so "Animation.Pose" can be ordered after it.
- **Debug lines.** `debug::drawLine(from, to, color)` until the frame ends
  (the "DebugLines.Clear"* system, BeginFrame), at most 32768. The
  DebugLines step pulls them from an upload buffer per surface and frame
  slot (a reserved descriptor slot per surface) as a line list.

## The C# API

`Crucible.Animation` is a namespace, so the calls can't live in a class of
that name; they're static methods on the component (owner, 2026-11-10):
`Animator.Play`, `SetLayer`, `ClearLayer`, `FindJoint`,
`JointModelTransform`, and `DrawSkeletons`*. `Crucible.Debug.DebugLines.Draw`
adds a line. The inspector's asset fields now list sub-assets after their
asset ("Models/Walker.gltf: Walk"), so an Animator's clip can be picked.

## Mapping notes

- Rotations blend by normalized lerp, taking the shorter way (the second
  quaternion negated where the two point apart). Between samples 1/30 s
  apart this is indistinguishable from slerp and much cheaper.
- An additive layer's rotation is `nlerp(identity, sample × first⁻¹, w) ×
  pose`; translations add `(sample − first) × w`; scales multiply by
  `lerp(1, sample / first, w)`.
- Masks: each entry is a joint and its subtree at a weight; later entries
  win; a layer without entries is unmasked.

## As built

- crucible/Animation.h and Animation.cpp (core), crucible/DebugLines.h and
  DebugLines.cpp, shaders/crucible/debugLines.slang, the DebugLines step in
  RenderSteps.cpp. A new memory tag, `MemoryTag::Animation`*.
- C#: Crucible.Engine/Animation/AnimationComponents.cs, Debug/DebugLines.cs,
  `RenderSteps.DebugLines`; the editor's View menu.
- Tests: `PoseTests` (the first sample while editing, sampling between
  samples, speed and clamping, crossfades, cuts, the bind pose, joints by
  name, the pool, missing assets, skeleton lines, reloads), `LayerTests`
  (full and masked overrides, overlapping masks, weight fades, additive),
  `DebugLineTests`; the pose tests run against the Walker imported in memory
  and served through a test asset source.
