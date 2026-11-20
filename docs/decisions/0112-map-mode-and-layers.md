# 0112 — Map mode and layers

**Status:** Accepted (2026-11-20). Supersedes ADR 0108's editing (its
amendment included), and its "one map is one layer with one tileset".
Names added are marked `*` until the owner settles them (docs/naming.md).

## Context

The owner's try of ADR 0108's redesigned editing hit a bug at once (a
tileset made with Create > Tileset from a 250x250 image didn't import: the
texture's block-compressed usage needs whole 4x4 blocks, and only the
Tiles panel's drop made it a sprite), and found the editing still built
around the engine, not the person making a level: a layer was an entity
of its own with a file of its own, painting needed that entity selected,
and a map drew from one tileset. The owner asked for an editor more like
Tiled or RPG Maker, with no cost at run time.

## Decision

Decided by the owner (2026-11-20):

1. **Map mode.** Edit Map (the Inspector's button on a map or a layer, the
   hierarchy's menu) or Create > 2D > Tilemap switches the editor to a
   layout of its own: the **Palette*** on the left, the viewport (its
   toolbar holding the map tools, framed straight at the map, the floor's
   grid and the selection's outline off), **Layers*** over the Inspector
   on the right. It stays whatever is clicked until **Done*** (or Escape
   with nothing to let go of); the layout before it comes back, and is the
   one saved if the editor closes in map mode.
2. **Layers are child entities.** A map is an entity with
   **`TilemapLayers`*** (naming its `.crtilemap`) and a child per layer,
   each child's `Tilemap` drawing one layer by **`layer`*** (the layer's
   id) and, if the layer collides, its own `Collider2D` of shape Tilemap.
   Sorting, tint, physics layers, and hiding a layer in the game are the
   child's ordinary components. The Layers panel lists them top first:
   clicking one paints on it, **Visible*** (its Tilemap's tint opaque or
   see-through, in the game too), **Collides*** (its Collider2D),
   double-click to rename (the entity too), **Add Layer***, Up, Down,
   Delete (the last stays). A layer whose entity was deleted or moved away
   outside map mode is offered back: "Layer 2 has no entity"*,
   **Re-create***, **Delete Layer***.
3. **Several tilesets, one tile size.** A map lists its tilesets; any
   layer mixes any of them; all share the map's tile size and pixels per
   unit, so the map is one grid (import refuses others, saying which).
   Each tile carries its texture to the GPU, so a layer mixing tilesets
   costs nothing more to draw (ADR 0108 guessed an item a texture).
4. **One way to make a tileset: New Tileset*.** The Palette's + tab (New
   Tileset…, or one of the project's of the map's size), a texture dropped
   on the Palette, or the Assets panel's Create > Tileset… open one
   dialog: the image (an image that doesn't import as it's set is made a
   sprite on choosing it, so it shows), Tile Width, Tile Height, Margin,
   Spacing, the grid seen over the image, and in map mode the map's tile
   size required. **Create*** makes the tileset beside the image, the
   image made a sprite (without opening the Sprite Sheet Editor), and adds
   it to the map. A tab's menu: **Edit Grid…***, **Remove from Map***
   (refused while its tiles are painted).
5. **Tools like a map editor's:** **Stamp*** B, Rectangle R, **Fill*** F,
   **Eraser*** E, **Select*** S (a region: Ctrl+C makes it the brush,
   Ctrl+X too and empties it, Delete empties it, dragging it moves it),
   Flip X and Flip Y (X, Y); the right button dragged over painted cells
   picks them up as the brush (Tiled's); dragging over the Palette picks a
   block. Each change saves and imports the map; a stroke, a move, a cut
   is one undo step.
6. **Collision in the Palette.** **Collision*** on shows each tile's
   shapes over it: clicking a tile makes it solid (Full) or clear again;
   the presets, Custom, Clear, and Flip X and Y apply to the tiles chosen.
   **Show Collision*** draws the colliding layers' merged outlines over
   the map.
7. **Maps from before.** A `.crtilemap` of ADR 0108 (`Tileset`, `Cells` of
   `[x, y, tile, flips]`) reads as one layer, "Layer 1", of id 0, so its
   entity draws as before. Edit Map on such an entity moves its Tilemap,
   Collider2D, and RigidBody2D to a child and gives it TilemapLayers, as
   one undo step ("Convert to Layers"*).

Auto-tiling (terrain brushes, resolved while painting into ordinary
tiles) is its own step after the owner's try.

**Leaving, made plain** (2026-11-20, after the owner's try didn't find
Done): the Palette's top bar names map mode with its own **Done** ("Map
Mode — Done (or Escape) to leave"*), the View menu has **Leave Map
Mode*** (on only in map mode), and leaving puts the viewport's camera
back where it was before map mode.

## How it works

- **The file** (`.crtilemap`): `Tilesets`* (IDs), `Layers`* bottom first,
  each `Id`*, `Name`*, `Cells`*, a cell `[x, y, tileset, tile, flips]`
  (`TileCell.Set`*, the tileset by its place).
- **The import** (TMAP version 2, crucible/TilemapFile.h): INFO
  (`TilemapInfo`: tile size, cell size, counts), TXTR* (the textures, each
  once), LAYR* (`TilemapLayer`*: id, its cells', its loops' ranges), CELL
  (`TilemapCell.texture`* into TXTR), LOOP, PNTS. Collision merges layer
  by layer (`TilemapImporter.Outlines` takes each cell's own tileset).
- **Drawing:** the map's cache holds every layer; tiles build once all its
  textures are ready, layer by layer; a Tilemap gathers its layer's runs.
- **Colliding:** a Tilemap collider's chain is its entity's map and layer
  (the layer riding in the resolved collider's unused `pointCount`); the
  chain cache keys on both; a reload reads every layer in use again.
- **The editor:** `MapEditor`* (Shell.Map) holds the open map, its
  tilesets and textures, the layer being edited, the tool, the brush
  (`BrushTile`*: place, tileset, tile, flips), the region; the viewport
  routes its buttons and keys to it in map mode. Shell.Tilemaps.cs keeps
  the layers' entities in step with the file (each edit with its file
  change as one undo group), and the file with the map entity (renamed,
  duplicated with its layers pointed at the copy, deleted on asking).
- `TilemapLayers` is aligned to 4 in C++ (`alignas`), as C# aligns an
  AssetId: a component of nothing else would otherwise register with
  another alignment on each side.

## Consequences

- Run-time cost is unchanged: a layer is drawn and collides as a map
  entity did, and mixing tilesets costs nothing.
- Box2D's chains can't be sensors, so a layer's collider can't be a
  trigger (the physics warns, as before): water as a trigger needs another
  shape. Open item.
- The scene shows a map's layers as children (collapsed under it); edits
  to them outside map mode are caught when the map is opened.
- Replaced: the Tiles panel, `TilePainter`, `TileTool`, Use Another Map,
  `CreateTilesetFor`, the old Create > Tileset.
- Tests: importer (layers and tilesets round trip, a map from before
  layers, two layers and textures imported apart, a tile size refused);
  native (layers drawn apart and a layer mixing textures on every
  backend; a collider colliding with its own layer only); the editor's
  `launcher.jit.tilemaps` rewritten (61 checks, the window saved as
  tilemaps.png in map mode).
