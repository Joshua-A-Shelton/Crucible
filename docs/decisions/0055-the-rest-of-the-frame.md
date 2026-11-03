# 0055 — The rest of the frame

**Status:** Accepted (2026-11-02); 9a built, 9b to come. Names settled by the
owner (docs/naming.md).

## Context

After shadows, the frame still lacked:
- a sky;
- a transparent pass (blended materials drew as opaque);
- the shader-level antialiasing ADR 0004 makes default: specular AA and
  alpha-to-coverage;
- tonemapping, the post-tonemap MSAA resolve ADR 0015 decided on, post
  effects, and ADR 0010's outlines.

M5 step 9 decides V: the post-processing chain, the resolve, and outlines.
It's split in two (owner, 2026-11-02):
- **9a:** the sky, transparency, specular AA, toon band AA, and
  alpha-to-coverage;
- **9b:** tonemapping, the resolve, bloom, grading, and outlines (decided
  when 9b starts).

## Options (9a)

**Sky:** a gradient with a sun disk; a physical atmosphere (Hillaire's
lookup tables); a painted cubemap.

**Transparency:** sorted back to front per instance; weighted blended
order-independent transparency (McGuire and Bavoil 2013). The owner asked
to compare both, on speed and looks, before choosing.

**Whether the sky lights the scene:** separate from `AmbientLight`; or
driving it when there is none.

**Toon band edges:** widened by how fast N·L changes across the pixel; or
left at the authored softness.

## Decision (9a)

1. **A gradient sky now, cubemaps later:**
   - `Sky` in `crucible/Sky.h` holds `zenithColor`, `horizonColor`,
     `groundColor`, `intensity`, `sunSize` (the disk's angular diameter)
     and `sunIntensity`.
   - It's one per world, the one with the lowest entity index, registered
     by `registerLights` and saved in scenes.
   - It's drawn only where nothing else is.
   - A painted cubemap is expected to become a field or mode of `Sky`
     once cubemap import exists.
2. **Sorted transparency,** chosen after the comparison below:
   premultiplied alpha, farthest first, one draw per instance, no depth
   writes, no shadows cast.
3. **The sky doesn't light the scene;** `AmbientLight` stays separate.
4. **Toon band edges are antialiased:** each edge is at least as wide as
   N·L's change across the pixel.
5. **Specular AA and alpha-to-coverage are on by default,** as ADR 0004
   set out.

## The comparison

Both modes were built and run on the same scenes (1280×720, 4× MSAA,
release builds, Lambert test material):

| Scene | AMD RX 7900 XT, Vulkan | NVIDIA, D3D12 | NVIDIA, Vulkan |
|---|---|---|---|
| 3 panes: GPU sorted / OIT | 0.009 / 0.064 ms | 0.005 / 0.061 ms | 0.011 / 0.066 ms |
| 4,000 particles: GPU sorted / OIT | 0.27 / 0.35 ms | 0.41 / 1.05 ms | 0.44 / 1.26 ms |
| 4,000 particles: frame CPU sorted / OIT | 0.58 / 0.22 ms | 1.11 / 0.59 ms | 1.15 / 0.51 ms |

- **Looks:**
  - panes at alpha 0.4 and the particle cloud looked nearly the same;
  - intersecting panes looked alike (neither splits a pane that passes
    through another);
  - **at alpha 0.9, OIT was visibly wrong.** Red over green came out
    olive, because it never fully hides what's behind a surface.
- **Speed:**
  - OIT costs about 0.06 ms even for three panes (clearing and
    compositing two more MSAA targets), and up to 2.9× sorted under
    overdraw on NVIDIA;
  - sorted costs more CPU only with thousands of instances (sorting, one
    draw each).

## Details (9a)

- **The default schedule:** DepthPrepass, LightClustering, Shadows,
  Opaque, **Sky**, **Transparent**, Resolve.
- **Sky (`sky.slang`):**
  - a triangle at depth 0 (reverse-Z far), tested GreaterEqual, so it
    covers only what's still cleared;
  - the view ray comes from `inverseViewProjection` (straight ahead for
    orthographic views);
  - above the horizon it blends toward the zenith by √up, below toward
    the ground by √(4 × −up);
  - the sun's disk sits toward the first directional light, in its color
    times `sunIntensity`, with its edge smoothed by `fwidth`.
  - Gathered with the lights into `ViewConstants`: `skyZenith`,
    `skyHorizon`, `skyGround`, `skySunCosine`, `skySunIntensity`, and
    `sky` (whether there is one). Without a sky, renderFrame's clear color
    shows.
- **Transparent:**
  - blended materials get only `MaterialPipelines::transparent`; the
    prepass, Opaque, and Shadows skip them.
  - Culling records each visible instance's view depth (its bounds'
    center along the camera's forward), stored beside the instance list.
  - The step draws each blended submesh of each instance, sorted farthest
    first (stable, so a model's submeshes keep their order).
  - It's lit by the same lighting loop, shadows included.
  - `materialTransparent<M>` returns premultiplied color, so a color with
    alpha 0 adds light.
- **Alpha-to-coverage:**
  - the prepass draws alpha-tested materials with `materialCoverage<M>`
    and the pipeline's alpha-to-coverage;
  - alpha is sharpened around the cutoff to about a pixel,
    `(alpha − cutoff) / fwidth(alpha) + 0.5` (Golus);
  - Opaque then tests Equal, so it colors exactly the samples covered (the
    same vertex shader, so depths match), and no longer discards;
  - Shadows still clip with `maskPixel`.
  - With one sample, it's the plain alpha test.
- **Specular AA:** `specularAntialiasing(normal, roughness)` (Tokuyoshi
  and Kaplanyan, as in Filament): roughness widened by the normal's
  screen-space variance (variance 0.15, threshold 0.2). PBR applies it in
  `begin`; custom models can call it where derivatives are valid.
- **Toon band AA:** `begin` measures the normal's change across the pixel
  (derivatives aren't valid inside the cluster loop), and each band edge
  is at least half of that times the band count wide.
- **New entries:**
  - every game material type gets `<Type>_coverage` and
    `<Type>_transparent`;
  - default.slang adds `coveragePixel`, `toonTransparentPixel` and
    `pbrTransparentPixel`.

## Why

Sorted transparency is exact in the common cases (glass, water, panels,
nearly opaque layers), and faster on every GPU tried; Steam Deck's
bandwidth would widen OIT's GPU cost. Its CPU cost only matters at
particle scale, where a particle system can bring its own method later
without changing materials.

A gradient sky suits painted, stylized skies and costs one triangle.
Alpha-to-coverage needs the prepass's depth to agree with Opaque's color
sample by sample, hence the Equal test.

Given up, for now:
- order-independent transparency (and per-sample shading, which its MSAA
  composite would want);
- a physical atmosphere;
- shadows from transparent surfaces.

## As built (M5 step 9a)

- Tests, on every backend:
  - without a `Sky`, the clear color shows; with one, the horizon's gray
    at the horizon, blue above, red below;
  - the sun's disk is toward the light and not just off it;
  - geometry keeps its own color.
  - Nearly opaque panes made nearest first still blend nearest last, an
    opaque quad in front of a pane hides it, and one behind a half-clear
    pane shows through.
  - An alpha-tested edge has partly covered pixels at 4× MSAA and none at
    one sample.
- C#: scenes save and load `Sky`.
- A sphere test model (`sphereModel`) for curved surfaces; a scene of toon
  and PBR spheres, a transparent one, shadows, and the sky was checked by
  eye.
- The comparison's code was removed once sorted was chosen.
