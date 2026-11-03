# 0055 — The rest of the frame

**Status:** Accepted (2026-11-02, 9a; 2026-11-03, 9b); built. Names settled by
the owner (docs/naming.md).

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
- **9b:** tonemapping, the resolve, bloom, grading, and outlines.

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

## Options (9b)

**Tonemapping:** PBR Neutral, AgX, ACES; one fixed, or a choice.

**Post effects:** bloom; grading by parameters; vignette (SMAA and LUT
grading later).

**Where the settings live:** on the camera entity; one per world; global
functions.

**Exposure:** manual; manual plus automatic adaptation.

**Bloom:** no threshold; a soft threshold; both, with the threshold 0
unless set.

**Outlines:** inverted hulls; edges detected on screen from depth and
normals; both. The owner asked to compare the two techniques. Then, for
hulls:
- where width and color live: on the material, MeshInstance, or ToonData;
- how width behaves with distance: pixels thinning far away, constant
  pixels, or world units;
- color: flat, tinted by the surface, or both per material;
- smoothed outline normals from the importer, or not yet.

## Decision (9b)

1. **Selectable tonemapping, PBR Neutral by default:** `Tonemapper`
   (`PbrNeutral`, `AgX`, `Aces`).
2. **Bloom and parameter grading.**
3. **On the camera:** a `PostProcess` component beside `Camera`
   (`crucible/PostProcess.h`, registered by `registerCamera`, saved in
   scenes).
   - Every field is neutral at zero, since components start zeroed: a
     camera without one looks the same as one left at zero.
   - That includes bloom, which is off until `bloomStrength` is set
     (0.04 is a good start).
4. **Manual exposure** (`exposure`, in EV); eye adaptation later, opt-in.
5. **Bloom with an optional threshold:** `bloomStrength` and
   `bloomThreshold` (0: everything blooms).
6. **Inverted hulls** for outlines, chosen after the comparison below.
   Edge detection and material-drawn lines are to be revisited
   (docs/open-items.md).
7. **Hull settings on the material** (`MaterialInfo`; `.crmaterial` and
   `MeshImportSettings` take a nested `Outline { Width, Color, Tint }`):
   - **width:** pixels, thinning past `outlineFadeDistance` (10 m);
   - **color:** both, per material: `color × lerp(1, surface color,
     tint)`;
   - **normals:** smoothed outline normals generated by the mesh importer.
8. **Steps:** `Resolve` becomes `Tonemap` (owner), and `Bloom` comes
   before it.

## The outline comparison

Both were built and run on the same scenes (1280×720, 4× MSAA, release),
whole-frame GPU time:

| Scene | AMD, Vulkan: none / hull / edges | NVIDIA, D3D12 | NVIDIA, Vulkan |
|---|---|---|---|
| Spheres | 0.27 / 0.27 / 0.40 ms | 0.34 / 0.34 / 0.49 ms | 0.28 / 0.29 / 0.39 ms |
| 500 objects | 0.68 / 0.74 / 0.71 ms | 0.74 / 0.80 / 0.83 ms | 0.66 / 0.74 / 0.79 ms |

- **Hulls** draw silhouettes only. Their cost grows with geometry.
  - Hard-edged meshes split at corners unless the hull uses smoothed
    normals; smoothing the shading normals instead rounds the shading,
    hence a separate stream.
