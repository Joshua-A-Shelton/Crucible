# CLAUDE.md — Crucible

Crucible is a game engine built on Slag
(https://github.com/Joshua-A-Shelton/Slag), Josh's thin abstraction over
Direct3D 12 and Vulkan. The primary focus is 3D; excellent 2D support is a
secondary focus.

Licensed under zlib (`LICENSE`, ADR 0007); dependencies must stay
compatible.

This file is the source of truth for project goals, constraints, and
conventions. If a request conflicts with it, point out the conflict before
writing code. **Status:** charter complete (2026-10-25); architecture
decisions in progress (`docs/open-items.md`).

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
- **Frame phases** (ADR 0013): the frame is an editable, ordered list of
  phases; systems register into a phase with before/after constraints,
  sorted when the schedule is built. The fixed tick is a phase group with
  a step cap. Structural changes apply at the end of each phase.
  Parallelism in v1 is inside systems. The loop driver is replaceable.
  The pacing wait comes first, then input.

## 5. Working with Claude on this repo

This project is as much a learning exercise as it is an engine.

- **Ask before every architectural decision.** Present the options, how
  existing engines handle the problem, the trade-offs, and a
  recommendation, then wait for the project owner's decision.
- **Ask before adding any third-party library**, stating its license.
- **Explain before acting.** Say what is about to be done and why, and ask
  for feedback, before doing it.
- **Explanations are kept twice:** walked through in chat, and written into
  the ADR so the reasoning can be reread later.
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
