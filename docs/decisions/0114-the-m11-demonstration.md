# 0114 — The M11 demonstration

**Status:** Accepted (2026-11-20); M11 step 6 built. Names added are
marked `*` until the owner settles them (docs/naming.md).

## Context

M11's done-when: a pixel-art sample, pixel-exact at several window sizes,
composited over a 3D scene. Step 6: a small playable scene, a pixel-art
character moved round a tilemap with Box2D physics, animated and lit, over
a 3D background, with a bridge walked under and over (sorting, tile, and
physics layers switching together at its ends); checked headless, then
tried by hand.

## Decision

Decided by the owner (2026-11-20):

1. **A new sample, `samples/Crossing`***, written by
   **`make_crossing.py`*** (project, art, map, 3D, scene; standard library
   only, every ID fixed); its game code, `Project/Code/Crossing.cs`, by
   hand. The Walker sample is left as it is.
2. **Art drawn by the script**: the hero (16x16, idle and a four-frame
   walk facing down, left, right, up, as eight sprite clips), the tiles
   (grass, a sunken road, stone, rock, the bank, the deck, railings,
   stairs, ramps), and a lamp post; 16 pixels a meter.
3. **Top-down**, an island of 32 by 22 tiles over a **3D sea and sky** with
   a slowly turning rock (`Spin`*), shown wherever the island has no tiles.
4. **The crossing:** a sunken road (walls both sides, a ramp through each
   end) passes under a raised road (railings, stairs at each end).
   `FloorTrigger`*s (`floor`*, `size`*: boxes the hero's position is
   tested against) at the stairs' tops and bottoms put the hero on the
   bridge or the ground: its sorting layer **High*** or **Low*** and its
   physics layer **Bridge*** or **Ground*** together. The map's layers:
   **Ground*** (collides as Ground, the bank solid under the deck),
   **Deck*** (draws only, in the sorting layer **Deck***), **Railings***
   (collides as Bridge).
5. **Controls:** WASD or arrows, a gamepad's left stick or d-pad, in
   eight directions (`Hero`*: `speed`, and a walking and a standing clip
   for each facing). The hero is a dynamic body with a circle, its
   velocity set from its input, kept upright.
6. **Light:** a dusk ambient, lamps along both roads (some banded), a spot
   under the bridge, a lantern the hero carries; the 3D has its own sun
   and sky.
7. **The camera:** a pixel camera at **320x180**, Exact (whole scales,
   bars), following the hero (`Follow`*).
8. **The check** (`CrossingChecks`*, CTest `samples.crossing`*, game arg
   `check-crossing`*): the scene run standalone, headless, at a fixed 60
   fps, the hero walked by a script (`walk-script`*: points, each with
   seconds to try), its floors and points read from the game's log; then
   the scene drawn in the editor at 640x360, 1280x720, 1920x1080, and
   1000x700.

## As built

- The walk: held below the bank's face; through the south ramp into the
  sunken road; under the deck on the ground; out through the north ramp;
  up the east stairs (onto the bridge at x 8.66); over the road on the
  bridge; held by the railing (no lower than y -0.952); down the west
  stairs (onto the ground at x -10.44). Every point reached but the two
  pushed into the bank and the railing.
- Drawn: inside the island every virtual pixel a block of whole-number
  scale at each size, and the same 320x180 image at each; black bars at
  1000x700 (3x); the 3D sea round the island; the grass by a lamp brighter
  than the dusk's; the hero hidden under the deck in Low, shown over it in
  High.
- Found while building: a static collider's mask must name only its own
  floor's layer, since two shapes collide if either's mask names the
  other's layer (ADR 0075); the generator sets each map layer's mask to
  its own.

## Consequences

- M11 is done once the owner has tried it by hand.
- The sample is also where 2D features are tried from now on: art made by
  a script keeps it reproducible and licence-free.

## Amended: drawn under the deck where it's under it (2026-11-21, after the owner's tries)

The owner's first try found the hero's head drawn behind the bridge's
north railing while climbing the stairs: on the ground, the hero's whole
sorting layer was under the deck and its railings, though that railing is
behind it. Now a hero on the ground is drawn under the deck only inside a
**`Cover`*** box; elsewhere on the ground it's in **Low**, now *above*
Deck, so on the stairs it's drawn over the railing behind it and still
y-sorts with the lamps. Inside the Cover it's in a new sorting layer
**Under***, below Deck. The sorting layers, in drawing order: Ground,
Under, Deck, Low, High.

The second try found the Cover too tight: covering only the deck's
footprint, a hero coming up the sunken road had its head drawn over the
deck's south edge until its feet were under it. Under a raised deck, the
head goes under first: the Cover over the road reaches a sprite's height
(0.8 m) south of the deck. The stairs, beside the road, aren't covered.

The floor is kept by the physics layer alone; `FloorSystem` sets the
sorting layer each frame from the floor and the Cover, and the walk's log
names it at each point. `samples.crossing` checks the hero's layer along
the road and up the stairs, and draws its head under the railing coming up
the road and over the railing behind it on the stairs.
