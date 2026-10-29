# 0049 — GPU assets

**Status:** Accepted (2026-10-29). Names settled by the owner (docs/naming.md).

## Context

Imported models (`.crmesh`, ADR 0043) and textures (`.crtexture`, ADR 0042)
must reach the GPU before step 5 can draw them. Slag gives buffers,
textures, descriptor slots in its heaps, a slot allocator that reuses slots
only after a semaphore value (Slag ADR 0032), and a destroyer that waits
for one (Slag ADR 0069). Loading is synchronous for now; asynchronous
loading comes with cooking (ADR 0021). M5 step 3; decision Q settles the
runtime handles and loading API, and how mesh data sits in GPU memory.

## Options

**Mesh memory:** one buffer per model, its streams and indices at offsets;
or shared pools per stream for every model, sub-allocated.

**Handles:** small generational handles, like `Entity`; or pointers.

**Uploading:** a staging buffer and a copy everywhere; or writing straight
into CPU-writable GPU memory (resizable BAR) where the device has it, with
staging otherwise.

**The map from asset ID to loaded resource:** in C#, which reads bytes and
counts uses; or in C++, calling back into C# for bytes.

## Decision

1. **One buffer per model.** Unloading frees it whole; Slag's sub-allocator
   already pools small buffers.
2. **Generational handles,** `MeshHandle` and `TextureHandle`: a stale
   handle is detected, and handles can live in components.
3. **Direct writes where possible:** a model's buffer is written straight
   into `GpuUpload` memory when the device has it, else through a staging
   buffer and a copy. Textures always go through staging (their layout in
   memory is the driver's).
4. **The asset map is in C++:** `acquireMesh(assetId)` loads on first use
   and counts uses, `releaseMesh` unloads after the last; for bytes it
   calls the reader C# registers (`setAssetReader`), which hands the bytes
   back during the call.

## Details

- **File layouts move into the core,** so the runtime reads what the
  importers write from one definition: `crucible/MeshFile.h` (MESH:
  `MeshFileInfo`, `MeshEntry`, `MeshLod`, `MeshRange`, `MeshNode`,
  `MaterialEntry`, `MeshAttribute`, `TextureRole`) and
  `crucible/TextureFile.h` (TXTR: `TextureInfo`, `TextureFormat`). Their C#
  mirrors move to `Crucible.Assets`.
- **`AssetId`** in C++ is the same 16 bytes as C#'s.
- **`crucible/RenderAssets.h`:** `acquireMesh` / `releaseMesh` and
  `acquireTexture` / `releaseTexture` by asset ID; `loadMesh(bytes)` /
  `unloadMesh` and `loadTexture(bytes)` / `unloadTexture` for data outside
  the asset system; `meshData(MeshHandle)` (a `MeshData`: the buffer, each
  stream's offset, and the file's tables), `textureDescriptor` (the
  texture's resource-heap slot), `samplerDescriptor(Sampler)` (the
  sampler-heap slot of `LinearRepeat`, `LinearClamp`, or `PointClamp`,
  made at start-up). C# wraps it as `RenderAssets`.
- **Nothing in use is freed:** a renderer-wide semaphore is signalled after
  every frame and upload; unloading hands the buffer or texture to Slag's
  `DeferredDestroyer`, and the texture's slot to its `DescriptorAllocator`,
  with the last value signalled, so frames already submitted finish first.
- **Descriptor slots:** the first 1024 resource slots stay with render
  steps (per-frame data); loaded textures take slots after them.

## As built (M5 step 3)

- `crucible/RenderAssets.h` and `RenderAssets.cpp` (in the renderer's
  object library): two generational tables (meshes, textures), each with a
  `HashMap` from asset ID to slot. A slot's generation is bumped when it is
  freed, so a kept handle reads as stale (`meshData` null, descriptor 0).
  Acquiring something loaded from bytes, or unloading something acquired,
  is reported and ignored.
- A model's buffer holds its streams and indices at 256-byte-aligned
  offsets; the file's tables (INFO, MSHS, LODS, RNGS, NODE, MATL) are kept
  on the CPU, 16-byte aligned, behind `MeshData`'s spans.
- Textures: staging laid out by Slag's copy footprints (rows padded to 256
  bytes) from the file's packed rows, copied per mip, then moved to
  `ShaderRead` in the same submit; the slot is written as a 2D texture
  view. Formats: BC7 (sRGB or not), BC5, BC4, RGBA8 (sRGB or not), RGBA16F.
- Samplers at sampler slots 1 to 3 (0 reads as no sampler); linear ones
  use Slag's maximum anisotropy.
- The work semaphore is signalled by each frame's closing submit and each
  upload; `collectAssets` runs at the start of every frame. Stopping the
  renderer frees whatever is still loaded.
- C#: `Crucible.Render.RenderAssets` (`SetReader`, acquire and release,
  the descriptor getters); its `[UnmanagedCallersOnly]` reader pins the
  bytes for the call and turns a throwing reader into "no such asset".
- Moving the layouts: the importers' headers include `MeshFile.h` and
  `TextureFile.h` and use their types; the importers' bindings treat them
  as the core's (`EXTERN_HEADERS`), so their C# mirrors are
  `Crucible.Assets.*`.
- Tests: on every backend, a model and a texture written in memory are
  loaded, then read back from the GPU byte for byte (each stream; each mip
  through a texture-to-buffer copy), with the model through both upload
  paths (a test hook forces staging); unloading makes handles stale;
  acquiring by ID loads once, counts uses, reuses a slot under a new
  generation, and reports NotFound and InvalidData; things still held when
  the renderer stops are freed. C#: acquiring before the renderer starts
  throws Unsupported.
- Slag's render graph handles were renamed `RenderGraphTexture` and
  `RenderGraphBuffer` (Slag 1.1.0, owner's request), so Crucible's
  `TextureHandle` doesn't share a name with them.
