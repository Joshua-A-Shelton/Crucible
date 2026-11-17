# 0095 — Chain colliders

**Status:** Accepted (2026-11-16); M9 step 6c built, then tried by hand by
the owner in the Walker's Chains2D scene (2026-11-16). Names, including those
added while building (marked *), settled by the owner as drafted
(2026-11-16, docs/naming.md).

## Context

M9 step 6 ends with chains: Box2D's chain shape, a run of line segments of
any length for terrain, caves, and level outlines. A polygon collider is
capped at 8 points and boxes laid end to end catch things at their joins
("ghost collisions"); a chain's segments know their neighbors, so a ball or
character crosses from one to the next smoothly. Box2D's rules: one-sided
(solid on the right of the points' direction), no mass (static or
kinematic bodies), never a sensor, a loop or open (an open chain needs a
ghost point past each end), one material or one per segment. The owner
chose (2026-11-16) a Chain shape on Collider2D, Box2D's winding, and point
handles in the editor now.

## Decision

- **The shape.** `ShapeKind2D::Chain`, with Collider2D's `chain`: an asset
  ID (the picker offers chains only). It uses the collider's offset,
  friction, restitution, and its body's layers. Its points are in the
  entity's space and scale with it; mirrored (one negative scale) the
  points run the other way, so the solid side stays the one drawn. A chain
  on a dynamic body, or marked as a trigger, logs a warning and makes
  nothing. An open chain's ghost points are its end segments extended
  straight on. One material for the whole chain.
- **The asset.** An authored `.crchain` (JSON: `$guid`, `Points` as
  `[x, y]` pairs, `Loop`) imported by the "Chain" importer into a `.crchn`
  (a CHAN file, crucible/ChainFile.h: `INFO`, `PNTS`). The import fails,
  saying where, with fewer than 2 points (3 for a loop) or neighbors 5 mm
  or closer (Box2D's linear slop). 2D physics reads a chain through the
  asset source when a collider first uses it and keeps it for every world;
  `reloadChainAsset`* (called by the editor after each import) reads it
  again, and the colliders using it rebuild at their next sync.
- **Winding.** Box2D's: solid on the right of the direction the points
  run. Ground runs right to left; a counterclockwise loop is a solid
  island, a clockwise one a cave. View > Colliders draws a tick at each
  segment's middle on its solid side.
- **Point handles.** Edit Points (a button under Collider2D's Chain field,
  or P in a viewport) turns them on for the selected entity's chain, and
  hides the transform handles. Drag a point to move it in the entity's
  plane (Ctrl snaps to 0.25 m in its space); drag a segment's middle dot to
  insert a point there; click a point to select it, and Delete removes it
  (never below 2, or 3 for a loop; a removal or drag that would put
  neighbors too close is refused). Each edit is one step in the editor's
  undo history ("Move Point", "Insert Point", "Delete Point") that writes
  the `.crchain` (its other keys kept, one point a line) and imports it at
  once, so every collider using it follows. These steps don't mark the
  scene modified: undo steps gain `ChangesScene`*, and the scene's
  modified state counts only steps that do. Selecting something else, or
  the collider no longer being that chain, turns the handles off.
- **Create > Chain** in the Assets panel's menu makes "New Chain.crchain"
  (numbered if taken) beside the asset clicked, or in the folder: two
  points of ground, solid below.
- **Mirrors, for every 2D shape.** Building this found that a 2D entity's
  world scale lost its sign, so a mirror read as half a turn (a mirrored
  wedge came out turned). A mirrored world matrix is now a negative X scale
  with the turn it has, as a Transform2D scales before it turns, which
  write-back keeps.

## Known limitations

- Points are edited one at a time; no box selection, and no editing the
  `Loop` flag from the editor (it's in the file).
- Chain edits made while playing stay in the file, but leave the undo
  history at Stop, as the scene's own edits do.
- One material per chain.

## As built

- crucible/ChainFile.h (`chainFileKind`, `chainFileVersion`,
  `minChainPointSpacing`*, `ChainInfo`*), crucible/Physics2D.h
  (`ShapeKind2D::Chain`, `Collider2D::chain`, `reloadChainAsset`*).
- Box2dPhysics.cpp: `ChainData`*, `chainData`*, `readChain`*, `chainOf`*,
  `freeChains`*, `Body::chain`*/`chainVersion`*; `poseOf` keeps mirrors.
- C#: `ChainImporter` (`Read`*, `Build`*, `MinPointSpacing`*),
  `ChainImportSettings`; the editor's `ChainHandles` (with its undo step
  `ChainEdit`*), `UndoStep.ChangesScene`*, `Shell.CreateChain`*,
  `Shell.Chains`*, `Shell.Reimport`'s `quiet`*.
- Tests: `Chain2DTests` (native), `ChainImporterTests` (C#),
  `ChainHandleChecks` (CTest `launcher.jit.chain-handles`*, which saves
  `chain-handles.png`*); `Physics2DBodyTests` gains "A mirrored 2D
  collider is mirrored and not turned".
- For trying it by hand: the Walker's Chains2D.crscene: rolling hills
  (Hills.crchain) walled at both ends, a cave (Cave.crchain) with a ball
  bouncing inside, a Patroller walking the hills, balls and a crate.
