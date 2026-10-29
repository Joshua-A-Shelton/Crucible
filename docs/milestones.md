# Milestones to v1.0

**Status:** Accepted (2026-10-26).

Each milestone ends in something that runs and is covered by tests.
**Done when** is the concrete proof. **Settles** lists open checks from
`docs/open-items.md` that the milestone answers by testing; **Decides**
lists deferred design items that get their ADR when the milestone reaches
them, with something concrete to test against.

The order puts the riskiest assumptions first: the C#↔C++ boundary (ADRs
0001, 0011, 0017) is proven before the first pixel, and latency is
measurable from the first frame (goal 4).

**v1.0** is M0–M11 plus whatever the turn-based RPG's first vertical slice
turns up (ADR 0009).

## M0 — Tooling

- Repository skeleton, CMake presets (`debug`, `release`), clang-format.
- **Slag as a dependency:** FetchContent with a pinned tag by default; during
  development, the local `~/Claude/Slag` checkout through
  `FETCHCONTENT_SOURCE_DIR_SLAG`, so Crucible and Slag can be changed side
  by side (ADR 0015).
- .NET SDK detection and C# projects built from CMake.
- C++ and C# test frameworks, hooked into CTest.
- A CI workflow describing the verification matrix (Linux and Windows).
  Hosted CI is off for now; the matrix runs by hand on the owner's machines
  (CLAUDE.md §6).

**Done when:** CMake builds a C++ library and a C# project, and both test
suites run in the verification matrix.

**Decides:** the C# test framework (third-party, with its license).

**Status (2026-10-26):** done locally. Debug and release presets build with
GCC and Clang on Linux; both suites pass (doctest: 2 tests, TUnit: 1);
clang-format and `dotnet format` checks pass. TUnit and doctest chosen in
ADR 0029. Windows was verified with M1 step 8 (below): the matrix has run
on both machines.

## M1 — Risk spike: the boundary

The smallest program that exercises the boundary's riskiest assumptions.

- A thin C++ launcher that hosts CoreCLR through `hostfxr` and calls one
  `[UnmanagedCallersOnly]` entry point in the engine's C# layer
  (ADR 0011).
- C# calling into the C++ core library through `LibraryImport`.
- A NativeAOT build with the launcher, core, and C# statically linked into
  one executable, with direct P/Invoke.
- One hot-reload cycle: a game assembly in a collectible
  `AssemblyLoadContext`, unloaded and reloaded, with the weak-reference
  unload check (ADR 0001, 0011).
- A minimal binding generator: one annotated function and one annotated
  struct, with shims, both C# layers, and layout checks on both sides
  (ADR 0017), run from CMake with a depfile.

**Done when:** the same test passes under JIT and AOT on Windows and Linux,
and a reload cycle unloads cleanly.

**Settles:** whether ADRs 0001, 0011, and 0017 hold up as written; NativeAOT
static linking on both platforms; the ClangSharp pipeline.

**Status (2026-10-26):** steps 1–7 done on Linux (GCC and Clang, debug and
release; 11 CTest entries pass). ADRs 0001 and 0011 hold as written; static
NativeAOT linking works, so its shared-library fallback isn't needed. ADR
0017 was amended: the generator binds the system's libclang itself instead
of using ClangSharp (about 470 MB of packages, and no built-in headers).
Step 8, Windows (MSVC, AOT on Windows, finding libclang.dll), remains.

**Status (2026-10-26, step 8):** done on Windows 11 with MSVC 19.44: all 13
CTest entries pass, JIT and AOT, including a non-ASCII path test. Windows
uses the static C runtime and the exact SDK is pinned (ADR 0031). Verified
by hand on both machines; M1 is done.

## M2 — Platform and first pixels

- SDL3 window and input events with timestamps, behind Crucible's platform
  API (ADR 0018).
- Slag device and swapchain; clearing the screen.
- Frame pacing through `waitForNextFrame`; `Mailbox` with the
  double-buffered `Fifo` fallback (ADR 0003).
- A minimal C# loop driver with BeginFrame and Input phases first
  (ADR 0013).
- **A latency-measurement harness** for input-to-photon latency and
  displayed-state lag (ADR 0003).

**Done when:** a paced window clears every frame, and input-to-photon
latency can be measured.

**Settles:** SDL timestamp accuracy (first pass); swapchain image counts
when switching `Mailbox` ↔ `Fifo`; pacing under `Mailbox`; how Slag maps
Vulkan's clip-space Y.

