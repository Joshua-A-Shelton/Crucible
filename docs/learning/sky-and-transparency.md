# Sky, transparency, and antialiasing in the shader

A learning note for M5 step 9a (ADR 0055).

## Drawing the sky last

It's tempting to draw the sky first, as a background. But every pixel
covered by geometry would then be shaded twice. Drawing it *after* the
opaque pass, at the farthest possible depth, lets the depth test throw it
away wherever something is already there. Crucible uses reverse-Z, where
far is 0, so the sky's triangle sits at depth 0 and passes only where the
depth is still the cleared 0. On a screen that's half mountains, the sky
costs half a screen.

For each pixel the shader rebuilds the direction the camera looks through
it, from the inverse of the view-projection matrix. How far that direction
points up picks a color between the horizon and the zenith (or the
ground). The sun's disk is the set of directions within a small angle of
the sun's.

## Why transparency needs an order

An opaque surface simply replaces what's behind it, so the order doesn't
matter: the depth buffer keeps the nearest. A transparent one *mixes* with
what's behind: `result = color × alpha + behind × (1 − alpha)`. That mix
isn't commutative. Red glass over blue glass isn't blue glass over red, so
"behind" has to be finished before the nearer layer is drawn: far to near.

Two ways out:

- **Sort.** Order transparent objects by distance each frame and draw
  them far to near. It's exact for separate objects. An object passing
  through another is drawn whole, either before or after it, so one half
  is wrong.
- **Don't need an order.** Weighted blended order-independent
  transparency adds every layer's color into a buffer, weighted so nearer
  layers count more, and multiplies their (1 − alpha) into another. A
  final pass divides out the weights. Addition doesn't care about order,
  but the result is an average: a red pane at alpha 0.9 in front of a
  green one should be almost pure red, and comes out olive.

Crucible built both and compared them (ADR 0055). Sorting was exact
where it matters and faster on the GPU. OIT's two extra full-screen MSAA
targets cost bandwidth even for a few panes. OIT won only on CPU time with
thousands of objects, which is a particle system's problem, and particle
systems usually have their own renderer.

## Premultiplied alpha

The transparent shader outputs `color × alpha` and `alpha`, and the blend
is `result = source + behind × (1 − alpha)`. Folding alpha into the color
ahead of time means a surface with alpha 0 but some color *adds* light
(fire, glows, magic) with the same blend state as glass. One pipeline
covers both.

## Specular sparkle

A shiny highlight can be smaller than a pixel. On a curved or bumpy
surface far away, the pixel's single sample lands on the highlight one
frame and misses it the next: sparkle. Temporal antialiasing hides that
by averaging frames; Crucible doesn't (ADR 0004), so the shader fixes it.
How much the normal changes between neighbouring pixels (`ddx`, `ddy`) says
how much of the surface one pixel covers. The roughness is raised to
match, so the highlight spreads to at least a pixel and stays put.

## Toon bands

Toon shading has the same problem in a different form: the sharp edge
between two bands stair-steps once it's thinner than a pixel. The same
measure of how fast the normal changes across a pixel widens each band
edge just enough to cover a pixel, so distant edges stay smooth while
nearby ones keep their authored sharpness.

Derivatives only work where neighbouring pixels run the same code, so
both are measured once at the start of shading, not inside the per-light
loop, where pixels in different clusters loop different numbers of times.

## Alpha-to-coverage

Leaves and fences are alpha-tested: a texel is either kept or thrown
away. Thrown away per pixel, the edge is as jagged as the pixels. MSAA has
several samples per pixel, and **alpha-to-coverage** keeps a share of them
in proportion to alpha: alpha 0.5 covers half the samples, so the edge
pixel blends half and half. Alpha is first sharpened, so it goes from 0 to
1 over about one pixel around the cutoff. That keeps the edge crisp and
antialiased, rather than a blurry fade.

The depth prepass decides which samples a leaf covers. The opaque pass
then tests for *equal* depth, so it colors exactly those samples. With
MSAA off, it's an ordinary alpha test.
