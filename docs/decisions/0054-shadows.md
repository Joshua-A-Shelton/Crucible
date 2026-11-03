# 0054 — Shadows

**Status:** Accepted (2026-11-02). Names settled by the owner (docs/naming.md).

## Context

ADR 0015 plans cascaded shadow maps for the sun, snapped to texels so they
don't shimmer, and an atlas for local lights. ADR 0010 rules out temporal
techniques, so shadows must be stable and noise-free in a single frame,
and the first game must hold 60 fps on Steam Deck. Shading models take
lights one at a time through hooks (ADR 0052), and the lighting loop
(ADR 0053) has to hand them shadows. M5 step 8 decides U: shadow
techniques and filtering.

## Options

**The sun's cascades:**
- four, fitted to spheres and snapped to texels;
- three, the same way;
- four tight boxes (sharper, but edges shimmer as the camera turns).

**Filtering:**
- a 5×5 tent of nine bilinear comparisons (Castaño's optimized PCF);
- hardware 2×2 comparisons alone;
- PCSS (contact hardening, 30+ samples);
- EVSM (blurred moments; leaks light).

**Local lights:** spot and point lights in an atlas, cached or drawn every
frame; or spot lights only.

**How shading models see shadows:** a separate `shadow` value in the
light sample; or folded into its color, as ADR 0052 planned.

**What casts:** flags on lights and meshes; lights only; per material.

**Between cascades:** blend over a band; switch hard; dither.

**Settings:** the shadow distance on the light and sizes as global quality
settings; everything global; the distance on the camera.

**Depth format:** D16Unorm or D32Float.

## Decision

1. **Four cascades for the sun,** each fitted to a sphere around its
   slice of the view and snapped to whole texels.
2. **A 5×5 tent filter:** nine bilinear comparisons, fixed cost, a soft
   edge about two texels wide, no noise.
3. **Spot and point lights in a per-surface atlas,** a tile kept until
   its light or a caster in it changes.
4. **A separate `shadow` in `LightSample`** (0 shadowed to 1 lit). Toon
   bands N·L × shadow, so cast shadows take the shadow tint like faces
   turned away; PBR multiplies by it. Amends ADR 0052, which planned to
   fold shadows into the color.
5. **Flags on lights and meshes.** Components start zeroed, so a bool
   can't default to on (owner, 2026-11-02):
   - lights have `castsShadows`, off by default, the sun's included;
   - `MeshInstance` has `noShadows`, so meshes cast unless told not to,
     and older scenes still cast.
   - Only the brightest directional light that casts gets cascades.
6. **A blend band:** each cascade blends into the next over its last
   tenth; the last fades out over its own.
7. **The distance on the light, the sizes global:**
   - `DirectionalLight::shadowDistance` (meters; 0 means 100);
   - `setShadowMapSize` (each cascade, default 2048) and
     `setShadowAtlasSize` (default 4096), with launcher
     `--shadow-map-size` and `--shadow-atlas-size`.
8. **D16Unorm,** or D32Float where the device can't filter D16Unorm
   (filtering depth is a query in Slag), else no shadows.

## Details

- **Gathering (`Lights.cpp`):**
  - the first casting directional light is moved to the front of the
    frame's lights, so the lighting loop knows the cascades are light 0's;
  - visible point and spot lights with `castsShadows` go to `Shadows.cpp`
    as `ShadowRequests`.
- **Cascades (`fitCascades`):**
  - splits are 0.75 logarithmic plus 0.25 uniform from the near plane to
    the shadow distance; each slice starts at the previous one's blend;
  - the sphere's radius depends only on the projection, never on where
    the camera looks, so texels keep their size. It's widened by the
    filter's border and rounded up to 1/64 m against float noise.
  - Its center is snapped to whole texels in the light's view, so a map
    only ever slides by whole texels as the camera moves.
  - Conventional depth (0 near, 1 far, LessEqual), not reverse-Z: an
    orthographic map gains nothing from it, and D16Unorm isn't a float.
- **Casters toward the light still cast:**
  - shadow pipelines turn depth clipping off, so casters nearer than a
    cascade's near plane are clamped onto it;
  - culling leaves the near plane out.
- **Acne:** slope-scaled depth bias in the shadow pipelines (2 and 2), and
  a normal offset in the lookup: half a texel, plus up to two more as the
  surface turns away from the light.
- **The atlas:**
  - it's cut into 32 × 32 cells; a tile is 1, 2 or 4 cells (128, 256 or
    512 texels at 4096), aligned to its size;
  - a spot light takes one tile, a point light six (cube faces +X, −X,
    +Y, −Y, +Z, −Z);
  - views are widened by the filter's border, and lookups clamped inside
    their tile.
  - Up to `maxShadowedLights` (16) lights get tiles, largest on screen
    first. A tile's size follows the light's size on screen; it grows at
    once, but shrinks only to a quarter or less, so lights near a boundary
    don't flip back and forth.
  - Lights no longer shadowed give their tiles back; when the atlas is
    full, a light goes without shadows that frame.
  - It's created the first time a light asks; changing its size waits for
    the GPU and draws every tile again.
