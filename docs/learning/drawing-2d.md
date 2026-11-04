# Drawing 2D with distance fields

A learning note for M6 step 3 (ADR 0059).

## One quad, any shape

A UI is mostly rectangles: panels, buttons, borders, rounded or not.
Instead of building triangles for each rounded corner, every primitive is
a single quad, and the pixel shader decides how much of each pixel the
shape covers. It does that with a *signed distance function*: for a point,
how far it is from the shape's edge, negative inside. For a rounded box
there's a short closed form (Inigo Quilez's):

```
q = abs(p) - halfSize + r
distance = min(max(q.x, q.y), 0) + length(max(q, 0)) - r
```

A border is the same distance moved inward by its width; a line is the
distance to a segment minus half its width (a capsule); a shadow is a
blurred box, which also has a closed form along one axis.

## Antialiasing from the distance

If a pixel's distance is −0.5 pixels, its center is half a pixel inside:
the pixel is fully covered. At +0.5 it's fully outside. In between, the
coverage is `0.5 − distance / pixelSize`. `fwidth(distance)` tells the
shader how much the distance changes from one pixel to the next, so the
same formula works when the shape is scaled or rotated: the edge is
always one pixel wide on screen.

## Why blending space matters

Light adds up in *linear* values, but the numbers in an image (sRGB) are
gamma-encoded so dark shades get more precision. Blending in linear light
is physically right: a half-covered edge between black and white is half
as much light, sRGB 188 rather than 128. Most of the frame already works
this way.

Text is the exception people notice. Glyph edges are mostly partial
coverage, and in linear light black text on white loses weight (its gray
edges come out lighter) while white text on black gains it. Browsers
blend in gamma space, so designers expect that look. The UI shader nudges
coverage by the text color's brightness, thickening dark text and
thinning light text, partway toward the gamma-space look.

## Exact glyphs, exactly placed

An exact glyph's image was rasterized for a specific pixel size, so it's
only sharp if each image texel lands on one screen pixel. The layout
already puts glyph origins on whole physical pixels; drawing rounds the
layout's origin to a pixel too and reads the page texel for texel. Any
other transform (rotation, scaling) resamples the image and blurs it,
which is what distance-field glyphs are for.

## Uploading only what changed

The glyph cache keeps pages on the CPU and remembers the rectangle that
changed in each since the last upload. Once a frame, those rectangles are
copied into the GPU textures. The copy is declared at the start of the
frame but filled at the end, after every step has run, so a glyph made
while the UI was being drawn still arrives before the GPU draws it.
