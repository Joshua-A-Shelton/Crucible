# 0048 — The render schedule

**Status:** Accepted (2026-10-28). Names settled by the owner (docs/naming.md).

## Context

ADR 0015 builds the renderer on `slag::rendergraph` (Slag ADR 0072): passes
declare their resource uses, and the graph works out barriers, semaphores
between queues, and transient memory, rebuilt every frame and never
reordered. It also asks for an ordered list of render passes with named
insertion points, which plugins edit like frame phases, core passes in
C++, and C# passes later through a batched command API (M6). Today a frame
is `clearAndPresent`, called by `ClearPresentSystem` in RenderSubmit. M5
step 2 puts the frame on the graph; decision P settles the C++ pass API and
how passes declare resources.

## Options

**One entry of the list:** a named step that adds any number of graph
passes; or exactly one graph pass.

**Sharing resources between entries:** named slots registered once and
looked up by ID; or a fixed struct of known targets.

**What pass code sees:** Slag's own `PassBuilder` and `PassContext`, plus a
small Crucible context; or Crucible wrappers.

**Recording:** in parallel on the job system; or in order on one thread.

**The engine's own compiled shaders at run time:** embedded in the core
library; or files beside it.

## Decision

1. **A `RenderSchedule` of named `RenderStep`s.** Each step adds 1..n graph
   passes each frame (shadows: one per cascade). The schedule is built once
   and edited by name, like frame phases (ADR 0036): add, insert before or
   after, replace, remove.
2. **Steps share resources through the `Blackboard`:** keys
   (`BlackboardKey`) are registered by name at start-up and are small IDs
   after that; each frame a step publishes a texture or buffer handle under
   a key, and later steps read it. Reading a key no step has published is
   reported, naming the key and the reading step. Plugins register keys of
   their own.
3. **Steps use Slag's types directly:** `slag::rendergraph::PassBuilder` and
   `PassContext`, through a `RenderStepContext` holding the graph, the
   device, the surface's size, the frame number, and the blackboard.
4. **Graph passes are recorded in parallel** with `jobs::parallelFor`.
5. **The engine's compiled shaders are embedded in the core library,** as
   byte arrays generated at build time from shaderfax's `.crshader` files.
   Game and material shaders remain assets.

## Details

- **The frame:** `render::renderFrame(surface, clear color)` (bound to C# as
  `Render.Api.RenderFrame`, called by `RenderSystem` in RenderSubmit, which
  replaces `ClearPresentSystem`): acquire the swapchain image (or take the
  off-screen texture), begin the surface's graph frame, import the image and
  publish it as `Backbuffer`, run every step, compile, record, execute,
  present. Resizing, minimized windows, and skipped frames behave as before.
- **One graph per surface,** so windows render independently, with two
  frames in flight like today. After the graph's submits, an empty submit
  on the graphics queue signals the surface's frame semaphore, which
  pacing's fallback and the GPU-timing thread wait on.
- **Off-screen surfaces** still end each frame in the render-target layout
  (the graph's final layout for them), so `readSurfacePixels` works as
  before.
- **The first steps,** temporary until step 5's real ones: `Clear` clears
  the backbuffer to the given color, `Triangle` draws one triangle over it.
- **Build order:** shaderfax links the core (its binary-file writer), and
  the core now embeds what shaderfax compiles. The render code, the
  embedded shaders, and the core's generated bindings (which call render
  functions) become `crucible_render_objects`, built after shaderfax;
  shaderfax links the rest (`crucible_core_objects`) and a stub with no
  embedded shaders. `crucible_core` and `crucible_core_static` contain both
  object libraries, as before.

## Why

A step that adds several graph passes keeps a feature in one place for
plugins to insert or replace; one entry per graph pass would scatter a
shadow system over several entries that must move together. Registered keys
cost a name lookup once and an array index per frame, and let plugins
share resources the engine never heard of; a fixed struct can't grow.
Using Slag's types directly avoids a layer that would only rename them.
Recording in parallel is what the graph and the job system were designed
for. Embedded engine shaders can't go missing or be stale, and every
launcher, test, and tool gets them for free.

Given up, for now: caching compiled graphs across frames (Slag ADR 0072),
and C# render steps (the batched command API, M6).

## As built (M5 step 2)

- `crucible/RenderSchedule.h`: `RenderSchedule` (`add`, `insertBefore`,
  `insertAfter`, `replace`, `remove`), `renderSchedule()` (the renderer's,
  holding Clear and Triangle on first use), `RenderStep` (a name, an
  `addPasses` function and its user data, and optional `prepare` and
  `release` hooks with the device), `RenderStepContext`, `BlackboardKey`,
  `registerBlackboardKey`, `backbufferKey()`. Steps added to a running
  renderer are prepared at once; a failed prepare leaves them out. A frame
  whose step reads an unpublished key is reported and returns InvalidData;
  the next frame starts cleanly.
- The Triangle step culls back faces with clockwise front faces (ADR 0024),
  so a winding mistake shows as no triangle; its pipeline is made from the
  embedded `triangle` shader when the renderer starts.
- Engine shaders: `crucible_embed_shaders` (`cmake/CrucibleShaders.cmake`)
  runs shaderfax and `cmake/EmbedShaders.cmake`, which writes the byte
  arrays and the table `engineShader(name)` searches. NativeAOT builds link
  `slag_rendergraph` too (`cmake/CrucibleDotnet.cmake`).
- Off-screen surfaces enter their first frame from `Undefined` and every
  later one from `RenderTarget` (found by Vulkan validation).
- An existing Linux build tree from before this change can report a
  dependency cycle through shaderfax: Ninja's dependency log remembers
  shaderfax's old link inputs. Deleting `build/<preset>/.ninja_deps` fixes
  it (a full rebuild).
- Tests: editing the schedule by name, duplicate and missing names, the
  default steps, blackboard keys; on every backend, steps publishing and
  reading through the blackboard, a failed prepare, an unpublished read;
  off-screen frames checked for the clear color in the corners and the
  triangle's three colors where its corners are; the by-hand window test
  (Mailbox at 144 Hz on RADV, no validation errors).
