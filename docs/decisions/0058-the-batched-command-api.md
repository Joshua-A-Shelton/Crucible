# 0058 — The batched command API

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md).

## Context

ADR 0015 lets C# add render passes through a batched command API: C#
writes compact commands into native memory, which native code replays as
Slag calls, so the boundary is crossed once per pass, not per draw. ADR
0048 built the render schedule for C++ steps and left C# steps for M6;
ADR 0019 makes the UI the API's first user. M6 step 2 decides X: the
command set and how C# steps declare resources.

## Options

**When C# writes commands:** in the step's `AddPasses`, called by the
schedule while the frame is built, like a C++ step; or from C# systems
earlier in the frame, into streams a native step only plays back (no
calls from native code into C#, but no access to the graph's handles).

**Per-frame data:** one upload buffer per surface, read through a fixed
resource-heap slot, with offsets in push constants; or slices of it
bound as vertex and index buffers.

**Resources:** this frame's only (the blackboard, transients, existing
assets' slots); or also C#-owned textures and buffers that last across
frames.

**A step that throws:** fails the frame; or is skipped while the rest of
the frame renders.

## Decision

1. **A C# step is an `IRenderStep`** (`Prepare`, `AddPasses`, `Release`),
   placed in the schedule by name with `RenderSchedule.Add`,
   `InsertBefore`, `InsertAfter`, `Replace` and `Remove`, among the C++
   steps. Native code calls it through one engine trampoline with a step
   ID, as jobs do (ADR 0014), so it never holds a pointer into a game.
2. **Commands are written during `AddPasses`.** `RenderStepContext` reads
   and publishes the blackboard, makes transients and frame data, and adds
   passes; a `RenderPass` declares its uses and attachments with direct
   native calls (a handful per pass); `RenderCommands` writes records
   straight into native memory.
3. **Playback is native, on the job threads,** when the graph records the
   pass. C# never runs during recording.
4. **Per-frame data is the frame data:** `Upload<T>` returns a
   `FrameData<T>` (a span and its offset); shaders read it as a
   ByteAddressBuffer through `FrameDataDescriptor`, and a slice of it can
   be the constant buffer.
5. **Resources last one frame:** blackboard entries, transient textures
   and buffers, and loaded textures' slots (`RenderAssets`). C#-owned
   persistent resources wait for a step that needs them.
6. **A step that throws fails the frame:** no later step runs, nothing is
   submitted, and the render system rethrows the exception, naming the
   step; the frame loop reports it and the next frame starts cleanly.
7. **The commands:** `SetPipeline`, `PushConstants` (up to 128 bytes),
   `SetConstantBuffer` (frame data or a buffer the pass uses as one),
   `SetViewport`, `SetScissor`, `Draw`, `Dispatch`, `BeginLabel` and
   `EndLabel`. Vertex and index buffers, indexed and indirect draws,
   copies and queries are added when a step needs them; barriers stay
   the graph's.

## Details

- **Records** (`crucible/RenderCommands.h`): a 4-byte header (type, and
  length in 4-byte words), the command's struct, then any bytes it
  carries (push constants, a label's NUL-terminated text), padded to 4.
  The structs are exposed (ADR 0017), so the C# writer uses generated
  mirrors whose layouts are checked at start-up.
- **Checked when the step returns,** on the main thread: known types and
  lengths, live pipelines of the pass's kind, draws and dispatches in the
  right kind of pass after a pipeline, push constants within 128 bytes,
  constant buffers 256-aligned within the frame data or declared, labels
  balanced. A bad record is reported (step, pass, record number) and
  fails the frame, so playback never meets one. It runs in every build:
  it walks the records once, and C# is where mistakes are expected.
  `RenderCommands` also refuses a draw in a compute pass (and the like)
  as it's written, with an exception.
- **A pass's records** are a native buffer C# writes into directly,
  handed over again (and maybe moved) only when it fills; buffers are
  pooled per surface and keep their capacity, so a warmed-up frame
  allocates nothing.
- **Declaring:** a pass's uses and attachments are declared before the
  step's next pass (the graph lists uses in pass order); commands may be
  written to any of the step's passes until it returns. Every C# pass
  also reads the frame's ViewConstants, bound as its constant buffer when
  it starts.
- **Descriptors:** `RenderPass.Use` returns the slot a shader reads or
  writes a texture through (Texture2D, Texture2DMS or Texture2DArray by
  the texture; RWTexture2D or RWTexture2DArray), and a ByteAddressBuffer
  slot for buffers a C# step made this frame (their size is known). The
  slots are written while the pass records: 40 per frame slot per
  surface, past the C++ steps' (`descriptorsPerSurface` is now 120).
- **The frame data** is collected in CPU memory while steps run (spans
  stay valid as it grows) and copied into the frame slot's upload buffer
  once, before the graph compiles: upload memory is write-combined, so
  one sequential copy is cheaper than C# writing into it piece by piece.
  The buffer grows in powers of two from 64 KiB; uploads are 256-aligned.
- **Pipelines** come from a shader asset through the asset reader:
  `createGraphicsPipeline(shader, vertex, pixel, GraphicsPipelineState)`
  (a blend mode, culling, topology, depth test, up to 4 color formats, a
  depth format, the sample count) and `createComputePipeline`. There are
  no vertex buffers, so shaders pull vertices from the frame data or a
  buffer. `destroyPipeline` waits until the GPU is done with it.
- **Lifetime:** a step's `Release` runs when it leaves, when the sample
  count changes (then `Prepare` again), and before the renderer stops:
  the host removes every C# step when its loop ends. Unloading a game
  removes the steps whose types it holds (ADR 0011).
- **Threading:** main thread only, like the renderer; playback runs on
  job threads and reads only what the main thread finished writing.

## Why

Writing in `AddPasses` keeps C# steps the same shape as C++ ones: they
see the graph's handles, the surface size and the blackboard, and the
schedule edits them by name. Native playback keeps managed code off the
recording threads. Checking records once on the main thread makes
playback a plain switch with no failure paths. One frame-data buffer with
one slot needs no vertex-layout state in pipelines and matches how the
lights are already read. Frame-only resources and the short command list
are what the UI needs; the rest is added against a real use.

Given up, for now: persistent C#-owned GPU resources; vertex and index
buffers; indexed, indirect and multi-draw commands; copies; queries;
choosing the sample positions or a stencil test; and graph buffers not
made by a C# step getting a slot (the C++ steps' buffers are read through
ViewConstants' slots).

## As built (M6 step 2)

- Native: `crucible/RenderCommands.h` and `RenderCommands.cpp`; the
  blackboard's keys exposed from `crucible/RenderSchedule.h`; each
  surface's `ManagedState`, begun before the steps run and finished
  (frame data copied) before the graph compiles.
- C#: `Crucible.Render`'s `IRenderStep`, `RenderSchedule`,
  `RenderStepContext`, `RenderPass`, `RenderCommands`, `FrameData<T>`,
  and `BlackboardKey`'s named keys.
- Tests:
  - Native (`RenderCommandsTests.cpp`): placing steps by name, and on
    every backend a compute pass filling a transient buffer from frame
    data and a graphics pass drawing quads from it, scissored, checked
    pixel by pixel, with six kinds of mistake each failing only its
    frame, and Release and Prepare around a sample count change.
  - C# (`RenderScheduleTests.cs`, JIT and NativeAOT): edits, errors,
    blackboard keys; hot reload: a game's step left in the schedule
    doesn't keep the game loaded.
  - `launcher.jit.render-steps`: a C# game's step drawing through every
    command, a deliberate exception failing one frame, the frame read
    back and checked.
