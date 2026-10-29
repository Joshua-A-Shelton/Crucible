# 0046 — The M4 demonstration

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

M4 is done when importing a glTF, saving the scene as JSON, and reloading
it gives an identical scene, and mirroring tests show no inside-out faces
and correct left and right. Steps 1 to 8 built the pieces: serializers, the
binary format, the asset database, scenes, texture, glTF, and FBX import,
and prefabs. M3's demonstration (ADR 0037) was a sample run by the tests.

## Options

**Form:** a sample program with a test that runs it; or tests only.

**Model:** our own, made lopsided on purpose; or a Khronos sample asset.

**Scope:** everything M4 built in one round trip; or one model instance.

## Decision

1. **A sample program, `samples/SceneRoundTrip`,** run by a CTest test on
   the project checked in beside it, and runnable by hand on any model
   (`--model file.glb`). It prints a report and fails on any failed check.
2. **Our own lopsided model,** made by a stdlib-only Python script
   (`samples/SceneRoundTrip/make_model.py`); the script and its output are
   committed. The model's right arm, forward nose, and red-and-blue texture
   make a mirroring mistake visible, and every part is closed, so
   inside-out faces can be counted.
3. **Everything M4 built:** the model and its texture imported, two model
   instances (one with overrides), an authored `.prefab` nesting a model
   instance, and a component of the sample's own referring into an
   instance.

## Details

- **The round trip:** copy the project to a work folder, import everything,
  build the world, save `Demo.scene`, load it into a new world, compare,
  and save again.
- **Identical** means both:
  - **The same world:** `Scene.SaveUnpacked` writes every entity in full,
    instances expanded, so two worlds are the same when their unpacked
    files are: the same scene IDs, hierarchy, child order, and saved
    component values. The prefab instances' roots must match too.
  - **The same file:** saving the reloaded world gives the same bytes.
- **Mirroring checks**, read from the imported MESH file as a renderer
  would:
  - no inside-out faces at any level of detail: every clockwise face
    agrees with its vertex normals;
  - every part of the model encloses a positive volume, as clockwise
    front faces should;
  - the model's `RightArm` is on +X and its `Nose` on +Z.

  With `--model`, the first is reported and the others skipped: an
  arbitrary model needn't be closed or have those nodes.
- **The report** lists each import's time and output size, the entity
  counts, and each check.

## Why

A program anyone can point at their own model shows the pipeline working
on real input, as M3's sample did for the frame loop, and the test keeps it
working. A model built to be asymmetric catches what a nearly symmetric
sample would hide, needs no third-party licence, and stays small. Running
everything M4 built through one round trip tests the parts together, not
only one by one. Comparing worlds as well as bytes catches both a world
that loads wrong and a file that drifts.

## As built (M4 step 9)

- `samples/SceneRoundTrip` (`Crucible.Samples.SceneRoundTrip`), with its
  project in `Project/` (`Demo.crproject`; the model, its texture, their
  sidecars with fixed GUIDs, and the hand-written `Prefabs/Pedestal.prefab`
  nesting the model and overriding its position and scale). The scene:
  the model, the model again moved, turned, and with its body stretched,
  the pedestal, and a watcher whose `LookAt` targets the first model's nose.
  17 entities from 4 roots; 1,625 bytes of JSON, 7,401 with every instance
  written out.
- `Scene.SaveUnpacked` is new in the engine, for the world comparison.
- CTest runs it as `samples.scene-roundtrip` (label `integration`); run by
  hand, the core's folder goes on the library path:
  ```
  LD_LIBRARY_PATH=build/debug/src/native/core build/debug/artifacts/bin/Crucible.Samples.SceneRoundTrip/debug/Crucible.Samples.SceneRoundTrip --model my.glb
  ```
  (on Windows, the folder goes on `PATH`).
- Checked by breaking things: skipping the winding swap fails the
  inside-out and volume checks on every face; skipping the mirror of node
  translations fails the right-arm check.
- On Blender's Suzanne (`--model`, from ufbx's samples) the round trip
  passes and no face disagrees with its normals, but no LOD beyond LOD 0
  is made: an open item.
