# 0108 — Tilemaps

**Status:** Accepted (2026-11-19); M11 step 4 built. Editing amended
the same day (see "Amended: editing redesigned"). The design's names were
settled before building; those added while building are marked `*` until
the owner settles them (docs/naming.md).

## Context

ADR 0005 builds tilemaps into the 2D path. Steps 1 to 3 (ADR 0104, 0106,
0107) draw, scale, and animate sprites; a pixel-art level is mostly tiles:
a grid of small pictures from a sheet, which the player walks on. A game
needs to paint them in the editor, draw them cheaply, and collide with
them without catching on the seams between tiles.

## Options

**Map data:** an asset of its own; or saved in the scene.

**Tilesets:** an asset of their own (a texture, a grid, per-tile data); or
the texture's sprite sheet grid (ADR 0107).

**Collision shapes:** none or full squares; presets (slopes, halves);
custom polygons.

**Flips and turns:** flips; flips and 90° turns; neither.

## Decision

Decided by the owner (2026-11-19):

1. **Tilesets are assets:** an authored `.crtileset` naming a `Texture`
   cut by `TileWidth`, `TileHeight`, `Margin`, `Spacing`, with `Tiles`'
   collision `Shapes`, each a list of `Points` in the tile's pixels: any
   outline, several per tile.
2. **Maps are assets:** an authored `.crtilemap` naming its `Tileset` and
   its `Cells` (position, tile, flips). One map is one layer with one
   tileset; more layers are more entities.
3. **`Tilemap`** (C++ `crucible::render2d`, C# `Crucible.Render2D`): `map`,
   `tint`, `sortingLayer`, `orderInLayer`, drawn in chunks through the
   Sprites step and the pixel camera.
4. **Collision** through a static `RigidBody2D` and a `Collider2D` of the
   new shape `Tilemap`, built from the map on its entity.
5. **Flips** on X and Y per cell, mirroring picture and shapes together.
6. **Editing:** Create > 2D > Tilemap and the Tiles panel (as amended
   below; first built as the Tileset Editor, the Tile Palette, Edit Tiles
   (T), and "New Tilemap" in the asset browser).

## Why

Files of their own keep levels' bulk out of scenes, diff readably a row a
line, and let a map be placed twice or in a prefab; a tileset of its own
keeps level-design data (what's solid) apart from the sprite sheet's, and
lets one texture serve several tilesets. Custom shapes are the owner's
choice: slopes, platforms, and odd ledges all come from one tool, rather
than a fixed menu. Colliding through Collider2D keeps one model of bodies,
layers, and materials. Flips double a tileset's reach for two bits a cell;
turns would mostly matter once shapes are more than squares, and wait.

## How it works

- **Importing a tileset** (`TilesetImporter`, into `.crtls`*): the file
  checked (shapes inside their tile, whole pixels, tiles on the texture),
  and a TSET file with its grid. Nothing at run time reads it.
- **Importing a map** (`TilemapImporter`, into `.crtmap`*): everything the
  runtime needs is baked into a TMAP file (crucible/TilemapFile.h*): each
  cell's tile as its corner in the texture, the cell's size in meters (the
  tile's pixels over the texture's pixels per unit), and collision. The
  tileset's file and texture are dependencies: changing either imports
  the map again.
- **Merging collision** (`TilemapImporter.Outlines`*): every painted
  cell's shapes, mirrored with the cell, placed on the map in whole pixels
  and made counterclockwise. Each edge is split where another shape's
  corner (in its cell or the eight around) lies on it, so a slope meeting
  a block still shares its edge; then edges matching a reversed one
  cancel. What's left links into closed outlines, counterclockwise around
  ground and clockwise around holes; where two outlines touch at a corner,
  the walk takes the sharpest left turn so each stays its own; points in
  the middle of a straight run go. Shapes only partly touching keep inner
  edges: still correct, possibly catching.
- **Colliding:** a `Tilemap`-shaped collider reads its entity's map as a
  chain asset: the chain loader reads a TMAP's outlines as several loops,
  one Box2D chain each (a three-point loop gets a fourth, Box2D's least).
  `reloadChainAsset` reads it again after painting.
- **Drawing:** the Sprites step gathers tilemaps with sprites. A map's
  tiles are built once its texture is ready, as quads in the map's space,
  sorted chunk by chunk (16x16 cells), row by row. Without y-sort the
  whole map is one item in its layer; in a y-sorting layer each chunk's
  row is one, sorted by its bottom. Each frame the items are placed by the
  entity's transform (snapped to the pixel camera's grid) and copied into
  the instance buffer; `render2d::reloadTilemap` drops a map read before.
- **The editor:** see the amendment below. Painting (`TilePainter`*)
  writes the map file and imports it as each cell changes, so the map and
  its collision follow the brush; a stroke is one undo step (`TileEdit`*).
  A flipped brush mirrors its arrangement too.

## Known limitations

- **Off-screen chunks** are drawn; culling waits for maps big enough to
  matter.
- **Turns** (90°) per cell, **animated tiles**, **auto-tiling rules**, and
  **importing LDtk or Tiled levels** are later (open items).
- **One tileset a map.**
- **Collision is static:** a map's Collider2D can't be on a dynamic body.

## As built

- Native: crucible/Tilemap.h (`Tilemap`, `reloadTilemap`),
  crucible/TilemapFile.h (`TilesetInfo`*, `TilemapInfo`*, `TilemapCell`*,
  `TileFlip`*, `TilemapLoop`*); Sprites.cpp's `TilemapCache`*, `TileRow`*,
  `TileRun`*, `gatherTilemaps`*; Box2dPhysics.cpp's `ShapeKind2D::Tilemap`,
  `resolvedCollider`*, `makeChain`*, a chain's `loops`.
- C#: `TilesetImporter`, `TilemapImporter`, `TilesetData`*,
  `TilemapData`*, `TileCell`*, `ImportContext.PathOf`*; the `Tilemap`
  component's white tint; `SpriteAnimator` and `Tilemap` registered for
  scenes (`SpriteAnimator` had been missed in step 3).
- Editor (as amended): `TilesPanel`*, `TileGridView`*, `TilePainter`*,
  `TileTool`*, `TilePreset`*; `Shell.CreateTilemapEntity`*,
  `CreateTilesetFor`*, `ShowTiles`*, `ShowTileset`*, `EditTileset`*,
  `SetMapTileset`*, `UseMap`*, `IsMapShared`*, and `Delete(entity,
  deleteMaps)`* in Shell.Tilemaps.cs.
- Tests: C# (TilemapImporterTests*): merging, holes, corners, flips, files
  read and written, a map imported in meters, reimporting on a tileset's
  change, errors. Native: Tilemap2DTests* (resting, sliding without
  catching, loops and holes, reloading) and a drawing test on every
  backend. The editor's TilemapChecks* (CTest `launcher.jit.tilemaps`*).

## Amended: editing redesigned (2026-11-19)

The owner's try found the first editor unintuitive: from a texture to a
map that's painted and collides took nine steps in five places (a texture
set to Sprite; Create > Tileset; shapes drawn and Applied; Create >
Tilemap; a Tilemap added and its map picked; a Collider2D added and set to
Tilemap; Edit Tiles; the palette), in an order nothing pointed to;
painting was a hidden mode; Fill couldn't fill an empty area. Godot (one
node, one panel with paint and tileset tabs) and Unity (one menu item, a
palette window) were looked at.

