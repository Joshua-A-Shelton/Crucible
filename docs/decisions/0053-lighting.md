# 0053 — Lighting

**Status:** Accepted (2026-11-02). Names settled by the owner (docs/naming.md).

## Context

ADR 0015 plans clustered forward lighting, with a compute pass that lists
each cell's lights. Shading models already take lights one at a time and
ambient once (ADR 0052), but step 6 lit them with a fixed sun and a
constant ambient. M5 step 7 decides T: the lighting and ambient model.

## Options

**Components:** one per kind of light; or one `Light` with a kind.

**Units and falloff:**
- unitless, with inverse square windowed to a range;
- physical units (lux, lumens) with exposure;
- an artistic linear or smoothstep falloff.

**Ambient:** a hemisphere (sky and ground colors); L2 spherical harmonics;
or a constant.

**Assigning lights:**
- 3D clusters filled by compute;
- 2D screen tiles with depth bounds (Forward+);
- clusters built on the CPU.

## Decision

1. **One component per kind:** `DirectionalLight` (color, intensity),
   `PointLight` (plus range), and `SpotLight` (plus `innerConeAngle` and
   `outerConeAngle`, full cone angles in radians), in `crucible/Lights.h`.
   They're mirrored into C# as `Crucible.Render.*` and saved in scenes.
   - Placement comes from the entity's `WorldTransform`: directional and
     spot lights shine along +Z, and point and spot lights sit at the
     entity's position.
2. **Unitless, windowed inverse square:**
   - color × intensity in the π-normalized units the shading models use:
     a white light of 1 lights a white Lambert surface to 1;
   - point and spot lights fall off as
     `(1 / d²) × (1 − (d / range)⁴)²` (Frostbite, Unreal), which reaches
     zero at the range, so the range bounds the clusters.
3. **Hemisphere ambient:** an `AmbientLight` (sky color, ground color,
   intensity), blended by the surface normal's height.
   - One per world, the one with the lowest entity index. With none,
     there's no ambient light; a world without lights is dark.
   - Step 9's sky may replace or feed it.
4. **3D clusters in compute:** 16 × 9 screen tiles × 24 depth slices,
   filled by the `LightClustering` step from the lights the CPU culled.

## Details

- **Gathering (`Lights.cpp`, before the steps run):**
  - Directional lights are sorted brightest first, at most
    `maxDirectionalLights` (4), so the sun for step 8's shadows is the
    first.
  - Point and spot lights are culled against the view by their range
    sphere, one job per chunk. They're sorted by entity so the result is
    the same whichever worker found them, then capped at
    `maxVisibleLights` (1024).
  - They go into the surface's light buffer for the frame slot
    (`LightData`: position, range, direction toward the light, color with
    intensity, and the cone as `saturate(cos × spotScale + spotOffset)`).
  - The counts, the ambient colors, the cluster parameters and the slots
    go into `ViewConstants`.
- **Depth slices** grow exponentially from the near plane to the farthest
  visible light's reach: `slice = floor(log(z) × clusterSliceScale +
  clusterSliceBias)`. The far plane is infinite, so the lights bound the
  grid instead.
- **`LightClustering`** (compute, `clusters.slang`, after DepthPrepass):
  - one thread per cluster builds the cluster's view-space box. The
    formula `ndc × w(z) / P` serves perspective and orthographic
    projections alike.
  - Each visible light's sphere is tested against the box, keeping up to
    `maxLightsPerCluster` (64).
  - Its output is one transient buffer, published as `LightClusters`:
    each cluster's count, then each cluster's index list. It's written
    through RW views and read through read-only ones: four more
    descriptor slots per surface, now 16.
- **The lighting loop (`material.slang`'s `shadeMaterial`):**
  - every directional light;
  - then the lights in the pixel's cluster, from its screen tile and the
    log of its view depth;
  - then the hemisphere ambient for the surface normal;
  - all through the shading model's hooks, which are unchanged.
- **The render graph** orders it: LightClustering writes the cluster
  buffer, and Opaque reads it, so the graph puts a barrier between them.
- **The default schedule** is DepthPrepass, LightClustering, Opaque, then
  Resolve.

## As built (M5 step 7)

- Tests, on every backend, with a plain Lambert test material, so pixels
  follow the lights exactly:
  - a point light at 1 m and at 2 m matches the windowed inverse square
    within 5–8%, and a light whose range falls short lights nothing;
  - a 30-degree spot lights its axis and not 34 degrees out;
  - the hemisphere ambient gives a face tilted up the sky's color and one
    tilted down the ground's;
  - 120 small lights over 12 depths from 2 to 57 m each light their own
    quad's center and not its edge, which exercises the slices;
  - the existing material and drawing tests run with a sun and ambient
    matching step 6's temporary ones.
- C#: scenes save and load the light components.
- Fixed on the way: negating the unsigned slice count wrapped around and
  sent every pixel to the wrong slice.