**Status (2026-10-26):** steps 1–6 done and verified on Linux (GCC and
Clang, debug and release) and Windows (MSVC, debug and release; windowed
runs on D3D12 and Vulkan): 28 CTest entries, the C# suites under JIT and
NativeAOT. The binding generator handles enums, spans, strings, and
`Result`; the launcher owns a Slag device; C# drives BeginFrame, Input, and
RenderSubmit over a window or an offscreen surface; the latency harness
measures input to present completion. Settled: swapchain image counts (ADR
0003's amendment). Measured, decision open: pacing under `Mailbox`
(`docs/open-items.md`). Then settled before sign-off: SDL timestamp
accuracy, measured with injected OS input on Wayland, X11, and Windows (ADR
0018's amendment; raw keyboard on Windows by default, X11 flagged), and
Vulkan's clip-space Y (ADR 0024's amendment). Gamepads and the Steam Deck
are carried forward. Signed off by the owner on 2026-10-26; M2 is done.

## M3 — Foundations

- Job system: worker threads, frame and background priorities, waiting
  threads run jobs; locked queues first (ADR 0014).
- Math library: storage types and scalar math first, tested against
  `System.Numerics`; then register and wide SIMD types (ADR 0024).
- ECS core: chunked archetypes, opt-in sparse storage, runtime-registered
  components from C#, typed spans, managed component pools, deferred
  command buffers (ADR 0012, 0023).
- Full frame phases: editable list, before/after constraints, fixed-tick
  group with a step cap, sync points (ADR 0013).

**Done when:** thousands of entities are updated by both a C# system and a
C++ system, in parallel through the job system, with structural changes
applied at phase ends; math tests pass against `System.Numerics`.

**Decides:** phase names; whether x64 requires AVX2; ECS hierarchy,
relationships, and change detection (transforms need them; decided here,
built as far as the demonstration needs, full transform propagation in
M5).

**Steps** (agreed 2026-10-26; one commit each, verified on both machines;
the decision each one needs comes first):

1. C++ memory and containers. Decides A: allocator hooks, and Crucible's
   own containers vs. the standard library's.
2. Math storage types and scalar math in C++ (`Vec2/3/4`, `Quat`, `Mat4`),
   tested against `System.Numerics` through the bindings. Decides B: how
   the generator maps C++ math types onto `System.Numerics`.
3. Math compute types: a register vector and 4- and 8-lane wide types, with
   SSE/AVX2, NEON, and scalar paths, tested against the scalar math and
   benchmarked. Decides C: whether x64 requires AVX2.
4. Job system in C++: workers, per-worker queues (locked first) with
   stealing, frame and background priorities, waiting threads run jobs.
   Decides D: the job API (handles and dependencies, counters, or
   parent/child jobs) and the worker count.
5. Jobs from C#: managed job bodies through the engine's trampoline (ADR
   0014), a parallel-for, under JIT, AOT, and hot reload.
6. ECS core in C++: entity IDs, runtime component registration, chunked
   archetypes, opt-in sparse storage, queries, deferred command buffers.
   Decides E (entity ID layout, chunk size, query API), F (hierarchy and
   relationships), and G (change detection), each in its own ADR.
7. ECS from C#: components registered from C# types, typed span views per
   chunk, managed component pools, C# systems in parallel chunk loops.
   Decides H: source generator or runtime reflection for registration,
   managed "struct with references" pools, layout migration across hot
   reload.
8. Full frame phases: editable list, constraints, the fixed-tick group with
   a step cap, sync points applying deferred ECS changes; C++ and C#
   systems registered into phases. Decides I: phase names and the system
   registration API.
9. The demonstration: thousands of entities updated by a C# and a C++
   system in parallel, structural changes applied at phase ends, timed.

