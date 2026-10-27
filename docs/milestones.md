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
relationships, and change detection (transforms need them); how plain C#
services are registered and found.

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

**Decides:** exact file layouts.

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
