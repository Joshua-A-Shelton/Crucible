# 0052 — Materials

**Status:** Accepted (2026-11-02); 6a and 6b built. Names settled by the owner (docs/naming.md).

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
3. **A temporary sun and constant ambient** light everything until step 7
   (replaced by light components and clusters, ADR 0053).
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
  applied. Shadows were to be folded in at step 8; instead they come
  separately, as `LightSample::shadow`, so toon can band them like faces
  turned away (ADR 0054). Models still never see the light loop's
  internals.
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

## Step 6b: game materials (owner's decisions, 2026-11-02)

- **Texture parameters are `TextureSlot`,** a struct `{ uint index; }`
  with `sample(uv, fallback)`, so reflection can tell textures from
  numbers.
  - Materials aren't `[GpuShared]`, and they're read with
    `ByteAddressBuffer.Load`, field by field, so the nested-struct refusal
    of ADR 0047 doesn't apply.
  - The engine's `DefaultMaterial` keeps `uint` slots, because it is
    `[GpuShared]`.
- **Defaults are Slang's own field initializers**
  (`float roughness = 0.5;`), read with `getDefaultValueBlob`; fields
  without one are zero.
  - The owner first chose a `[Default(...)]` attribute, on my wrong claim
    that reflection couldn't read initializers. A check showed it can,
    vectors and arrays included, and Slang attributes take a fixed number
    of arguments.
- **Parameters** are 32-bit scalars (`float`, `int`, `uint`; `bool` is
  refused, use `uint`), float vectors, `float4x4`, `TextureSlot`, and
  fixed-length arrays of them.
- **`setMaterialParameters` is double-buffered:** each call writes a fresh
  copy and frees the old one once the frames using it are done.

### As built

- **The compiler:** `compileMaterialShader` finds every struct that
  implements `IMaterial`.
  - Its layout is a structured-buffer element's, checked to be the same
    on Vulkan and D3D12; `Load<T>` reads that layout.
  - It reflects the parameters and their initializers, and writes the
    `MTYP` section (`MaterialTypeInfo`, `MaterialParameter`,
    `MaterialParameterKind`).
  - It compiles a generated module that specializes the library's
    `materialForward<M>` / `materialMask<M>` into each type's
    `<Type>_forward` / `<Type>_mask`, beside one `materialVertex`. No
    Slang specialization API is needed.
  - It lists the files the module read, and `shaderfax --materials` uses
    the same path.
- **The `Shader` importer** (`.slang` → `.crshader`):
  - it compiles for the platform's formats, with `Assets/`, the source's
    folder and the engine's library (copied beside the assemblies) on the
    search path;
  - modules it imports from `Assets/` become dependencies;
  - each material type gets a C# struct in `Code/Generated/Materials/`:
    explicit offsets, `IMaterialData` (`ShaderId`, `TypeName`), inline
    arrays (`<Type><Field>Array`), and a static `Defaults`.
- **`.crmaterial`:**
  - The file has `$guid` first, as scenes do (the database finds Crucible's
    own files' GUIDs there), then `Shader`, `Type`, `AlphaMode`,
    `DoubleSided`, `AlphaCutoff`, `Values` and `Textures`.
  - The database now lets Crucible's own files have an importer.
  - The `Material` importer lays the values over the type's defaults and
    writes a `.crmat` (kind `MATR`, one record in `MTLS`).
  - It reads the shader's import through `ImportContext.ReadImported`,
    which makes the shader, and what it depends on, dependencies of the
    material.
  - Unknown parameters, wrong shapes, and textures given as values are
    errors that name the parameter.
- **Models:** `MeshImportSettings.Materials` holds one asset ID per slot,
  written as `MRMP`. A remapped slot acquires that material, and falls
  back to its generated one if the material can't load.
- **Runtime:**
  - `acquireMaterial` / `releaseMaterial` load by asset ID and count uses.
  - A game shader is loaded on first use, and its pipelines are cached
    per shader, type, alpha mode and sides. They're kept until the
    renderer stops; hot reload will replace them.
  - A record must match its type's size, or it's refused (reimport it).
  - `DrawConstants.alphaCutoff` carries the cutoff per draw, so
    `DefaultMaterial` dropped its own.
- **C#:** `RenderAssets.AcquireMaterial`, `ReleaseMaterial`, and
  `SetMaterial<T>`, which checks that `T` is the material's shader and
  type.
- **Tests:**
  - The compiler test checks a material's entries, its packed layout (an
    array of 3 at stride 4, the texture kind) and its defaults, and that
    `bool` is refused.
  - C#:
    - the `Shader` importer writes the types and the C# struct, and an
      imported module change reimports it;
    - the `Material` importer lays out values, arrays, textures and
      defaults, names mistakes, and reimports when its shader changes;
    - a model's slots remap;
    - acquiring a material needs the renderer, and `SetMaterial` refuses
      a struct of the wrong type.
  - On every backend, a quad remapped to a game material with its own
    shading model shows that every hook ran, that new parameters reach the
    next frame, and that acquiring it again counts.
- **Fixed on the way:** `memcpy` from a null pointer when a material has no
  textures (UBSan).