**Status (2026-10-27):** steps 1–9 done and verified on Linux (GCC, debug
and release, sanitizers) and Windows (MSVC, debug and release), JIT and
NativeAOT. The demonstration is `samples/Particles` (ADR 0037): 100,000
particles moved by C++ and C# systems in parallel, with creations and
destroys applied at sync points, in about 0.5 ms per fixed step (release).
It found and fixed two ECS problems: quadratic record growth and recorded
creations never reusing indices (ADR 0023's second amendment). Decided:
phase list (names still placeholders), AVX2 (ADR 0024), hierarchy (0033),
change detection (0034). Signed off by the owner on 2026-10-27; M3 is done.

## M4 — Assets and serialization

- GUIDs, the asset database, and the content-hash import cache (ADR 0021).
- Model import: glTF through cgltf, FBX through ufbx, meshoptimizer,
  mirroring into left-handed space (ADR 0022, 0024).
- Texture import: stb_image, linear-space sharp-filtered mipmaps, BC7 and
  BC5 through bc7enc_rdo.
- Source-generated serializers: JSON for scenes and prefabs, Crucible's
  binary format for imported data; versioning and migration.
- Prefabs with outermost-level overrides.

**Done when:** importing a glTF, saving the scene as JSON, and reloading it
gives an identical scene; mirroring tests show no inside-out faces and
correct left and right.

**Decides:** exact file layouts; how plain C# services are registered,
found, and saved (moved from M3 on 2026-10-26: saving them needs this
milestone's serialization).

**Steps** (agreed 2026-10-27; one commit each, verified on both machines;
the decision each one needs comes first):

1. The serializer generator, JSON first: reading and writing code for
   components and plain C# types, with defaults, skipped removed fields,
   "formerly named", and versioned migrations. Decides J: our own generator
   over `Utf8JsonWriter`/`Utf8JsonReader`, or `System.Text.Json` source
   generation underneath; the attribute model.
2. Crucible's binary format for imported data, read directly by C++.
   Decides K: the container layout (header, sections, alignment,
   versioning), and whether it is memory-mapped.
3. GUIDs, the asset database, and the content-hash import cache. Decides L:
   the project folder layout, the sidecar format, the content hash, and
   where the asset pipeline lives (editor-side, not the shipped engine).
4. Scenes: a world to JSON and back, with the hierarchy and entity
   references in components. Decides M: how plain C# services are
   registered, found, and saved.
5. Texture import: stb_image, linear-space sharp mipmaps, BC7 and BC5
   through bc7enc_rdo, into the binary format.
6. glTF import: cgltf and meshoptimizer, mirrored into left-handed space,
   the node hierarchy as a prefab-like asset, and the mirroring tests.
   Decides N: vertex layouts and the mesh format.
7. FBX import through ufbx, into the same mesh format.
8. Prefabs: instantiation, nesting, and outermost-level overrides.
9. The demonstration: import a glTF, save the scene as JSON, reload it,
   and compare.

Cooking and asynchronous loading (ADR 0021) wait for a later milestone.

**Status (2026-10-28):** steps 1–9 done and verified on Linux (GCC, debug
and release; ASan/UBSan and TSan for the importers) and Windows (MSVC,
debug and release). The demonstration is `samples/SceneRoundTrip` (ADR
0046): a lopsided glTF model and its texture imported, placed in a scene
as plain and nested prefab instances with overrides, saved as JSON,
reloaded into an identical world (compared entity by entity and as bytes),
with no inside-out faces and the model's right arm on +X. Decided: J
(0038), K (0039), L (0040), M (0041), N (0043), plus FBX (0044) and prefabs
(0045). Signed off by the owner on 2026-10-28; M4 is done.

## M5 — Renderer

- `slag::rendergraph` integration and the extensible pass list (ADR 0015).
- Slang materials: shading-model interfaces, toon and PBR, surface
  functions, the variant policy, pipelines created while loading
  (ADR 0025).
- Bindless material buffer laid out from Slang reflection.
- Depth prepass, light clustering, forward MSAA, specular AA,
  alpha-to-coverage, tonemapping (ADR 0004).
- CPU culling with instancing on the job system.

**Done when:** an imported, lit scene renders the same on D3D12 and Vulkan
(image comparison), with no temporal techniques.

**Decides:** shadows; lighting and ambient light; post-processing and the
post-tonemap MSAA resolve; outlines.

**Steps** (agreed 2026-10-28; one commit each, verified on both machines;
the decision each one needs comes first):

1. Shader toolchain: Slang compiled to DXIL and SPIR-V, compiled shaders
   loaded at run time, and C++/C# layout checks from Slang reflection.
   Decides O: where Slang runs (a build-time tool, a library in the
   importers, or both) and the compiled-shader file format.
2. The render graph and the pass list: the frame on `slag::rendergraph`,
   an ordered pass list with named insertion points, a first pass drawing
   a triangle. Decides P: the C++ pass API and how passes declare
   resources.
3. GPU assets: imported MESH and texture files loaded into Slag buffers
   and textures, with descriptor slots and delayed destruction; loading is
   synchronous for now. Decides Q: runtime asset handles and the loading
   API, and how mesh data sits in GPU memory.
4. Transforms and cameras: world-transform propagation and a camera
   component. Decides R: reverse-Z and an infinite far plane, late
   latching of the camera, and whether `LocalTransform` becomes a C++
   struct mirrored into C#.
5. Drawing meshes: CPU frustum culling on the job system, instanced
   batches, the depth prepass, and an unlit forward pass with MSAA, front
   faces clockwise.
6. Materials: Slang shading-model interfaces, surface functions, the
   bindless material buffer laid out from reflection, the variant policy,
   pipelines created while loading, PBR and toon. Decides S: the material
   asset format and how imported materials become materials.
7. Lighting: light components, clustered light lists (compute), and
   ambient light. Decides T: the lighting and ambient model.
8. Shadows: cascaded sun shadows snapped to texels, and an atlas for local
   lights. Decides U: shadow techniques and filtering.
9. The rest of the frame: sky, the sorted transparent pass, specular AA,
   alpha-to-coverage, tonemapping, the post-tonemap MSAA resolve, and
   outlines. Decides V: the post-processing chain, the resolve, and
   outlines.
10. The demonstration: an imported, lit scene rendered offscreen on D3D12
    and Vulkan, the images compared within a tolerance.

The batched C# command API stays in M6; in M5, C# provides components
(cameras, lights, transforms) that C++ render systems read.

## M6 — UI system

- Text: HarfBuzz shaping, FreeType exact-size glyph atlases (ADR 0020).
- Retained element tree, custom-draw element, panels (ADR 0019).
- Themes and style classes; transitions and tweens.
- Event routing, focus, tab order, gamepad directional navigation, focus
  scopes; DPI scaling with pixel snapping.
- Rendering through the batched command API.

**Done when:** a sample menu is navigable by mouse, keyboard, and gamepad,
with crisp text at several DPI scales.

**Decides:** the C# rendering (batched command) API, which the UI is the
first user of.

## M7 — Editor v0

- Editor launcher (ADR 0016), docking, a scene viewport, hierarchy, an
  inspector driven by generated metadata, an asset browser.
- Play in the editor's process; hot reload; Run standalone.
- Shader hot reload (ADR 0025).

**Done when:** open a project, place an imported model, edit a property,
press Play, edit C# and see it hot-reload, and launch a standalone run.

**Decides:** the C++ runtime extension mechanism, if needed by then;
interfaces for the replaceable subsystems built so far.

## M8 — Animation and audio

- Skeletal runtime: sampling, blending, hierarchy pass, IK; compute
  pre-skinning; native pose buffers; data-asset state machines; exact-time
  events and windows; root motion (ADR 0026).
- Audio: SDL3 output, Crucible's mixer on a real-time thread, the lock-free
  command queue, sample-accurate scheduling, PCM/ADPCM and streamed Vorbis
  (ADR 0028).

**Done when:** an animated character's footstep sounds stay in sync at 30
and 144 fps.

**Settles:** SDL3 audio output latency per platform (miniaudio if it falls
short).

## M9 — Physics

- Box3D and Box2D v3 behind Crucible's physics API, on the job system,
  stepped in the fixed tick with batched ECS sync (ADR 0027).
- Character mover driven by root motion; ray casts, shape casts, overlaps,
  sensors.
- Swept, query-based hitboxes with exact hit times.

**Done when:** a character walks a mesh level, and a test hit registers
identically at 30 and 240 fps.

## M10 — Shipping

- Cooking: dependency walk from root scenes, archives, async loading on
  background jobs (ADR 0021).
- JIT shipping builds (ReadyToRun on by default) and AOT shipping builds
  (ADR 0001).
- Plugin registration in both shipping modes (ADR 0011).

**Done when:** a packaged sample runs from its archives in both shipping
modes.

**Settles:** the Steam Deck check, when a device is available (ADR 0008).

## M11 — Pixel-art 2D path

Before the first game: UI tricks may need it, and it is less likely to be
built if it waits.

- Dedicated 2D renderer: sprite batching, draw-order sorting, 2D lighting
  (ADR 0005).
- Pixel-perfect camera: virtual resolution, integer scaling with a sharp
  scaler for other sizes, nearest filtering, pixel snapping.
- Compositing with 3D in one frame; tilemaps and frame animation, usable
  by any game.

**Done when:** a pixel-art sample is pixel-exact at several window sizes and
composites over a 3D scene.

**Decides:** the pixel-art path details.

## After M11: the first game

The turn-based RPG's vertical slice. Whatever it needs that the engine
doesn't have becomes the last work before v1.0.
