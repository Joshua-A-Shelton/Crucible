# CLAUDE.md — Crucible

Crucible is a game engine built on Slag
(https://github.com/Joshua-A-Shelton/Slag), Josh's thin abstraction over
Direct3D 12 and Vulkan. The primary focus is 3D; excellent 2D support is a
secondary focus.

Licensed under zlib (`LICENSE`, ADR 0007); dependencies must stay
compatible.

This file is the source of truth for project goals, constraints, and
conventions. If a request conflicts with it, point out the conflict before
writing code. **Status:** charter and core architecture complete; the
build order is `docs/milestones.md` (2026-10-26). Remaining design items
are decided as milestones reach them (`docs/open-items.md`).

---

## 1. Goals

Stated by the project owner and refined one at a time (2026-10-24).

1. **Native-side speed.** Shipped-game performance comes first; editor
   iteration speed matters, but loses when the two conflict.
   - The C++ core spends as little CPU time per frame as possible and
     scales across CPU cores.
   - **Speed in the core, hackability at the edges.** Extension points sit
     around the core's hot loops, not inside them.
   - Hot paths follow Slag's hot-path rules (no allocations after warm-up,
     no locks or virtual dispatch) as a **guideline, not a law**. A
     deviation is fine where it earns its keep, with a comment saying why.
   - Calls between C# and native code are cheap: modern .NET
     source-generated interop (`LibraryImport`), blittable types, no
     runtime marshalling on hot paths. Not Mono.
   - Speed claims are measured, not assumed.
2. **C# scripting that can conditionally compile to native** (ADR 0001).
   - In the editor, C# runs on CoreCLR (JIT) with hot reload: unload the
     game assemblies, recompile, reload, restore serialized state.
   - Each game chooses how it ships: **JIT** (CoreCLR, can load C# mod
     assemblies at runtime that plug into and change the running game) or
     **AOT** (NativeAOT, fully native, no runtime assembly loading).
   - The same game code must work under both: .NET's AOT and trim
     analyzers are errors for engine, plugin, and game assemblies in every
     configuration. Mods are exempt.
   - JIT-shipped games use ReadyToRun precompilation by default (opt-out
     per game).
3. **A visual editor that supports plugins** (ADR 0002). Plugins can
   contain both game code and editor code; editor code is removed from
   final builds.
   - Editor code lives in editor assemblies; an editor-only symbol is
     allowed for small cases in runtime code, and a build check compiles
     the shipping configuration.
   - Attributes expose data to the editor (and partly how); custom editors
     and tools live in editor assemblies.
   - Plugins are C#, source-only in v1. C++ never extends the editor.
   - The game is the root plugin (`<GameName>.dll`).
4. **Low latency** (ADR 0003). Displayed frames should match the game
   state as closely as possible.
   - Measured as input-to-photon latency (headline) and as how far the
     displayed state lags the simulation.
   - Just-in-time frame pacing on by default; games can opt out or make it
     a player setting.
   - Frame generation is never on by default; opt-in plugin only.
   - Present mode is the player's choice; default `Mailbox`, falling back
     to double-buffered `Fifo`. Never three-image `Fifo`.
   - Default loop: variable rate, with the camera and player-input path
     always on the latest state; physics on a fixed tick; physics bodies
     interpolated by default (extrapolate or raw per body).
   - The game loop is easy for a game to modify for its situation (goal
     7).
5. **Image clarity** (ADR 0004). Prefer techniques that keep the image
   sharp over ones that trade clarity for temporal stability. Modern
   techniques such as DLSS are permitted, but not at the expense of goals 4
   and 5.
   - **Every effect enabled by default must look correct in a single
     frame, with no temporal accumulation.** Temporal effects are opt-in.
     Spatial filtering within one frame is allowed.
   - Default AA: MSAA + specular AA + alpha-to-coverage; SMAA optional.
     The renderer leans clustered forward.
   - Native resolution by default; upscalers and DLAA are player options.
   - Motion blur, depth of field, film grain, and chromatic aberration are
     off by default.
6. **3D first, excellent 2D second** (ADR 0005).
   - Pixel art is the true 2D path: a dedicated 2D renderer on the shared
     foundation, able to composite with 3D. HD 2D and mixed 2D/3D use the
     3D renderer.
   - Pixel-perfect rendering is first-class (virtual resolution, integer
     scaling, pixel snapping).
   - Dedicated 2D physics; tilemaps and frame-based animation built in.
     2D gameplay features are usable by any game, whichever renderer
     draws it.
