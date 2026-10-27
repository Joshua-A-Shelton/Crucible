# 0015 — Renderer foundation

**Status:** Accepted (2026-10-25). Amended 2026-10-26 (M2): device
ownership, surfaces, pacing fallback, and backend choice.

## Context

Constraints already set: clustered forward with MSAA and the single-frame
rule (ADR 0004); per-material toon and PBR shading (ADR 0010); the
pixel-art 2D path (ADR 0005); the RenderPrepare and RenderSubmit phases
(ADR 0013); one native job system (ADR 0014); 60 fps on Steam Deck
(ADR 0010).

Slag's optional render graph module (Slag ADR 0072) works out barriers and
layouts from each pass's declared reads and writes, records passes in
parallel (`recordPass(i)` from any thread, run by the user's job system),
aliases transient resources in shared memory, handles multiple queues, and
never reorders or culls passes: declaration order is execution order. It
is rebuilt every frame (caching is on Slag's after-v1.0 list), and its API
is C++ with templates and lambdas, with no C API.

This ADR sets the foundation. Details get their own ADRs: materials and
shaders, shadows, lighting and ambient light, post-processing and the MSAA
resolve, the pixel-art 2D path, and the C# rendering API.

## Decision

1. **Build on `slag::rendergraph`.** Limitations Crucible finds are fixed
   in Slag.
2. **The rendered frame is an ordered list of render passes with named
   insertion points,** mirroring frame phases (ADR 0013). Plugins insert,
   replace, or remove passes; the editor shows them.
3. **Core passes are C++. C# adds passes through a batched command API**
   (later ADR): C# writes compact commands into native memory, which native
   code replays as Slag calls, so the boundary is crossed once per pass,
   not per draw (ADR 0006).
4. **v1 culling is on the CPU,** on the job system, with instanced draws.
   GPU-driven culling and draw generation (indirect draws) come later if
   needed.
5. **The default frame** (pass names are placeholders):
   1. **Depth prepass:** early depth rejection for the forward pass, depth
      for later passes.
   2. **Light clustering** (compute): a 3D grid over the view listing the
      lights per cell.
   3. **Shadows:** cascaded shadow maps for the sun, an atlas for local
      lights; cascades snapped to shadow-map texels so shadows don't
      shimmer as the camera moves.
   4. **Opaque forward pass with MSAA,** each material's shading model.
   5. **Sky.**
   6. **Transparent pass,** sorted back to front.
   7. **Resolve, post-processing, outlines:** bloom, tonemapping, color
      grading, ADR 0010 outlines. MSAA is resolved after tonemapping
      (details in a later ADR).
   8. **2D pixel-art composite and UI,** then present.

## Why

The render graph removes the most error-prone parts of explicit APIs
(barriers, aliasing, parallel recording) and fits the job system exactly.
Its never-reorder rule matches Crucible's explicitness: what's declared is
what runs. Building on it also tests Slag harder than its own samples do.

An extensible pass list applies goal 7 to rendering; without a C# pass
API, plugins (C# only, ADR 0002) couldn't extend rendering at all. CPU
culling is enough at the scale of Crucible's first games and far easier to
debug.

Forward rendering handles transparency and MSAA naturally. Texel-snapped
cascades and a post-tonemap resolve keep clarity without temporal
techniques: averaging HDR samples before tonemapping brings back the edge
aliasing MSAA removed.

Given up, for now: GPU-driven rendering, and the render graph's per-frame
rebuild cost until Slag caches `compile` results.

## Amendment (2026-10-26): device and surfaces

Settled with the project owner in M2 step 3, when the core first created a
Slag device. Names (`crucible::render`, `SurfaceHandle`) are placeholders.

- **The launcher owns the device,** like the platform (ADR 0011): it calls
  `render::initialize` after `platform::initialize` and shuts both down in
  reverse after C# returns. Failure to find a supported GPU is reported
  before .NET starts. On Windows it's also required: the D3D12 Agility SDK
  loads only in an executable that exports its version, which a C# test
  host can't. Every executable that creates a device gets the export
  (`crucible_use_d3d12_agility`). Software devices (WARP) are not allowed,
  so a game never silently renders in software.
- **One surface per window, created explicitly** by C# (`CreateSurface`,
  `DestroySurface`): the swapchain, two command buffers, and a frame
  semaphore. The platform layer knows nothing about rendering, and an
  editor can have windows without one. The surface resizes itself when the
  window's pixel size changes, and skips frames (sleeping one refresh)
  while the window is minimized or occluded.
- **Present mode** (ADR 0003): Mailbox with three images where supported,
  else Fifo with two; max frame latency 1.
- **Pacing:** `waitForNextFrame` uses Slag's frame latency capability; where
  it's missing, it waits until the GPU has finished the previous frame,
  which ignores presentation but keeps the queue at one frame. Requiring
  the capability was rejected: some Windows Vulkan drivers lack
  `present_wait`.
- **Backend:** Slag's platform default (D3D12 on Windows, Vulkan on Linux),
  overridden with the launcher's `--backend d3d12|vulkan`, so both are
  tested on Windows.
- **Per-frame functions return `Status`** (`CRUCIBLE_EXPOSE_STATUS`, ADR
  0030); new statuses `DeviceLost` and `OutOfDeviceMemory` map Slag's.
- **Validation:** debug builds turn on backend validation where installed;
  Slag's warnings and errors go to stderr, its info messages are dropped.
