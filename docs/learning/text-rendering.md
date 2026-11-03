# Exact-size glyphs and distance fields

A learning note for M6 step 1 (ADR 0020, amended).

## Two ways to put a glyph on screen

- **Exact-size:** FreeType rasterizes each glyph at the size and DPI it is
  drawn at, into a coverage bitmap packed in an atlas. The quad is drawn
  one texel per pixel, so what FreeType produced is what you see.
- **Distance fields:** each texel stores the distance to the glyph's
  edge instead of coverage. The shader turns distance into coverage at
  whatever scale the quad is drawn, so one small atlas serves every size.
  A single distance rounds sharp corners; **multi-channel** fields (MSDF)
  store three distances, each for different edges, and take their median,
  which keeps corners sharp.

## Why small exact text is crisper: hinting

At 11 px a stem is barely more than one pixel wide. If it lands across
two pixels, both are half covered and the stem looks grey and blurry.
**Hinting** nudges outlines so stems, the baseline and the x-height land
on whole pixels. Light hinting (FreeType's `LIGHT` target) only moves
things vertically, keeping letter shapes and spacing true. Distance
fields can't hint: the same field serves every size, so no size gets
its own nudges.

## Why distance fields win in motion

An exact bitmap is right at one size. Scaled, it's resampled and blurs;
turned, its pixel-aligned edges go soft. Animating a size means
rasterizing every size the animation passes through, growing the atlas
and uploading as it goes. A distance field just reconstructs the edge at
the new size, from the same texels, with an antialiased edge one screen
pixel wide (from the UV's rate of change, `fwidth`).

## So, both

Which is better depends on the text, not the game: crisp, dense editor
text and static UI labels want exact glyphs, while a title that grows
when selected, damage numbers, or text placed in the world want
distance fields. Measure before deciding: here the extra GPU cost of
MSDF was small (0.054 against 0.042 ms for 10,000 glyphs), and its
generation cost (about 0.7 ms a glyph) can be paid once, when a font is
first used or imported.
