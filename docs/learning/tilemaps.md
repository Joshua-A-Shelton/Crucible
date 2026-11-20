# Tilemaps

A learning note for M11 step 4 (ADR 0108): building levels from tiles,
drawing them cheaply, and colliding with them smoothly.

## Tiles and tilesets

A pixel-art level is built like a mosaic: a grid where each cell shows one
small picture, a *tile*, taken from a sheet of them, the *tileset*. Grass
top, grass corner, dirt, ledge: an artist draws a few dozen tiles and a
designer paints a whole world with them. Because the same picture repeats,
the art budget stays small and the world can be large.

A tileset is more than a picture: it says how to cut the sheet (tile size,
the margin around it, the spacing between tiles) and what each tile *is*.
Here that's its collision shapes; other engines also store sounds, or
whether a tile is water. A map, separately, says which tile sits in which
cell. Keeping the two apart means fixing a tile's shape fixes it
everywhere it's painted.

## Painting

Painting tools are borrowed from image editors: a brush puts down tiles
(several at once if you picked a block of them in the palette), an eraser
clears cells, a fill floods a connected area, and a picker takes a tile
from the map as the new brush. Flipping a tile mirrors it, so one drawing
of a wall serves both sides of a room.

Fill needs a boundary. Filling an empty area of an infinite grid would
never stop, so it only reaches one cell beyond the area already painted;
a rectangle tool covers a new area.

## Making the editing easy to find

The first version of this editor worked but was hard to discover: a map
took nine steps in five places, in an order nothing pointed to. That's a
common trap for tools: each step made sense alone, but nobody would guess
the sequence. The fix borrowed from Godot and Unity: **one entry point**
(Create > 2D > Tilemap makes everything needed, collision included),
**one place to work** (a panel that appears when a tilemap is selected),
and **visible state** (the tools on a toolbar with their keys, the brush
drawn under the pointer, painting started by choosing a tile and stopped
with Escape instead of a hidden mode).

Files the user never asked for need looking after too. A map is still a
file of its own, but the editor treats it as belonging to its entity:
made with it, copied when it's duplicated (so painting one copy doesn't
change the other — the same surprise as a shared material), renamed with
it, and offered for deletion with it. Each of those file changes is part
of the same undo step as the entity's change, so undo never leaves a
stray file behind.

## Drawing cheaply

A level might have tens of thousands of tiles. Drawing each as its own
object, gathered and sorted every frame, wastes work: tiles don't move.
So the tiles are built once into a list of quads in the map's own space,
in *chunks* (16 by 16 cells), and each frame the whole list is only
placed where the map's entity is. Painting rebuilds them.

Sorting matters for top-down games, where things lower on screen are in
front. A map in such a layer is split into rows, each sorted by its
bottom edge, so a character can stand in front of one row of a tree and
behind the next.

## Colliding without catching

The naive way to collide with tiles is a box per solid tile. It works
until something slides along the floor: where two boxes meet, the
physics sees a tiny corner, and a box sliding across can snag on it.
That's *ghost collision*, a classic platformer bug.

The fix is to collide with the outline of the ground, not the tiles. On
import, every solid tile's shapes are placed on the map and their edges
compared: an edge shared by two shapes (one's right side, its neighbour's
left side) is inside the ground, and both copies are removed. What's left
is the outline, linked into loops. Box2D's *chains* are made for exactly
this: smooth one-sided edges whose joints know about their neighbours, so
nothing catches.

Two details make the matching reliable. Points snap to whole pixels, so
"the same edge" is exactly equal, never nearly. And edges are split where
another shape's corner touches them: a half-height slope meeting a full
block shares only part of the block's side, and splitting there lets that
part cancel.

Which way an outline runs matters too. Going counterclockwise around
solid ground puts the solid side consistently on one hand, which is how a
one-sided chain knows which way is out; holes in the ground run the other
way round.

## Why bake

The map file keeps what a designer edits (which tile where); the imported
file keeps what the game needs (where each tile's picture is, how big a
cell is in meters, the collision outlines). Working it out once, on
import, keeps loading quick and the runtime simple, and an import that
fails can say exactly what's wrong.
