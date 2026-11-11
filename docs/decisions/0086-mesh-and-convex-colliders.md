# 0086 — Mesh and convex colliders

**Status:** Accepted (2026-11-11); M9 step 2 built. Names settled by the
owner as drafted (docs/naming.md); names added while building are marked *.

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
