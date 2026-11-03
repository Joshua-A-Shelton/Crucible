# Naming

Final names chosen by the project owner. Anything not listed here is still
a placeholder (CLAUDE.md §7).

## Code organization (2026-10-26)

| Name | What it is |
|---|---|
| `crucible` | C++ namespace |
| `crucible_core` (`crucible::core`) | The C++ core library target |
| `Crucible` | C# root namespace |
| `Crucible.Engine` | The engine's C# assembly |
| `src/native/`, `src/managed/` | C++ and C# source folders, mirrored under `tests/` |
| `CRUCIBLE_` | Prefix for macros and CMake options |
| `crucible_core_static` | The static form of the core library, for AOT builds |
| `crucible_launcher` | The launcher executable target (shipped games are named after the game) |
| `Crucible.Hosting` | Namespace of the engine's managed entry point |
| `Crucible.BindGen` | The binding generator tool |
| `Crucible.TestGame` | Small test game assembly for hot-reload tests |

## Files and tools (2026-10-28)

Settled in a review of M4's and M5's placeholders (ADR 0047's amendment).

| Name | What it is |
|---|---|
| `.crproject` | The project file |
| `.crmeta` | A source file's sidecar: GUID, importer, settings (`hero.png.crmeta`) |
| `.crscene` | A scene (JSON) |
| `.crprefab` | A prefab (JSON) |
| `.crmesh` | An imported model (binary, kind MESH) |
| `.crtexture` | An imported texture (binary, kind TXTR) |
| `.crshader` | A compiled shader (binary, kind SHDR) |
| `shaderfax` | The build's shader compiler (C++ executable and CMake target) |
| `crucible_add_shader` | The CMake function that runs it |
| `[GpuShared]` | Slang attribute: a struct C++ and C# mirror |
| `CRBN`, `TXTR`, `MESH`, `SHDR`, and section tags | Binary-file magic and tags (ADRs 0039, 0042, 0043, 0047) |

## Rendering (2026-10-28)

| Name | What it is |
|---|---|
| `RenderSchedule` | The frame's ordered render steps (ADR 0048) |
| `RenderStep` | One entry: a named step adding 1..n graph passes |
| `RenderStepContext` | What a step sees while adding its passes |
| `Blackboard`, `BlackboardKey` | Resources steps share by name |
| `Backbuffer` | The built-in blackboard key for the surface's image |
| `renderFrame` / `Render.Api.RenderFrame` | Renders and presents one frame |
| `RenderSystem` | The C# system calling it, in RenderSubmit |
| `Clear`, `Triangle` | The first render steps (temporary, until M5 step 5) |

## GPU assets (2026-10-29)

| Name | What it is |
|---|---|
| `MeshHandle`, `TextureHandle` | A loaded model or texture (ADR 0049); Slag's render graph handles became `RenderGraphTexture` and `RenderGraphBuffer` to keep them apart (Slag 1.1.0) |
| `acquireMesh` / `releaseMesh`, `acquireTexture` / `releaseTexture` | Counted use by asset ID |
| `loadMesh` / `unloadMesh`, `loadTexture` / `unloadTexture` | From bytes, outside the asset system |
| `meshData(MeshHandle)` → `MeshData` | What render steps read to draw a model |
| `textureDescriptor`, `samplerDescriptor` | Resource- and sampler-heap slots for shaders |
| `Sampler`: `LinearRepeat`, `LinearClamp`, `PointClamp` | The fixed samplers |
| `crucible/RenderAssets.h`, `setAssetReader` | The header, and C#'s byte reader |
| `RenderAssets` | The C# class |
| `crucible/MeshFile.h`, `crucible/TextureFile.h` | The imported files' layouts, in the core |
| `AssetId` (C++) | The same 16 bytes as C#'s |

## Transforms and cameras (2026-10-29)

| Name | What it is |
|---|---|
| `crucible/Transform.h`: `LocalTransform`, `WorldTransform` (`matrix`) | Transforms, in `crucible::scenes` / `Crucible.Scenes` (ADR 0050) |
| `updateWorldTransforms`, C# `World.UpdateWorldTransforms` | Propagation on demand |
| `computeWorldTransform`, C# `World.ComputeWorldTransform` | One entity's world matrix now, not stored |
| `SystemContext::lastRunVersion`, C# `FrameContext.LastRunVersion` | The version of a system's previous run (ADR 0034 amendment) |
| `changedSince(world, entity, ...)`, C# `World.ChangedSince` | Per-entity form of `changedSince` |
| `crucible.TransformPropagation` | The propagation system |
| `crucible/Camera.h`: `Camera`, `CameraProjection` (`Perspective`, `Orthographic`) | The camera component, in `crucible::render` / `Crucible.Render` |
| `Camera` fields: `projection`, `fieldOfViewY`, `orthographicHeight`, `nearPlane`, `farPlane`, `priority` | Full vertical angle, full height |
| `ViewConstants`: `view`, `projection`, `viewProjection`, `inverseView`, `inverseProjection`, `inverseViewProjection`, `cameraPosition`, `time`, `viewportSize` | The per-frame view for shaders |
| Blackboard key `View`, `viewKey()` | Where steps find the frame's `ViewConstants` |
| `Matrix4x4::perspectiveReverseZInfinite`, `orthographicReverseZ` | The renderer's projections |

## Drawing meshes (2026-10-29)

| Name | What it is |
|---|---|
| `crucible/MeshInstance.h`: `MeshInstance` (`model`, `mesh`), `LoadedMesh` (`handle`) | The saved instance, and the renderer's resolved handle (ADR 0051) |
| `crucible/DrawList.h`: `DrawList` (`batches`, `instances`, `transforms`), `DrawBatch` (`model`, `mesh`, `lod`, `firstInstance`, `instanceCount`, `baseColorTextures`) | What render steps draw |
| `RenderStepContext`: `world`, `view`, `draws` | What steps get for drawing |
| `DepthPrepass`, `Opaque`, `Resolve` | The default render steps |
| Blackboard keys `SceneDepth`, `SceneColor`; `sceneDepthKey()`, `sceneColorKey()` | The MSAA targets |
| `MeshData::descriptor` | The model buffer's raw-buffer slot |
| `setMsaaSamples`, `msaaSamples`, launcher `--msaa` | The MSAA sample count |
| `renderFrame(..., lastRunVersion, ...)` | The render system's last-run version |
| `shaders/crucible/mesh.slang` (`depthVertex`, `opaqueVertex`, `opaquePixel`), `resolve.slang` (`fullscreenVertex`, `resolvePixel`) | The default steps' shaders |
| `InstanceTransform` (`x`, `y`, `z`), `DrawConstants` | GPU structs: world matrix columns, per-draw push constants |

## Materials (2026-11-02)

| Name | What it is |
|---|---|
| `.crmaterial`, `.crmat` | Authored material (JSON) and its imported binary (ADR 0052; M5 step 6b) |
| `Shader` importer | Material shaders, `.slang` to `.crshader` (6b) |
| Module `crucible.material`: `IMaterial` (`surface`, `shading`, `Shading`), `IShadingModel` (`Data`, `Accumulator`, `begin`, `light`, `ambient`, `finish`) | The interfaces |
| `SurfaceInput` (`toWorld`), `SurfaceData` (`defaults`), `LightSample`, `AmbientSample`, `TextureSlot` | What materials and models see |
| `ToonShading`, `ToonData` (`bands`, `softness`, `shadowTint`, `rimWidth`, `rimColor`), `PbrShading`, `PbrData` | The shading models |
| `sampleTexture`, `linearRepeatSampler`, `linearClampSampler`, `pointClampSampler` | Helpers |
| `MaterialVertex`, `materialVertexMain`, `surfaceInput`, `loadMaterial`, `shadeMaterial`, `clipMaterial` | Engine-side library functions |
| `shaders/crucible/default.slang`: `DefaultMaterial`, `DefaultToon`, `DefaultPbr`; entries `materialVertex`, `toonPixel`, `pbrPixel`, `maskPixel` | The built-in materials |
| `crucible/MaterialFile.h`: `MaterialInfo`, `MaterialTexture`, `AlphaMode`, `DefaultMaterialType`; section `MTLS` | Material records |
| `MeshImportSettings.Shading`, `ShadingModel` (`Toon`, `Pbr`) | Generated materials' shading |
| `MaterialHandle`, `DrawBatch::materials` | Runtime |
| C# per-shader structs named after the Slang type (`Brick`), in `Crucible.Materials`; `RenderAssets.SetMaterial`; C++ `acquireMaterial` / `releaseMaterial`, `setMaterialParameters` | Runtime changes from C# (6b) |

## Game materials (2026-11-02, M5 step 6b)

| Name | What it is |
|---|---|
| `TextureSlot` (Slang `{ index; sample(uv, fallback) }`, C# `{ Index }`) | A texture parameter |
| Field initializers | A material's defaults (no attribute) |
| `IMaterialData` (`ShaderId`, `TypeName`); generated `Defaults`, `<Type><Field>Array` | The generated C# structs, in `Code/Generated/Materials/<Type>.g.cs` |
| `.crmaterial` keys: `$guid`, `Shader`, `Type`, `AlphaMode`, `DoubleSided`, `AlphaCutoff`, `Values`, `Textures` | The authored material |
| `MeshImportSettings.Materials` | Slots remapped to authored materials |
| `MaterialTypeInfo`, `MaterialParameter`, `MaterialParameterKind` (`Float`, `Int`, `Uint`, `Texture`); `shaderMaterialTypesTag` (MTYP), `meshRemappedMaterialsTag` (MRMP), `materialFileKind` (MATR) | Records |
| Library `materialForward<M>`, `materialMask<M>`; entries `<Type>_forward`, `<Type>_mask`, `materialVertex`; `DrawConstants.alphaCutoff` | Specialized entry points |
| `MaterialImporter` (`"Material"`), `ShaderImportSettings`, `MaterialImportSettings`, `ShaderImporter.LibraryFolder`; `ImportContext.ReadImported`, `WriteGeneratedSource`, `AssetsFolder` | Importers |
| `shaderfax --materials`, `crucible_add_shader(... MATERIALS)` | Building material shaders |
| `acquireMaterial`, `releaseMaterial`, `setMaterialParameters`; internal `materialShader`, `materialTypeName`, `createMaterialFromRecords`, `readAsset` | Runtime |
| `compileMaterialShader`, `compiledSectionCount` / `compiledSection` / `copyCompiledSection`, `compiledDependencyCount` / `compiledDependency`, `ShaderSectionInfo` | The importers' compiler API |
