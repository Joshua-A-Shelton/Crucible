# 0052 — Materials

**Status:** Accepted (2026-11-02). Names settled by the owner (docs/naming.md).

## Context

ADR 0025 set the architecture:
- shading models are Slang interfaces, specialized at compile time;
- authors write surface functions;
- variants only where unavoidable;
- every variant compiled at import or build time, and pipelines made while
  loading;
- parameters in one GPU buffer, laid out from reflection.

ADR 0010 asks for soft-banded toon and PBR, with new projects starting on
toon. M5 step 6 decides S: the material asset format, and how imported
materials become materials. The owner asked for hooks wherever they make
custom shading models easy. The step is split (owner, 2026-11-02):
- 6a: the material system on the engine's built-in materials;
- 6b: game material shaders, `.crmaterial` assets, and generated C#.

## Options

**Material asset:** authored JSON imported to a binary laid out from
reflection; or JSON shipped and laid out at load.

**A model's imported materials:**
- generated and remappable per slot;
- extracted as editable files on import;
- built only at runtime.

**Lighting before step 7:** a temporary sun and ambient; a directional
light component pulled forward; or unlit until step 7.

**Runtime changes from C#:**
- later;
- a name-based API;
- generated types.

## Decision

1. **`.crmaterial` (authored JSON) is imported to `.crmat` (binary),**
   with parameters already laid out by the shader's reflection (6b).
   Material shaders are `.slang` files, imported by the `Shader` importer
   to `.crshader` (6b).
2. **A model's materials are generated per slot,** and import settings can
   remap a slot to an authored material (6b).
   - Generated materials use the engine's `DefaultToon` or `DefaultPbr`
     (`MeshImportSettings.Shading`, toon by default).
   - They live in the model's `.crmesh` as its `MTLS` section, not as
     separate assets: the asset database has one output per import.
3. **A temporary sun and constant ambient** light everything until step 7.
4. **Generated C# types** per material shader, written into the game
   project's `Generated/` folder (6b).
5. **Shading models carry their own state through the lighting loop**
   (owner: "easy for custom shading models"):
   - `Data` holds the model's own inputs, which the material fills;
   - `Accumulator` holds whatever the model needs across lights;
   - the hooks run in order: `begin`, then `light` for each light, then
     `ambient`, then `finish`, which returns color and alpha.

   Effects that need every light at once (quantizing the total, a dominant
   light) fit in `finish`.

## The interfaces (shaders/crucible/material.slang)

```slang
interface IShadingModel
{
    associatedtype Data;
    associatedtype Accumulator;
    static Accumulator begin(SurfaceData s, Data d, SurfaceInput input);
    static void light(inout Accumulator a, LightSample light);
    static void ambient(inout Accumulator a, AmbientSample ambient);
    static float4 finish(Accumulator a, SurfaceData s, Data d, SurfaceInput input);
}

interface IMaterial
{
    associatedtype Shading : IShadingModel;
    SurfaceData surface(SurfaceInput input);
    Shading.Data shading(SurfaceInput input);
}
```

- **`SurfaceInput`** carries world position, normal, tangent, two UV sets,
  vertex color, the view direction, the screen position, time, and the
  entity index. `toWorld` maps a normal-map normal into world space.
- **`SurfaceData`** carries albedo, normal, roughness, metallic, emissive,
  alpha and occlusion. It's the same for every model, so a material can
  change models without being rewritten.
- **A light sample** has its direction and its color with attenuation
  applied. Shadows will be folded in at step 8, so models never see the
  light loop's internals.
- **A material's fields are its parameters,** read from the material
  buffer with `ByteAddressBuffer.Load<M>`.
- **`TextureSlot`** is a resource-heap slot (a `uint`).
  `sampleTexture(slot, uv, fallback)` samples it, or returns the fallback
  for slot 0.

## Shading models

- **`ToonShading`** gives N·L in `bands` steps whose edges are smoothed
  over `softness`. Shadowed areas are tinted toward `shadowTint` rather
  than going black, and there's a rim light on lit edges. Its data is
  `ToonData { bands, softness, shadowTint, rimWidth, rimColor }`.
- **`PbrShading`** uses a GGX distribution, height-correlated Smith
  visibility, Schlick Fresnel, and Lambert diffuse, metallic-roughness as
  in glTF.
  - Lights are π-normalized: a white light at normal incidence lights a
    white Lambert surface to 1.
  - Ambient is diffuse only, plus F0, until image-based lighting arrives.
  - Specular anti-aliasing is step 9's.

## Details (6a)

- **The built-in materials (`shaders/crucible/default.slang`):**
  - `DefaultMaterial` holds glTF's parameters: base color, emissive,
    metallic, roughness, alpha cutoff, normal scale, occlusion strength,
    and five texture slots.
  - `DefaultToon` and `DefaultPbr` share it and its surface function.
  - Entry points: `materialVertex`, `toonPixel`, `pbrPixel`, and
    `maskPixel` (the depth pass of alpha-tested materials).
- **Records (`crucible/MaterialFile.h`):**
  - a `MaterialInfo` holds the shader's asset ID (zero for the engine's),
    a type (`DefaultMaterialType`), `AlphaMode`, double-sided, the cutoff,
    and the parameter and texture ranges;
  - each `MaterialTexture` holds a slot's byte offset in the parameters
    and the texture's asset ID.

  The runtime acquires each texture and writes its slot into the
  parameters at load.
- **Variants (ADR 0025):** pipelines are keyed by material type, alpha
  mode and sides.
  - Opaque materials' depth uses the engine's positions-only `depthVertex`;
    alpha-tested ones use `maskPixel`, which discards below the cutoff.
  - The forward pass discards too, so what the prepass clipped isn't drawn
    over what's behind it.
  - Double-sided materials cull nothing, and their back faces flip the
    normal.
  - Blended materials draw as opaque until the transparent pass (step 9).
- **Runtime (`Materials.cpp`):**
  - a 4 MB material buffer (`Upload` memory) at resource slot 1, with
    first-fit allocation; freed ranges are reused once the frames that may
    read them are done;
  - generational `MaterialHandle`s;
  - pipelines cached per key and made while loading. Changing the MSAA
    sample count rebuilds them.
  - A model makes its materials when it loads and frees them with it.
  - Slots without a material draw a white toon fallback.
  - `DrawBatch::materials` holds the model's materials, one per slot.
- **Drawing:** each submesh is drawn with its material's pipeline, and
  draws are sorted by pipeline. `DrawConstants` gained the normal,
  tangent and UV1 streams, the material buffer's slot, and the material's
  offset. Its base-color fields are gone.
- **Vertices:** the library pulls every stream.
  - Octahedral normals and tangents are decoded.
  - Normals go through the cofactor of the world matrix's 3x3, so they
    stay perpendicular under non-uniform scale.
- **Slag 1.1.1** enables Vulkan's `shaderDemoteToHelperInvocation`
  (required by Vulkan 1.3). Slang compiles `discard` to SPIR-V's
  demote-to-helper, which is what `discard` means in D3D12 (Slag ADR
  0034).

## As built (M5 step 6a)

- Tests, on every backend:
  - toon puts two quads lit at N·L 0.4 and 0.65 in one band and 0.9 in
    the next, while PBR brightens step by step;
  - an alpha-tested quad keeps only its opaque half;
  - a quad turned away is culled when single-sided and drawn when
    double-sided;
  - the mesh drawing test runs with generated materials.
- C#: the mesh importer writes one generated material per slot (toon by
  default, its base-color texture at `DefaultMaterial`'s slot), and
  follows the Shading setting and alpha mode.
