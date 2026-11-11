# 0086 — Mesh and convex colliders

**Status:** Accepted (2026-11-11); M9 step 2 built, amended (Generate If
Missing), then tried by hand by the owner through the editor usability
work (collision generated from the asset browser and the inspector,
colliders landing, Generate If Missing). Names, including those added
while building, settled by the owner as drafted (docs/naming.md).

## Context

Step 1 (ADR 0085) gave Box3D boxes, spheres, and capsules. Levels collide
as their real shape, and props as something closer than a box: the
contract's Mesh and Convex collider shapes, from a model's mesh. Box3D
builds a triangle mesh into a `b3MeshData` (with its own bounding-volume
tree) and a point cloud into a `b3HullData` (at most 128 vertices); both are
self-contained blobs that can be stored and used from memory as they are.

## Decision

Decided by the owner (2026-11-11):

1. **Cooked at import by default:** the model importer writes each mesh's
   Box3D data into the MESH file, and the runtime uses it from memory.
2. **And built at run time, by a call:** `createCollisionMesh` and
   `createCollisionHull` build the same data from geometry the game hands
   over, for meshes made or changed at run time (the owner: dynamic meshes
   will need it). Imported models still need their setting.
3. **A model import setting:** `Collision` (None by default, Mesh, Convex)
   for every mesh in the model, so models that never collide stay small.
4. **Hulls keep at most 32 vertices** by default, `MaxHullVertices` raising it.

## How it works

- **Cooking.** The importers can't reach Box3D (private to the core), so the
  core exports `cookCollisionMesh` and `cookCollisionHull`. The native mesh
  importer, after the MESH sections, cooks each mesh's level 0 (its
  positions, and the indices of its level 0 ranges) into `COLM` or `COLH`: a
  `CollisionEntry { offset, size }` per mesh, then each blob 16-byte aligned
  (size 0: none for that mesh, with an import warning). Sections that are
  optional, so the MESH version stays 3 (it was drafted to bump; readers of
  version 3 files lose nothing); the Mesh importer's version is 7 and the
  FBX importer's 5 (that bump also covers step 4's clip events, which it
  missed), so models reimport.
- **Box3D's format.** Each blob carries Box3D's format version
  (`B3_MESH_VERSION`, `B3_HULL_VERSION`); data whose version doesn't match
  is treated as missing. Moving the Box3D pin to a changed format bumps the
  importers' versions.
- **Winding.** Meshes are one-sided. Crucible's front faces are clockwise as
  seen in its left-handed space, which is the order whose cross product
  points out of the front, Box3D's default winding: so the default, not
  `clockWiseWinding`. (Built first the other way, a ball fell through the
  mesh; the importer's corner swap at mirroring already keeps the cross
  product facing out.)
- **The runtime's data.** Collision data is kept by model, mesh, and kind,
  read from the model's MESH file through the asset source the first time a
  collider needs it, or made by the runtime calls (under an ID "crcoll" and
  a count). A mesh shape refers to its data, so data is counted by the
  bodies using it; `reloadCollisionAsset`* (the editor after a reimport) and
  `destroyCollisionShape` retire it, bodies using it rebuild their shapes
  (with the new data, or none), and it's freed when the last lets go.
- **Shapes.** Mesh: `b3CreateMeshShape` scaled by the entity's world scale
  (non-uniform allowed); not offset (Box3D's mesh shape takes none); static
  bodies only (Box3D collides meshes only there): on a moving body it warns
  and makes no shape, and a shapeless dynamic body doesn't fall (Box3D gives
  it no mass), so Convex is the shape for anything that moves. Convex:
  `b3CreateTransformedHullShape`, cloned, scaled, and offset. A collider
  whose model has no cooked data of its kind warns once and makes no shape.
- **Outlines.** View > Colliders draws a mesh's triangles and a hull's edges
  from the data itself, what Box3D sees.

## As built

- crucible/Physics.h (`cookCollisionMesh`, `cookCollisionHull`,
  `createCollisionMesh`, `createCollisionHull`, `destroyCollisionShape`,
  `reloadCollisionAsset`*), crucible/MeshFile.h (`COLM`, `COLH`,
  `CollisionEntry`); the collision cache and shapes in Box3dPhysics.cpp;
  the native importer's `CollisionImport`, `MeshSettings::collision` and
  `maxHullVertices`.
- C#: `MeshImportSettings.Collision`, `MaxHullVertices`;
  `Collider.CreateMesh`, `CreateHull`, `DestroyShape`; the editor reloads a
  reimported model's collision data.
- Tests: `MeshColliderTests`, `ConvexColliderTests` (runtime data),
  `CollisionCookingTests` (the Walker cooked both ways and served: a ball
  lands on its mesh's head, its hull comes to rest), and
  `MeshImporterTests.TheCollisionSettingCooksEachMesh`.

## Amendment (2026-11-11): Generate If Missing

Decided by the owner after trying editor usability step 1: loading imported
data stays the default, with building at load as a fallback a collider asks
for.

- `Collider::generateIfMissing`* (C# `GenerateIfMissing`, off by default):
  when the model wasn't imported with the collider's kind, its data is built
  at load from the model's level 0 (its positions and, for a mesh, its level
  0 ranges' indices, as the importer does), hulls keeping at most 32
  vertices. It's cached like imported data, marked built, and serves only
  colliders that ask for it, so one that doesn't still has no shape and says
  so. A model is still imported with one kind; the other kind can be built
  at load.
- A build longer than 10 ms says so in the Log (information): "generated a
  model's convex collision for mesh 0 at load (14 ms); set the model's
  Generate Collision to generate it ahead of time"* (worded as amended in
  ADR 0087). The runtime knows assets by ID,
  not by file, so the model isn't named (drafted with its file name).
- The warning for missing data now reads "a Convex collider's model has no
  Convex collision generated (set the model's Generate Collision, or the
  collider's Generate If Missing)" (ADR 0087's second amendment).
- Tested by `CollisionCookingTests`' "Generate If Missing builds collision at
  load from a model imported without it".
