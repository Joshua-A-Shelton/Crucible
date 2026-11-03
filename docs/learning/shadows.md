# Shadows

A learning note for M5 step 8 (ADR 0054): shadow maps, and the three ways
they go wrong.

## A shadow map

Draw the scene's depth from the light's point of view. That image, the
**shadow map**, records the nearest thing the light hits in every
direction. To shade a pixel, move its position into the light's view, and
compare its depth with what the map holds there. If the map saw something
nearer, something stands between the pixel and the light: the pixel is in
shadow.

Everything else is about three artifacts:

- **Blocky edges:** one texel of the map covers too much of the screen.
- **Shimmer:** shadow edges crawl as the camera moves.
- **Acne:** a lit surface shadows itself in stripes.

## Cascades: texels where they're needed

One map covering everything the camera sees spends nearly all its texels
far away, where things are small on screen. **Cascades** split the view
by distance into four slices, each with its own map. With the default
100 m, the nearest covers the first 7 m, the farthest 32 m to 100 m. The same 2048 texels
across, a character's face gets fine detail and a distant hill coarse
detail, which is all either needs.

The splits mix two rules: logarithmic (each slice a fixed ratio deeper,
matching how things shrink on screen) and uniform (equal steps). The pure
logarithmic split makes the first slice tiny, so Crucible uses three
quarters of it and a quarter uniform.

## Not shimmering

As the camera moves, a map following it lands on slightly different
texels every frame. A shadow edge falls through a texel one frame and
straddles two the next, so it crawls. Two things stop it:

1. **A sphere, not a box.** A box fitted around a slice of the view
   changes size whenever the camera turns, and so do its texels. A sphere
   around the same slice is the same size whichever way it looks.
2. **Snapping.** The map's center is moved to a whole number of texels in
   the light's view. As the camera moves, the map slides by exactly one
   texel at a time. Each texel keeps covering the same patch of ground, so
   edges hold still.

The sphere wastes the corners of the map: about a third of the
resolution. Stability is worth it here, because Crucible has no temporal
smoothing to hide crawling (ADR 0010).

## Casters behind the camera

A tall tree behind the camera's slice still shadows into it. A map built
tightly around the slice would cut the tree off at its near plane. Instead
the shadow pipelines **clamp** depth instead of clipping it: anything
nearer than the near plane is flattened onto it. A flattened caster is
still nearer than everything behind it, so it still casts.

## Acne

The map stores depth in texels, so a flat surface becomes a staircase in
it. Comparing the surface with its own staircase, half of each step looks
"behind" itself: stripes. Two fixes work together:

- **Depth bias** pushes what's drawn into the map slightly away from the
  light, more on steep slopes, where the steps are tallest.
- **Normal offset** moves the pixel being shaded slightly off its surface
  before the comparison, by about a texel, more when the light grazes it.

Too much of either and shadows detach from their casters ("peter
panning"). Too little and the stripes come back.

## Soft edges without noise

A single comparison is yes or no, so edges show the texels' staircase.
**Percentage-closer filtering** compares several nearby texels and
averages the answers. GPUs do a 2×2 version in one sample, with bilinear
weights. Crucible's **5×5 tent** gets a 5×5 footprint from just nine of
those, placed and weighted so their sum is a smooth tent shape. The edge
is about two texels wide, and every pixel costs the same. Random sample
patterns are cheaper for the same softness, but they sparkle without a
temporal filter to smooth them.

## Spot and point lights: the atlas

A spot light is like a small sun with a perspective view down its cone. A
point light shines everywhere, so it needs six views, the faces of a cube.
Rather than a texture each, all their views share one big **atlas**,
4096 texels square, cut into tiles of 128 to 512 texels. A light close to
the camera gets a bigger tile than one far away.

The filter reads a few texels around each sample. Near a tile's edge that
would read the neighbouring tile, so each view is widened by a small
border, and lookups stay inside it.

## Caching

A torch on a wall casts the same shadow every frame, until something
moves near it. So the atlas is kept from frame to frame. Each tile
remembers a fingerprint of what it drew: every caster in its view, mixed
with where each one stands. If this frame's fingerprint matches, the tile
is still right and isn't drawn again. A dungeon full of torches costs
almost nothing once drawn. Only the tiles a character walks through are
redrawn.

The fingerprint is a sum of one hash per caster, so the order culling
finds them in doesn't matter. Hashing each caster's position is more
exact than the ECS's change versions, which are per chunk: those would
redraw every tile holding any entity sharing a chunk with the moving
character.

## Shadows and toon shading

A physically based model just multiplies a light by how much of it gets
through. Toon shading decides its bands from how directly a surface faces
the light, and tints the dark band. If a shadow only darkened the light,
a face toward the sun but under a tree would land in the *lit* band, just
dimmer, and grayer instead of tinted. So shading models receive the
shadow separately, and toon multiplies it into N·L *before* banding: a
cast shadow and a face turned away get the same tinted look.
