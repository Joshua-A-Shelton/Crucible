# Comparing renders across graphics APIs

A learning note for M5 step 10 (ADR 0056).

## Why the same scene isn't the same pixels

D3D12 and Vulkan on one GPU run nearly the same hardware paths, but a few
things are left to the driver:

- **Alpha-to-coverage patterns.** Turning "alpha 0.6" into "these 2 of 4
  samples" is up to the driver, and drivers dither differently. At a
  leaf's edge one API covers the left samples and the other the right
  ones, so single pixels can differ a lot.
- **Rounding.** Filtering, the shadow comparison, and conversions round
  their last bit as each API's shader compiler and driver choose. Over a
  whole image that adds up to a fraction of a level.
- **Different GPUs** (AMD against NVIDIA) differ more: texture filtering
  weights, MSAA sample positions in some modes, and math precision. That's
  why the demonstration compares the two APIs on one machine, and keeps no
  "golden" image to compare every GPU against.

## Two numbers, not one

One number hides the shape of a difference:

- **The mean difference** says whether the whole image drifted, e.g. a
  wrong gamma or a missing light would shift everything a little.
- **The share of pixels that differ a lot** says whether something is
  local and wrong, like a missing object or a broken edge.

A strict mean and a small share together catch both, while allowing a few
very different pixels: exactly the alpha-to-coverage edges.

## Look at the difference

Numbers say how much; a picture says where. Subtracting the two images and
magnifying the result (×16) shows the differences as bright lines, which
point straight at their cause: here the leaf outlines, where coverage was
dithered differently, and a faint pattern on shadowed walls.

## Measuring before keeping an optimization

Step 5 added two optimizations: recomputing only moved transforms, and
uploading only changed matrices. On a synthetic 100,000-object scene they
helped. On a scene shaped like a real game's, they saved nothing you could
measure, so they were removed. An optimization costs code, and code that
saves nothing is only cost.