- **Caching:**
  - a tile is drawn again when its light was given new tiles, when the
    light's position, axis, range or cone changed, or when its casters'
    hash changed;
  - the hash adds up, per caster, a mix of its entity, model, mesh, level
    and world matrix, so the order culling finds them in doesn't matter.
  - Hashing world matrices sees each caster exactly. Change versions are
    per chunk, so one moving character would redraw every tile with a
    prop of its archetype in it.
  - Not seen: a material or model changing under the same handle. That's
    rare at run time, and hot reload (M7) can clear the cache.
- **Culling (`DrawList.cpp`):**
  - one pass over the chunks tests each instance against the camera and,
    unless it has `noShadows`, against every shadow view;
  - each view picks levels of detail by its own texels per meter;
  - batch keys lead with the view (0 the camera), so a sort groups
    batches by view, and every view shares one instance list.
- **The `Shadows` step** (after LightClustering, before Opaque):
  - one `ShadowCascade` pass per cascade into the transient
    `ShadowCascades` array;
  - one `ShadowAtlas` pass, only when a tile changed: it imports the
    atlas, clears each changed tile with `clearTileVertex` (a triangle at
    depth 1, viewport and scissor on the tile), then draws its casters;
  - it publishes `ShadowCascades` and `ShadowAtlas` only when there's
    something to read.
  - Opaque reads them when `ViewConstants::cascadeMap` / `shadowAtlas`
    are set, and writes the cascades' view while recording.
- **Shadow passes reuse the depth shaders:**
  - each pass gets a copy of the frame's `ViewConstants` with
    `viewProjection` replaced by the light's (an upload buffer per frame
    slot);
  - each material has a third pipeline, `MaterialPipelines::shadow`:
    depth only, clamped and biased, with alpha-tested materials clipped
    as in the prepass.
  - Faces are culled as the material's are: one-sided materials cast only
    from their front, double-sided ones from both sides (as in Unity).
- **Shaders:**
  - `shaders/crucible/shadows.slang` holds `sunShadow`, `localShadow` and
    `shadowComparisonSampler` (sampler slot 4: linear, clamped,
    LessEqual).
  - `[GpuShared] ShadowView` holds `viewProjection`, `rect` (in the map's
    UVs), `texelSize`, `normalOffset`, `normalOffsetPerMeter` and `layer`.
    The cascades are views 0–3, because `[GpuShared]` can't hold arrays,
    which is why the planned `cascadeViewProjections[4]` isn't there.
  - `LightData::shadowView` is a light's first view, or `noShadowView`.
  - ViewConstants adds `shadowViews`, `cascadeMap`, `shadowAtlas`
    (slots) and `cascadeSplits`.
- **Descriptor slots:** five more per surface (the shadow views and the
  cascades per frame slot, and the atlas), now 24.

## Why

Sphere-fitted, snapped cascades trade about a third of their resolution
for edges that don't move as the camera does. Without temporal smoothing
that stability matters more, and soft-banded toon hides the lost
sharpness. The tent filter's nine samples cost the same every pixel and
never sparkle. Caching makes static lights nearly free, which is most of
an RPG's torches.

A separate shadow value keeps the toon look whole: a cast shadow lands in
the tinted band, not just a dimmer lit one. Zeroed components decided the
flags' polarity. Depth clamping lets cascades stay tight without losing
tall casters behind the camera.

Given up, for now:
- contact-hardening penumbrae;
- per-light bias settings;
- caching cascades (the sun moves with the camera);
- a debug view of the cascades and the atlas.

## As built (M5 step 8)

- Tests, on every backend, with the Lambert test material (a shadowed
  pixel is ambient light alone):
  - the sun shadows ground under occluders in each of the four cascades,
    and not past its shadow distance; without `castsShadows`, nothing is
    shadowed;
  - meshes with `noShadows` cast nothing;
  - a spot light's tile is drawn once and kept while nothing changes, and
    while something out of its reach moves. It's drawn again when the
    occluder moves (the shadow follows), the light moves, or the
    occluder is destroyed.
  - A point light shadows through its −Y face and its +X face.
- Without a GPU: the cascades keep their texel size, and a fixed point
  lands at the same place within a texel, after the camera moves and
  turns.
- C#: scenes save and load `castsShadows` and `shadowDistance`.
- Checked by eye at grazing light and with casters standing on the
  ground: no acne, and no gap at the caster's foot.