- **Edge detection** also draws creases and intersections, and lines the
  game may not want (the ground's far edge against the sky).
  - It costs about 0.12–0.15 ms whatever the scene: a 4× MSAA normals
    target in the prepass, and neighbour reads per sample.
  - Per-object control would need a mask.
- **A prototype of material-drawn lines** (a painted line map with an
  N·V threshold, drawn by the material's surface) drew view-dependent
  strokes, such as an anime nose's, inside a painted region and only
  within a range of angles. It works with either technique. One authoring
  rule came out of it: the threshold channel must stay continuous past the
  painted region, or filtering draws lines along its border.

## Details (9b)

- **The order:** DepthPrepass, LightClustering, Shadows, Opaque,
  **Outlines**, Sky, Transparent, **Bloom**, **Tonemap**.
- **Finishing in ViewConstants**, gathered with the camera (`Camera.cpp`):
  - `exposureScale` (2^EV) and `tonemapper`;
  - `bloomStrength`, `bloomThreshold` and `bloomLevels`;
  - `whiteBalance`: temperature and tint as one matrix, Unity's LMS
    method;
  - `lift`, `gamma`, `gain`, `contrast` and `saturation`.
- **Tonemap (`tonemap.slang`)**, per MSAA sample, then averaged:
  1. exposure;
  2. `lerp(color, bloom / levels, strength)`;
  3. white balance;
  4. the tonemapper;
  5. gain and lift, gamma, contrast around 0.18, saturation around
     luminance.
  - The tonemappers: Khronos PBR Neutral, AgX (Wrensch's polynomial fit),
    and ACES (Hill's fit).
- **Bloom (`bloom.slang`)** is one half-size transient with a mip per
  level, at most `maxBloomLevels` (6), down to about 8 texels.
  - **The prefilter** averages each 2×2 of SceneColor's samples, exposed
    and Karis-weighted, through the soft-knee threshold.
  - **The chain:** Jimenez's 13-tap downsample, then 3×3 tent upsamples,
    added onto each level by blending.
  - **Format:** R11G11B10Float where it can be drawn, filtered and
    blended, else RGBA16F.
  - Published under the `Bloom` key, only when the camera has bloom.
- **Outlines:**
  - Each outlined material (width above 0, not blended) gets
    `MaterialPipelines::outline`: `outlineVertex` with `<Type>_outline`
    (or default.slang's `toonOutlinePixel` / `pbrOutlinePixel`).
  - Front faces are culled, and depth is tested GreaterEqual and written.
  - `outlineVertexMain` pushes each vertex out on screen along its
    outline normal (its normal without that stream), by
    `width × min(1, outlineFadeDistance / view depth)` pixels.
  - `materialOutline<M>` evaluates the surface only for a tint or an
    alpha test, and clips alpha-tested hulls.
- **Outline normals:**
  - the importer sums the normals of every vertex at the same position
    (grouped by sorting their positions' bits) into the `ONRM` stream
    (`MeshAttribute::OutlineNormals`);
  - opposite normals that cancel keep the vertex's own.
- **Formats:** MESH files are version 2 (ONRM, and `MaterialInfo` gains
  `outlineWidth`, `outlineColor` and `outlineTint`), and .crmat files
  version 2. Importers and prefabs reimport.
- **DrawConstants** gains `outlineNormals`, `outlineWidth`,
  `outlineColor` and `outlineTint`.

## Why (9b)

Tonemapping each sample before averaging keeps MSAA's edges, as ADR 0015
argued. PBR Neutral leaves authored colors alone until they're very
bright, which suits stylized art; AgX and ACES are a setting away. Bloom
and grading are single-frame (ADR 0004). Zero-neutral fields keep scenes
simple: add the component, change only what you want.

Hulls give per-material control of anime-style outlines, at a cost that
grows only with outlined geometry. The importer's outline normals remove
their one artifact. Creases and view-dependent lines are a different
tool, kept on the list.

Given up, for now:
- automatic exposure;
- LUT grading;
- SMAA;
- edge-detected outlines and material-drawn lines;
- outlines on blended materials.

## As built (M5 step 9b)

- Tests, on every backend:
  - +1 EV doubles the light before PBR Neutral, which matches a C++ copy
    of it;
  - the three tonemappers keep a bright orange orange, each differently;
  - bloom glows past a bright quad's edge, not past a threshold above its
    light, and less under a lower one;
  - grading's saturation, white balance, contrast and gain each move the
    pixels the expected way.
  - Only outlined materials get a line; it is 3–5 pixels at 4 and thinner
    past the fade distance; a fully tinted white line on red is red.
  - Existing tests now expect tonemapped values (white is 240).
- The importer: outline normals equal the normalized sum of the normals
  at their position, across a hard edge.
- C#: `.crmaterial` `Outline` (and its mistakes), and
  `MeshImportSettings.Outline` into generated materials.
- The comparisons' and prototype's code was removed once decided.
