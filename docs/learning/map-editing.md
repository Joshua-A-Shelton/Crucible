# Map editing

A learning note from ADR 0112: how map editors such as Tiled and RPG
Maker lay out their work, and what Crucible's map mode took from them.

## A mode, not a selection

Painting tiles is long, focused work: hundreds of strokes on one map.
Map editors give it a workspace of its own instead of tying it to
whatever is selected. Tiled opens a map as a document; RPG Maker switches
to map mode for the map chosen in its tree. Crucible's first editor
painted while a tilemap entity was selected, so clicking anything else in
the scene stopped it. Map mode is a layout that stays until Done, whatever
is clicked: the palette, the layers, and the tools are always where they
were.

## Layers

A level is several layers: ground behind the player, decoration, a canopy
in front. In Tiled they're one list in one map. Crucible keeps each layer
an entity of its own, a child of the map's, because an entity already
does everything a layer needs: its own sorting layer and order, its own
tint, its own collider and physics layers, turning it off in the game. The
Layers panel shows them as a map editor would, top first, and keeps the
entities in step: adding a layer adds a child, moving it moves the child
and swaps the two layers' order in their sorting layer, renaming it
renames the child. What the panel can't control is someone deleting a
layer's entity in the hierarchy, so map mode checks when it opens and
offers it back.

## Why one tile size

A map is one grid. A cell is a square of the map whatever tileset its
tile comes from, and collision merges in whole pixels of that grid, so a
16-pixel tile beside a 32-pixel one has no shared edge to cancel. Tiled
lets bigger tiles hang over their cell, which then needs its own rules for
sorting and collision; RPG Maker has one size. Crucible requires one size
(and one pixels per unit) for every tileset of a map, and says which
tileset differs.

## Mixing tilesets costs nothing

Each tile carries the index of its texture in the bindless heap, as each
sprite does, so tiles from two textures draw in the same batch. A layer
mixing three tilesets is drawn exactly as one using one.

## Stamps and picking up

A brush is a stamp: a block of tiles, from a palette drag or picked up
off the map. Tiled's right-drag to pick up is the quickest way to repeat
what's already painted (a doorway, a tree), so map mode has it. Select
copies a region into the stamp too, or moves it.

## Collision where the tiles are

RPG Maker sets passability per tile in its palette, not in a separate
window. Map mode's palette has a Collision switch: on, it shows each
tile's shape over it, and a click makes a tile solid or clear. Slopes and
odd shapes are a preset or a custom outline away, and Show Collision draws
the merged outlines over the map to check what the player will meet.

## One way in

Two ways to make the same thing drift apart: the first editor's Create >
Tileset didn't make the texture a sprite while dropping a texture did, so
one of them made tilesets that didn't import. New Tileset is now the only
way, whichever button opens it.
