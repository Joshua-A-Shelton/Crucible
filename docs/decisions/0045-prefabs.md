# 0045 — Prefabs

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

ADR 0021 makes prefabs authored JSON that may nest, with property
overrides plus added components or children, applied at the outermost
level only. ADR 0022 and 0043 make an imported model's node hierarchy a
prefab-like asset. Scenes (ADR 0041) already save a world as JSON with
random 64-bit entity IDs, nested children, and entity references written
as those IDs. M4 step 8 builds prefabs on top; step 9's demonstration
needs an imported model placed in a scene.

## Options

**Overrides:** only the fields that differ from the prefab; or whole
components.

**IDs of entities inside an instance:** derived from the instance's ID
and the entity's ID in the prefab, never written; or written per instance
as a table.

**Models:** an imported MESH file instantiated directly, one entity per
node; or a `.prefab` file written by the importer beside the model.

**Transform and mesh components:** added now as placeholders, with M5
building on them; or deferred, leaving models out of step 9.

## Decision

1. **Overrides hold only the changed fields.** Saving compares each
   component's JSON with the prefab's, field by field (nested objects
   recursively; arrays and values whole), and writes the difference.
   Loading writes the instance's current value, merges the difference into
   it, and reads the result, so the generated serializers need nothing new
   and later prefab edits to other fields still arrive.
2. **Entities inside an instance have derived IDs:** the instance root's ID
   is the instance's own; every other entity's is
   `derive(instance ID, its ID in the prefab)`. Nesting composes the same
   way. Overrides and references name entities by these IDs.
3. **Imported models instantiate directly.** A MESH file is a prefab: a
   root entity, then one entity per node, whose ID in the model is a hash
   of its name path, so re-exporting in another order keeps overrides in
   place. Reimporting updates every instance.
4. **`LocalTransform` and `MeshInstance` are added now** as saved engine
   components: position, rotation, and scale (System.Numerics, ADR 0024);
   and the model's asset ID with a mesh index. M5 adds world transforms and
   drawing.

## Details

- **A `.prefab` file** is a scene file with exactly one root entity. The
  asset database indexes it by its `$guid`, like a scene. A prefab whose
  root is itself an instance is a variant of that prefab.
- **An instance in a file:**
  ```json
  {
    "Id": "6c1e9a03d7b4f210",
    "Prefab": "8f2a…",
    "Overrides": {
      "1b44c0de9a7e3f01": {
        "Components": { "Game.Health": { "Value": 50 } },
        "Removed": [ "Game.Loot" ]
      }
    },
    "AddedChildren": { "6c1e9a03d7b4f210": [ { "Id": "…", "Components": {} } ] }
  }
  ```
  A component in `Components` that the prefab entity doesn't have is added
  in full. Added children follow the prefab's own children, and may be
  instances themselves. Each section is left out when empty.
- **Outermost level only:** a file overrides the instances it contains
  directly, wherever in them the target is; a house prefab's overrides on
  its doors are part of the house. Nothing stacks several layers.
- **References** inside a prefab name its own entities; each file is read
  with its own ID map, and overrides are merged in the outer file's IDs.
- **`derive(i, e) = mix(i ^ mix(e ^ 0x9e3779b97f4a7c15))`** with `mix` the
  SplitMix64 finalizer; zero becomes one. A model node's ID is the same
  `mix` over the FNV-1a hash of its name path (`Body/Lid`; the k-th sibling
  with a repeated name gets `#k`), and the synthetic root is 1.
- **`PrefabInstance { Prefab }`** marks the root of each instance the
  outermost file contains, so saving writes it back as an instance;
  removing it unpacks the instance into plain entities. It isn't saved
  itself: the file format carries it.
- **Not expressible, so saving refuses:** removing or reparenting an entity
  that came from the prefab. Reordering the prefab's children isn't kept.
- **A missing prefab, or one that contains itself,** is a load error that
  names it.
- **API:** `Prefab.Instantiate(world, id, library, parent)` creates an
  instance with a new random ID; `Prefab.Save(world, root, id, library)`
  writes a subtree as a prefab; `Scene.Save` and `Scene.Load` take the
  library when the scene holds instances. `PrefabLibrary` reads each
  prefab or model once, through a function the host supplies (the asset
  database's in the editor).

## As built

- `Crucible.Scenes`: `SceneFile` parses a file once, keeping each
  component's JSON as a slice; `SceneLoader` creates a file's entities with
  a frame (its IDs both ways), instancing prefabs file by file;
  `SceneWriter` saves, re-creating each instance's prefab in a scratch
  world under the same IDs and comparing. `Scene.Load` and `Save` keep
  their format for plain entities.
- The engine's components (`SceneEntityId`, `PrefabInstance`,
  `LocalTransform`, `MeshInstance`) are registered when the first `World`
  is created. The engine runs the serializer generator for its own types;
  its registration generators find nothing to emit there.
- `AssetDatabase.Prefabs()` reads `.prefab` files from disk and models
  through their import, importing them first if needed.
- Model nodes sharing a name with a sibling are told apart by order, so
  reordering only those swaps their IDs.
- Tests (JIT and NativeAOT): saving only changed fields, prefab edits
  reaching instances, byte-identical round trips, nested prefabs layering
  each file's overrides, added and removed components and added children,
  per-instance IDs and references, unpacking, refused removals and moves,
  missing and self-containing prefabs, models and reordered nodes; and
  through the asset database, a `.prefab` and an imported FBX in a scene.

## Why

Field-level overrides are what makes a prefab worth having: fix the
prefab, and every instance gets the fix unless it changed that very field.
Merging at the JSON level reuses the serializers as they are. Derived IDs
need no bookkeeping in files and can't drift out of sync with the prefab.
Instantiating models directly avoids a generated file that hand edits
would be lost from. The two components let step 9 put an imported model
into a scene, which is M4's goal.

Given up, for now: removing a prefab's entities from an instance,
reordering them, migrating overridden fields across a component's version
change, entity names (no `Name` component yet), and a faster cached form
of prefabs for runtime spawning (with cooking).