Decided by the owner:

1. **One Tiles panel**, shown when a tilemap is selected, replacing the
   Tileset Editor, the Tile Palette, and Edit Tiles. At its top, the map
   file (**Use Another Map** points the entity at another), the tileset (a
   texture **dropped** on the panel makes one beside it, cut like its
   sprite sheet grid, the texture made a sprite if it wasn't), and the
   tileset's grid. Two tabs: **Paint** — a toolbar with each tool's
   symbol, name, and key (Paint B, Rectangle R, Erase E, Fill G, Pick I),
   Flip X and Flip Y; choosing a tile (or a block of them) or a tool
   starts painting, Escape stops; while painting, the brush's tiles show
   faintly under the pointer and the grid around the map — and
   **Collision** — presets for the chosen tiles (Full, Half, Slope,
   Shallow Low, Shallow High), Custom (points as before), Clear, and Flip
   X and Y mirroring their shapes, each saved at once as an undo step (no
   Apply). Double-clicking a tileset shows it alone, on Collision.
2. **Maps stay files of their own, made and kept by the editor** (the
   owner weighed keeping cells in the scene, as Godot and Unity do, and
   chose this: collision stays merged at import and scenes are
   unchanged). **Create > 2D > Tilemap** in the hierarchy makes the entity
   and its file, `<entity name>.crtilemap` in a folder beside the scene
   (`Level Tilemaps` for Level.crscene; `Tilemaps` while it isn't saved).
   Duplicating the entity copies the file; renaming it renames the file
   unless another entity (in the scene, or named in another scene or
   prefab) uses it; deleting it asks whether its file goes too ("Delete
   File", "Keep File"). Each file change is part of the edit's undo step.
   Create > Tilemap left the asset browser.
3. **Collision added for you:** the new entity has a static Collider2D of
   shape Tilemap (and a Transform2D); it's a normal collider, removable.
4. **Fill on an empty area** reaches one cell beyond the painted ones;
   Rectangle covers a new area. A map just made, before it has a tileset,
   imports as empty.

The tool keys work while a tilemap is selected, so E and R choose Erase
and Rectangle there instead of the transform handles' Rotate and Scale.

