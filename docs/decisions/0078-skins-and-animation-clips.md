# 0078 — Skins and animation clips

**Status:** Accepted (2026-11-09); M8 step 1 built, then tried by hand by
the owner (the Walker placed, its joints left out of the hierarchy). Names,
including those added while building, settled by the owner as drafted
(docs/naming.md).

## Context

M8 animates characters (ADR 0026). Before anything can play, the importers
have to bring in what a character is made of: which joints each vertex
follows and how strongly (the skin), how the joints hang together and where
they were when the mesh was bound to them (the skeleton), and how they move
over time (the clips). glTF and FBX both carry all three, in different
shapes; this step decides the shape Crucible keeps them in. The pose
runtime (step 2) and compute skinning (step 3) read what this step writes.

## Decision

Decided by the owner (2026-11-09):

1. **Clips are assets of their own,** not sections inside the model. A
   character can play clips from other files, and a model's clips change
   without touching its mesh. Each clip is a *sub-asset* of the model it
   came from: an output of the model's import with an ID of its own, kept by
   name in the model's sidecar (below).
2. **Joints aren't entities.** A model's prefab leaves out its joint nodes
   and everything below them; the pose (step 2) holds joint transforms in
   native buffers. Sixty joints per character as entities would be sixty
   transforms through the ECS hierarchy pass for data only the animation
   system reads.
3. **Clips are resampled and simply quantized:** every track sampled at a
   fixed rate (30 per second by default), rotations as 16-bit smallest
   three, translations and scales as 16 bits within each track's range, and
   a channel that never changes stored once. Simple to sample (two samples
   and a lerp), about a quarter of the size of float keys. Revisit with
   curve fitting or ACL-style compression if memory or quality calls for
   it (docs/open-items.md).
4. **16-bit joint indices and 8-bit weights,** 4 per vertex, or 8 as an
   import option for faces and cloth; each vertex keeps its strongest
   influences, renormalized so they sum to 255.
5. **A clip's tracks find joints by name,** so a clip plays on any skeleton
   with joints of the same names (retargeting between different skeletons
   is later work).
6. **Both formats in this step:** cgltf for glTF skins and animations, ufbx
   for FBX skin deformers and animation stacks.

## The formats

The MESH file goes to version 3 (crucible/MeshFile.h):

| Section | Holds |
|---|---|
| `JIDX` | uint16 × 4 (or 8) per vertex: joint indices, into JNTS |
| `JWGT` | unorm8 × 4 (or 8) per vertex: weights, summing to 255 |
| `SKIN` | `SkinInfo`: joint count, influences per vertex |
| `JNTS` | `JointEntry` per joint, each after its parent: parent joint, node, inverse bind matrix, name |

A skinned mesh has `MeshAttribute::Skinned`. A file has one skeleton: the
union of every skin's joints (glTF files with several skins that share
joints are common; conflicting inverse binds get a warning). Joints stay
nodes in NODE too, so their rest transforms and names are in one place.

The clip file, .crclip (kind CLIP, crucible/ClipFile.h):

| Section | Holds |
|---|---|
| `INFO` | `ClipInfo`: duration, sample rate, sample count, track count |
| `TRCK` | `ClipTrack` per joint the clip moves: its name, its constant channels, offsets into the samples, ranges |
| `ROTS` | uint16 × 4 per sample: smallest three plus the dropped index |
| `POSS`, `SCLS` | uint16 × 3 per sample, within the track's range |
| `NAME` | the joints' names |

Sample *i* is at time *i* / rate; the last is at or just past the duration.
Everything is in Crucible's space: mirrored on X like the model (inverse
binds as S·M·S, rotations with y and z negated, translations with x).

## Sub-assets

An importer calls `ImportContext.AddSubAsset(name, kind, version,
extension)` for each extra output. After the import, the database:

- gives each name an ID: the one the sidecar already lists for it, or a new
  one; names no longer made are forgotten;
- writes it to `Cache/Imported/` under a key made from the parent's key and
  the name, so it's up to date exactly when its parent is;
- records it in `imports.json` like any import, so `AssetCache` (ADR 0073)
  reads it with no changes;
- rewrites the sidecar when the list changed:
  `"SubAssets": { "Walk": "…", "Idle": "…" }`.

Sub-assets are found by ID (`Find`, `ImportedFile`, `SubAssetsOf`) but
aren't in `Assets`: the browser shows the model. Importing one imports its
parent. A sidecar copied with its file gets new sub-asset IDs, as its own ID
does.

## Settings

`MeshImportSettings.MaxInfluences` (4; 8 for eight) and `ClipSampleRate`
(30, clamped to 1–240), shared by the glTF and FBX importers. Both
importers' versions went up, so every model reimports once.

## As built

- glTF: `JOINTS_0/1` and `WEIGHTS_0/1`; samplers evaluated as linear (slerp
  for rotations), step, or cubic spline; each animated joint's track starts
  from its rest transform, so channels a clip doesn't animate hold still.
- FBX: each skin cluster's bone is a joint, bound by ufbx's
  `geometry_to_bone`; each animation stack is a clip, every joint evaluated
  with `ufbx_evaluate_transform`. Meshes with more than one skin keep the
  first (warned); blend shapes still warn.
- `samples/Walker`: a 13-joint character written by `make_walker.py`, with
  a "Walk" clip (1 s, feet planted at 0.0 and 0.5 s) for the footstep
  check, and an "Idle".
- Tests: `SkinImportTests` (skeleton order, weights, the rest pose skinning
  back to the bind positions), `ClipImportTests` (durations, sample counts,
  constant channels, and ufbx's 3ds Max skin posed by its clip against
  Max's own exports of frames 5 and 15, within half a percent),
  `SubAssetTests` (stable IDs, the sidecar, importing through the parent,
  the cache, copies, and model prefabs without joints).