7. **Hackable, with good defaults** (ADR 0006). Games can reshape the
   engine to their needs, and the editor picks up on those changes.
   - In scope: configure, extend (C#), replace whole subsystems, and
     modify the C++ source (buildable from source; no promise of easy
     merges with engine updates).
   - The editor sees what's exposed to C#, through source-generated
     metadata. C++ reaches the editor by being exposed to C#, which should
     be cheap. No direct C++ reflection.
   - Subsystems are replaceable at coarse boundaries (per frame or per
     batch, never per object).
   - A new project runs with no configuration; every default can be
     overridden at the level it lives at.
   - No art-style lock-in: Crucible's own games are stylized (ADR 0010),
     but a game can use any art style.

## 2. Non-goals

- **Strictly a game engine.** Slag is general purpose; Crucible is not.
  Design decisions optimize for games and may rule out non-game software
  (visualization, film, general tools). Crucible's own editor and
  game-development tools are the exception.
- **No mod sandboxing.** C# mods are trusted code with the same access as
  the game process (ADR 0001).
- **No Mono, no IL→C++ translation.** C# runs on CoreCLR or NativeAOT only
  (ADR 0001).
- **No dependence on temporal accumulation** for default image quality.
  Temporal techniques are opt-in, never required (ADR 0004).
- **No frame generation by default.** Opt-in plugin only (ADR 0003).
- **No C++ editor extensions.** The editor is extended in C# only
  (ADR 0002).
- **Not state of the art in v1:** no virtualized geometry (Nanite-style),
  no real-time global illumination that depends on denoising, no hardware
  ray tracing. Revisited after v1.
- **No platforms beyond Slag's:** no macOS, Android, or iOS. Revisited
  with the platforms item in `docs/open-items.md`.

## 3. Constraints

- **Rendering is built on Slag.** Crucible inherits Slag's platform and API
  baseline (see Slag's CLAUDE.md §3) and its explicit, externally
  synchronized threading model.
- **Audience and scope** (ADR 0009): Josh's engine, possibly shared with
  collaborators; public use is allowed but not designed for. A limited
  feature set, driven by Crucible's own games, with good documentation for
  what exists. First target: a turn-based 3D RPG; later, a 3D action RPG.
  Semantic versioning.
- **Reference targets** (ADR 0010): stylized, anime-esque art with
  soft-banded toon shading; action RPG feel between Zelda and Kingdom
  Hearts, faster, with tight parry windows. The first game holds a steady
  60 fps on Steam Deck at 1280×800 with MSAA.
- **Platforms** (ADR 0008): games ship on Slag's platforms (Windows and
  Linux, x64; ARM64 compiles but is unsupported in v1) plus Steam Deck as
  an explicit target. Consoles stay possible and are never referenced by
  name in public code. The editor runs on Windows and Linux with **1:1
  feature parity** (hard requirement); integrations with single-platform
  external tools (e.g. PIX) may exist where the tool does.
- **Third-party licenses must be highly permissive.** zlib is preferred;
  MIT and similar are acceptable. Anything else (e.g. vendor SDK licenses)
  needs an explicit decision from the project owner, recorded in an ADR.
  Each shipped dependency's notice carries over to games (ADR 0007).
- **Approved dependencies:** Slag (zlib; FetchContent with a pinned tag,
  or the local checkout through `FETCHCONTENT_SOURCE_DIR_SLAG` during
  development), .NET (MIT; ADR 0001), SDL3
  (zlib; ADR 0018), HarfBuzz (Old MIT; ADR 0020), FreeType (FTL, which
  asks for a credit in product documentation; ADR 0020), Box3D and Box2D
  v3 (MIT; ADR 0027), stb_vorbis (public domain or MIT; ADR 0028), zstd
  (BSD-3-Clause, accepted by the owner: shipped games include
  `licenses/zstd.txt`; ADR 0039). Editor only
  (never shipped): cgltf (MIT), ufbx (MIT or public domain), meshoptimizer
  (MIT), stb_image (public domain or MIT), bc7enc_rdo (MIT or public
  domain); ADR 0022; Slang (Apache 2.0 with LLVM exception; ADR 0025);
  dr_wav, dr_flac (public domain or MIT-0; ADR 0028); System.IO.Hashing
  (MIT; XXH128 for the import cache, ADR 0040).
  Build-time only: the system's libclang (Apache 2.0 with LLVM exception),
  through Crucible's own bindings (ADR 0017); Microsoft.CodeAnalysis.CSharp
  (MIT), the Roslyn API for Crucible's source generators (ADR 0036). Tests only: TUnit (MIT),
  doctest (MIT); ADR 0029. Fallbacks, not used: enkiTS
  (zlib; ADR 0014), Jolt Physics (MIT; ADR 0027), miniaudio (public
  domain or MIT-0; ADR 0028), MSTest (MIT; ADR 0029). Nothing else without an ADR.

## 4. Architecture

Accepted architecture decisions, summarized. Details live in the ADRs.

- **Hosting** (ADR 0011): a thin C++ launcher owns `main()`, platform init,
  and shutdown, around the C++ core library. It starts .NET and calls one
  managed entry point in the engine's C# layer, which drives the frame
  loop. Plugins register through source-generated entry points in every
  mode. JIT/editor builds load the core as a shared library; AOT builds
  link everything into one `<GameName>.exe`.
- **Hot-reload rule** (ADR 0011): native code holds references only into
  the engine's C# layer, never into game or plugin assemblies. Native code
  identifies game objects by IDs, not `GCHandle`s.
- **Data model** (ADR 0012): hybrid. ECS for the world, shown in the
  editor as a hierarchy. Blittable components live in native memory,
  shared by C++ and C# systems without copies. Managed components are
  pay-for-use (native column holds an index into a per-type C# pool).
  Systems run in frame phases; structural changes are deferred. Logic not
  about world entities (turn order, inventory, quests, dialogue) is plain
  C# that refers to entities by ID.
- **ECS storage** (ADR 0023): Crucible's own, chunked archetype tables
  with opt-in sparse storage for churny components; component types are
  registered at runtime. flecs is the reference design and fallback.
- **Math and coordinates** (ADR 0024): Crucible's own C++ math library.
  Storage types byte-identical to `System.Numerics`, separate SIMD compute
  types (register and wide/SoA), scalar fallbacks
  (`docs/learning/storage-and-simd-types.md`). Row vectors stored row-major
  in C++, C#, and shaders. Left-handed, Y-up, +Z forward, meters;
  importers mirror right-handed sources.
- **Frame phases** (ADR 0013): the frame is an editable, ordered list of
  phases; systems register into a phase with before/after constraints,
  sorted when the schedule is built. The fixed tick is a phase group with
  a step cap. Structural changes apply at the end of each phase.
  Parallelism in v1 is inside systems. The loop driver is replaceable.
  The pacing wait comes first, then input.
- **Job system** (ADR 0014): Crucible's own, native, one pool shared by
  C++, C# (through a wrapper and the engine's dispatcher), and third-party
  libraries. Frame and background priorities; work stealing, no fibers;
  waiting threads run jobs. enkiTS (zlib) is the fallback. Exception:
  audio mixing has its own real-time thread (ADR 0028).
- **Audio** (ADR 0028): SDL3 device output (miniaudio fallback), Crucible's
  own mixer on a real-time thread fed by a lock-free queue, sounds
  scheduled at exact event times. PCM/ADPCM effects, streamed Vorbis music.
  Behind Crucible's audio API; Steam Audio after v1.
- **Renderer foundation** (ADR 0015): built on `slag::rendergraph`
  (limitations get fixed in Slag). The frame is an ordered, extensible
  list of render passes with named insertion points; core passes are C++,
  C# adds passes through a batched command API. Clustered forward with a
  depth prepass and MSAA. CPU culling with instancing in v1.
- **Materials and shaders** (ADR 0025): Slang, with shading models as
  interfaces specialized at compile time. Shaders (surface functions) and
  materials (parameter values) are separate. Variants only where
  unavoidable, all compiled ahead of time; pipelines are created only while
  loading. Material parameters live in one bindless buffer, laid out from
  Slang reflection. Shaders hot-reload in the editor.
- **Skeletal animation** (ADR 0026): Crucible's own runtime (ACL for
  compression when needed; ozz-animation as reference and fallback).
  Compute pre-skinning once per frame. Poses in native buffers referenced
  by index, animated in parallel per character. State machines are data
  assets with a C# API; events carry exact clip times; windows are time
  ranges; root motion supported.
- **Physics** (ADR 0027): Box3D for 3D (Jolt as fallback; checkpoint
  before the action RPG), Box2D v3 for 2D, behind Crucible's physics API
  on our job system. Steps in the fixed tick with batched native ECS sync.
  Combat uses swept, query-based hitboxes with exact hit times.
  Determinism is not a goal.
- **Binding generator** (ADR 0017): annotated C++ headers, parsed at build
  time with the system's libclang through Crucible's own bindings, generate C ABI
  shims, raw and idiomatic C# layers, struct mirrors with layout checks on
  both sides, and editor metadata. Only C-shaped signatures cross the
  boundary: plain values, `enum class`, plain-data structs, `std::span` and
  `std::string_view` parameters, and `crucible::Result` returns (throwing in
  C# unless marked `CRUCIBLE_EXPOSE_STATUS`); `CRUCIBLE_EXPOSE_AS` crosses as
  an existing C# type (the math types as `System.Numerics`), and struct
  fields may be fixed-size arrays. Each C++ namespace becomes a
  C# namespace, with its functions in an `Api` class. C++ changes need an
  editor restart; C# changes hot-reload.
- **Platform layer** (ADR 0018): SDL3's window, input, event, clipboard,
  and dialog parts, behind Crucible's own platform API. Input timestamps
  come from SDL events.
- **UI system** (ADR 0019): a retained element tree in C#, with a
  custom-draw element for tools; Crucible's own panel layout; text shaping
  and rasterization native; rendering through the batched command API.
  Latin text in v1, non-Latin after v1 (UTF-8 throughout, shaping behind
  an interface). Text (ADR 0020): HarfBuzz shaping, FreeType exact-size
  glyph atlases. Themes of style classes with a small selector set;
  gamepad directional navigation and focus scopes; layout snapped to whole
  pixels.
- **Assets and serialization** (ADR 0021): GUID identity; an asset
  database with a content-hash import cache; JSON for authored files,
  Crucible's own binary for imported data; source-generated serializers
  shared by the editor, cooker, saves, and hot reload. Prefabs nest, with
  overrides at the outermost level only. Shipping builds pack what the root
  scenes reach; loading is async. Importers (ADR 0022): glTF and FBX for
  models, our own linear-space sharp-filtered mipmaps, BC7/BC5
  compression.
- **Editor** (ADR 0016): written in C#, launched like a game (ADR 0011).
  Its UI is Crucible's own UI system, shared with games. Play runs in the
  editor's process by default; "Run standalone" launches a separate
  process.

## 5. Coding conventions

- **C++:** C++20, following Slag's conventions (Slag CLAUDE.md §10):
  namespace `crucible`; `PascalCase` types and files; `camelCase` functions,
  variables, and members (no `m_`); `CRUCIBLE_UPPER_CASE` macros; includes
  always `#include <crucible/...>`. `.clang-format` (copied from Slag) is
  authoritative.
- **C++ error handling** (ADR 0030): no exceptions, no RTTI. Fallible
  functions return `crucible::Result<T>`; programmer errors are caught by
  validation and asserts.
- **C++ memory** (ADR 0032): the core runtime (`src/native/core`) uses
  Crucible's own containers through Crucible's tagged allocator, and only
  non-allocating standard headers; tools, tests, and the launcher may use
  the whole standard library. Running out of memory is fatal; `tryAllocate`
  is for large requests expected to fail sometimes.
- **C# error handling** (ADR 0030): exceptions for unexpected failures, the
  Try pattern for expected ones, and no throwing from per-frame APIs.
- **C#:** standard .NET conventions: root namespace `Crucible`; `PascalCase`
  types, methods, properties, and constants; `camelCase` locals and
  parameters; `_camelCase` private fields; file-scoped namespaces.
  `.editorconfig` is authoritative and `dotnet format` checks it.
- **Shared C# settings** (`Directory.Build.props`): .NET 10 (LTS; the exact
  SDK, 10.0.403, is pinned in `global.json`, ADR 0031), nullable reference
  types, warnings as errors, `IsAotCompatible` (AOT and trim analyzers as
  errors, ADR 0001) for every project including tests. Packages are pinned
  by lock file; the CI workflow restores in locked mode.
- Comments explain *why*, not *what*.

## 6. Build & tooling

- CMake 3.25+ with presets: `debug` (Slag validation on) and `release`,
  Ninja, output in `build/<preset>/`. Version `0.1.0` (semantic
  versioning, ADR 0009) generates `crucible/Version.h`.
- C# builds through CMake: the `crucible_managed` target runs `dotnet build
  Crucible.slnx`, with output under `build/<preset>/artifacts/`. C# test
  projects are executables registered with CTest.
- Tests: doctest (C++) and TUnit (C#), ADR 0029. CTest labels: `native`,
  `managed`, `integration` (the launchers, which create a device), `aot`,
  and `gpu` (needs a device Slag supports; `ctest -LE gpu` skips them). The
  doctest suite `window` is skipped unless run by hand on a desktop:
  `crucible_native_tests --no-skip --test-suite=window`. Test names may not contain
  `,` or `;` (a `;` makes CTest run nothing and report a pass); the
  `tests.names` test enforces it. Every C# suite also runs
  as NativeAOT code (`<suite>.aot`).
- **x64 level** (ADR 0024): x86-64-v3 (AVX2) by default, for everything
  including dependencies and the NativeAOT engine; `CRUCIBLE_X86_LEVEL=v2`
  builds for SSE4.2. `CRUCIBLE_FORCE_SCALAR_SIMD` tests the SIMD types'
  scalar paths. The launcher refuses a CPU without the level, with a
  message.
- `CRUCIBLE_BUILD_AOT` (on by default) builds the NativeAOT variants:
  `crucible_launcher_aot` (core and C# engine statically linked into one
  executable) and the AOT test suites. AOT publishes take about 30 seconds
  the first time.
- **Build requirements:** CMake, Ninja, a C++20 compiler (MSVC on Windows),
  the exact .NET SDK in `global.json`, and Clang with libclang (the binding
  generator loads the system's libclang, ADR 0017; NativeAOT links with clang
  on Linux; on Windows LLVM's default folder is searched).
- **Windows** (ADR 0031): the static C runtime (`/MT`) everywhere, Slag
  included; the launcher uses `nethost.dll` and `wmain`. Build from a Visual
  Studio developer environment: `scripts\msvc.cmd <command>` runs a command
  inside one, finding Visual Studio with vswhere (also over SSH).
- **Bindings** (ADR 0017): `tools/Crucible.BindGen` runs before the core and
  engine build, on every public header in `src/native/core/include/`.
  Output goes to `build/<preset>/generated/`; IDE builds of the C# projects
  read `build/debug/generated/`, so configure and build the debug preset
  first.
- **Source generators** (ADR 0036, 0038): `tools/Crucible.Generators`
  (netstandard2.0) writes `[System]` registration into each game assembly,
  and JSON readers and writers into `[Serialized]` types.
  Projects reference it with `OutputItemType="Analyzer"
  ReferenceOutputAssembly="false"`; its tests are `Crucible.Generators.Tests`
  (JIT only). See `docs/learning/source-generators.md` and
  `docs/learning/generated-serializers.md`.
- **Asset pipeline** (ADR 0040): `src/managed/Crucible.Assets`, editor-side
  and never shipped: projects (`<Name>.crproject`, `Assets/`, `Code/`,
  `Plugins/`, `Cache/`), `.meta` sidecars, the asset database, importers,
  and the import cache. Tests: `Crucible.Assets.Tests` (JIT only).
- **Registration and scenes** (ADR 0041): `[Component]`, `[Service]`, and
  `[System]` types are registered by generated code per assembly;
  `[Component, Serialized]` types are saved in scenes (`Crucible.Scenes`,
  `.scene` JSON with the GUID inside).
- **Samples** (ADR 0037): `samples/Particles` is the M3 demonstration, a game
  the JIT launcher loads with `--game <dll>` and configures with
  `--game-arg key=value`; it prints per-step timings.
- Slag comes from a pinned tag; to work on Slag side by side, configure with
  `-DFETCHCONTENT_SOURCE_DIR_SLAG=~/Claude/Slag`.
- **Verification by hand** (no hosted CI for now: Actions minutes are spent
  elsewhere). Before a milestone is marked done, the full matrix runs on the
  owner's machines: Linux (GCC and Clang, debug and release, clang-format and
  `dotnet format` checks) and the Windows machine over SSH (MSVC, debug and
  release). `.github/workflows/ci.yml` is the reference for that matrix and
  runs only when started by hand (`workflow_dispatch`), so a push never
  spends minutes.

```bash
cmake --preset debug && cmake --build --preset debug
```

```bash
ctest --preset debug
```

## 7. Working with Claude on this repo

This project is as much a learning exercise as it is an engine.

- **Ask before every architectural decision.** Present the options, how
  existing engines handle the problem, the trade-offs, and a
  recommendation, then wait for the project owner's decision.
- **Ask before adding any third-party library**, stating its license.
- **Explain before acting.** Say what is about to be done and why, and ask
  for feedback, before doing it.
- **Explanations are kept twice:** walked through in chat, and written into
  the ADR so the reasoning can be reread later. Concepts that need a longer
  explanation get a learning note in `docs/learning/`, listed in
  `README.md`.
- **Ask before naming.** Proposed names are placeholders until the project
  owner settles them; final names go in `docs/naming.md`.
- Record decisions in `docs/decisions/` and update this file when a
  decision changes a rule here. Unresolved questions live in
  `docs/open-items.md`.

### ADR format

`docs/decisions/NNNN-short-title.md`:

```
# NNNN — Title

**Status:** Proposed | Accepted (YYYY-MM-DD) | Superseded by NNNN

## Context        what problem this solves, which goals it serves
## Options        each option, with how existing engines do it
## Decision       what was chosen
## Why            the reasoning, including what was given up
```
