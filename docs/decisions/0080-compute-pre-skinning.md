# 0080 — Compute pre-skinning

**Status:** Accepted (2026-11-10); M8 step 3 built, then tried by hand by
the owner (the Walker's mesh walking while the game plays). Names, including
those added while building, settled by the owner as drafted
(docs/naming.md).

## Context

Step 2 (ADR 0079) poses skeletons. Step 3 makes meshes follow them. ADR
0026 chose compute pre-skinning: a character is drawn several times a frame
(depth prepass, shadow cascades and tiles, opaque, outlines, the editor's
pick), so its vertices are skinned once into a buffer and every pass draws
that buffer as it would a static mesh, with no skinned variant of any
material.

## Decision

Decided by the owner (2026-11-10):

1. **Per view.** A Skinning render step, first in the schedule, skins the
   skinned instances that view or its shadow views draw, into a transient
   buffer in that frame's render graph. The editor's viewport and Game panel
   each skin their own; culling keeps it to what's seen.
2. **Bounds from the pose.** At load, each joint gets a reach: how far the
   vertices it influences are from its bind-pose origin. Each frame a skinned
   instance's bounds are its posed joints grown by their reaches, in its
   mesh's space, so a raised arm is never culled and a crouch doesn't waste
   a margin.
3. **A draw per instance.** Skinned instances get batches of one, each
   pointing at its own deformed vertices.

## How it works

- **Loading.** Models keep their skin's streams (JIDX, JWGT) in their GPU
  buffer, and its joints, names, origins, and reaches on the CPU
  (`MeshData::jointIndices`, `jointWeights`, `joints`, `names`,
  `jointOrigins`*, `jointReach`, `skinInfluences`*).
- **Finding skins.** Building a view's draw list, every mesh instance whose
  mesh is `Skinned` looks for a pose: a `PoseHandle` on its entity or its
  nearest ancestor. One found, its palette is written: for each of the
  mesh's joints, *inverse bind × posed joint × (the pose's entity's world ×
  the mesh entity's world⁻¹)*. Skinned vertices are then in the mesh
  entity's own space and draw through its transform, so picking, the
  selection outline, and the instance's world matrix work unchanged; and
  the mesh entity's own transform cancels out, as in glTF, where a skinned
  mesh's node transform is ignored. A skinned mesh without a pose (no
  Animator above it) draws as it was bound.
- **Joints by name.** The mesh's joints are matched to the pose's skeleton
  by name, once per model and skeleton (`poseJointIndex`*,
  `poseSkeletonKey`*; rebuilt after a reload), so meshes from other files
  can follow one pose. A joint the pose lacks keeps its vertices as bound.
- **The step.** One dispatch per skinned instance (`skinVertices`, 64
  vertices a group) blends each vertex's palette rows by its weights and
  writes position, normal, tangent (its handedness kept), and outline normal
  in the model's own formats, stream by stream, into "SkinnedVertices".
- **Drawing.** A skinned batch has `DrawBatch::deformedOffset`;
  `DrawConstants::deformed` names the skinned vertices' slot, and the
  material shaders read those four streams from it (`geometryBuffer`*), the
  rest (UVs, colors) from the model. Every pass that draws meshes declares
  reading SkinnedVertices, so the graph orders it after Skinning.
- **Shadows.** A skinned caster's share of its shadow view's hash includes
  its palette's, so cached shadow tiles redraw when it moves.

## Details

- The palettes are an upload buffer per frame slot, three `float4` rows per
  joint (the instance transforms' layout), grown as needed.
- Batch keys gained a bit for skinned instances (model indices are now
  below 2²³), with the skin's index in the mesh's place.
- Each surface reserves six more resource-heap slots (palettes, and the
  skinned vertices' read and write views, per frame slot); the reserved
  range below loaded assets grew from 1024 slots to 2048
  (`firstAssetDescriptor`*).
- Normals are turned by the blended matrix's 3x3 and renormalized, which is
  right for rotations and uniform scale; non-uniformly scaled joints would
  need the inverse transpose.
- Reaches are per model, not per mesh: a model with several skinned meshes
  bounds each by every influencing joint.

## As built

- shaders/crucible/skinning.slang; the Skinning step (RenderSteps.cpp);
  skins gathered in DrawList.cpp; joint streams and reaches in
  RenderAssets.cpp; `animation::poseJoints`.
- Tests: `SkinningTests` (a two-joint quad, posed and moved, drawn where
  skinning on the CPU puts it, on every backend; its mesh's scale ignored),
  `SkinnedCullingTests` (culled by its bind bounds until posed, then by its
  posed ones).
