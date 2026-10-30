# 0051 — Drawing meshes

**Status:** Accepted (2026-10-29). Names settled by the owner (docs/naming.md).

## Context

M5 step 5: imported meshes drawn, with CPU frustum culling on the job
system, instanced batches, a depth prepass, and an unlit forward pass with
MSAA (ADR 0015), front faces clockwise (ADR 0024). Models load by asset ID
(ADR 0049); world matrices and the camera come from ADR 0050. Skipping
unchanged transforms is to be measured here (docs/open-items.md).

## Options

**Resolving instances to loaded models:**
- a renderer cache, acquiring on first use and releasing models no
  instance used for a while;
- a handle component acquired when an instance appears and released when
  it goes, which needs ECS remove hooks;
- scenes acquiring everything they reference.

**World matrices on the GPU:**
- a persistent buffer by entity index, updated only from chunks whose
  `WorldTransform` changed, with culling writing 4-byte instance indices;
- or every visible matrix written each frame.

**What the unlit pass draws:**
- base color times base-color texture;
- the base color factor alone;
- debug colors.

**The color target:**
- HDR with configurable MSAA, resolved by a shader;
- or MSAA in the backbuffer's format.

## Decision

1. **A model cache in the renderer.** `MeshInstance` moves to C++
   (`crucible/MeshInstance.h`, still `Crucible.Scenes.MeshInstance` in C#,
   so saved scenes don't change). The renderer gives each instance a
   runtime-only `LoadedMesh` with the handle it resolved to, acquiring the
   model on first use. Models no instance used for 2 seconds are released.
2. **GPU-resident world matrices, sent by change version.**
   - Each frame slot keeps a buffer of `InstanceTransform` by entity
     index.
   - A slot's copy is brought up to date only from chunks whose
     `WorldTransform` changed since that copy was last synced.
   - Culling writes each visible instance's entity index.
3. **Unlit base color:** the material's base-color factor times its
   base-color texture times the vertex color. Acquiring a model acquires
   its base-color textures. Materials replace this in step 6.
4. **HDR with MSAA:** `SceneColor` is `R16G16B16A16Float` and `SceneDepth`
   is `D32Float`, at 4x by default (1, 2, 4 or 8, `setMsaaSamples`,
   launcher `--msaa`). Resolve clamps each sample, then averages into the
   backbuffer. That is ADR 0015's post-tonemap resolve, with a clamp
   standing in for step 9's tonemapping.

## Details

- **Frame:** before the steps run, the renderer builds a `DrawList`:
  1. New instances get a `LoadedMesh`. Chunks whose `MeshInstance` changed
     since the render system's last run are resolved again.
  2. Culling runs one job per chunk. Each mesh's bounds are transformed
     (Arvo) and tested against the view's planes (Gribb–Hartmann for row
     vectors; an infinite far plane drops out).
  3. The level of detail is the coarsest whose stored error
     (`MeshLod::error`) projects under one pixel.
  4. Visible instances are grouped into `DrawBatch`es by model, mesh and
     level.
  5. Stale handles are resolved again for the next frame.
- **Steps:** `DepthPrepass`, `Opaque` and `Resolve` replace Clear and
  Triangle.
  - `DepthPrepass` uses reverse-Z: it clears depth to 0 and tests Greater.
  - `Opaque` clears to `renderFrame`'s color and tests depth
    GreaterEqual without writing it. Equal would be exact in theory, but
    a last-bit difference between the two pipelines' positions would drop
    pixels.
  - Each batch draws one `drawIndexed` per submesh. Blackboard keys:
    `SceneDepth`, `SceneColor`.
- **Vertex pulling:** each model's buffer has a raw-buffer slot
  (`MeshData::descriptor`). The vertex shader reads positions, half or
  float UVs, and sRGB vertex colors from the stream offsets in the
  `DrawConstants` push constants.
  - The instance is `SV_StartInstanceLocation + SV_InstanceID`.
  - Half floats are decoded by hand: Slang's SPIR-V for `f16tof32` needs
    the Int16 and Float16 capabilities, which Slag doesn't enable.
- **`RenderStepContext`** gains `world`, `view` (the CPU `ViewConstants`)
  and `draws`.
- **`renderFrame(surface, world, lastRunVersion, ...)`:** the render
  system's last-run version (ADR 0034 amendment). 0 means a world not seen
  before, so every copy starts over.
- **Descriptors:** each surface reserves 8 resource-heap slots below the
  textures' 1024: transforms, the instance list, and Resolve's input, one
  per frame slot each.
- **Memory:** transforms live in `GpuUpload` memory where the device has
  it (resizable BAR), else `Upload`. Instance lists live in `Upload`. A
  buffer that's too small grows when the frame slot's GPU work is done,
  and the frame is culled again.

## Measured (M5 step 5)

The benchmark suite, in release on RADV (RX 7900 XT, resizable BAR, 23
workers). 100k quad instances, 1% moving every frame:

| Movers | Propagation, incremental / full | Draw list CPU, incremental / full | Matrices sent |
|---|---|---|---|
| Scattered among static entities | 0.24–0.31 / 0.30–0.31 ms | 1.19–1.38 / 1.39–1.43 ms | 99,918 / 100,000 |
| In an archetype of their own | 0.31 / 0.31 ms | 1.11–1.14 / 1.45–1.49 ms | 1,000 / 100,000 |

The same on Windows, in release with D3D12 on NVIDIA (RTX 2070 Super, no
resizable BAR, so the transforms go to `Upload` memory):

| Movers | Propagation, incremental / full | Draw list CPU, incremental / full | Matrices sent |
|---|---|---|---|
| Scattered | 0.65–0.90 / 0.87–0.89 ms | 2.20–2.22 / 2.28–2.34 ms | 99,918 / 100,000 |
| In an archetype of their own | 0.66–0.68 / 0.91–0.92 ms | 2.12–2.15 / 2.33–2.34 ms | 1,000 / 100,000 |

- **Change versions are per chunk** (about 100 of these entities each), so
  scattered movers make nearly every chunk look changed, and nothing is
  saved.
- **Movers in their own chunks save about 0.35 ms (24%)** of the draw
  list per 100k instances.
- **Incremental propagation:** nothing in this flat scene on Linux, but
  about 27% on the Windows machine when the movers are grouped. In deep
  hierarchies it saves about a third on Linux (ADR 0050).
- **Uploads on Windows** save less (about 0.18 ms, 8%): without resizable
  BAR the matrices go to system memory, which is cheaper for the CPU to
  write.

**Kept for now (owner, 2026-10-29).** Both paths were never slower in any
measured case. Upload skipping relies on propagation writing only matrices
that changed, so dropping incremental propagation would save less code
than it seems. The numbers come from a synthetic flat scene and cover CPU
time only, so they are measured again on step 10's imported scene. Drop
the paths if the savings there are under about 5%, or if they get in the
way of later work such as GPU-driven culling. If they matter, the bigger
lever is layout: dynamic entities in chunks of their own.

## As built (M5 step 5)

- Tests:
  - On every backend, five quads:
    - two red ones are one batch;
    - a textured one comes out white × blue;
    - a green one is hidden by depth;
    - a far one is culled.
  - Nothing moving sends no matrices. One moving entity sends only its
    chunk, and the other frame slot catches up.
  - Forced full uploads send everything. 1x MSAA matches. An unused model
    is released after 2 s.
  - The default schedule is DepthPrepass, Opaque, Resolve. A frame without
    meshes is the clear color everywhere.
