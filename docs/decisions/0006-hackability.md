# 0006 — Hackability

**Status:** Accepted (2026-10-24).

## Context

Goal 7: games can reshape the engine to their needs, with good defaults,
and the editor picks up on those changes. Earlier decisions already lean
on this goal: AOT-compatible reflection (0001), attributes for exposing
data (0002), a game loop built from changeable phases (0003), and "speed
in the core, hackability at the edges" (goal 1).

"Hackable" can mean four levels:

- **(a) Configure:** change settings, swap defaults.
- **(b) Extend:** add components, systems, loop phases, render passes,
  asset importers, editor tools, in C#.
- **(c) Replace:** swap a whole subsystem (physics, render pipeline,
  audio, ...).
- **(d) Modify the source:** change the engine's C++ core (Unreal's model).

How the editor can learn about changes:

- **C#:** source generators read the code at compile time and generate
  metadata. Works under AOT.
- **C++:** a header parser/code generator (Unreal's UHT; usually built on
  libclang, Apache 2.0 with LLVM exception), C++26 static reflection
  (compiler support still arriving; Slag is C++20), or exposing C++ only
  through C#.

## Decision

- **All four levels are in scope.** For (d), the engine is buildable from
  source by a game team; there is no promise that C++ changes merge
  easily with engine updates.
- **The editor sees what's exposed to C#.** Anything in C# is picked up
  automatically through source-generated metadata. A C++ change shows up
  in the editor once it's exposed to C#, and exposing it should be cheap:
  ideally one generator produces both the `LibraryImport` bindings and the
  editor metadata. No direct C++ reflection.
- **Subsystems are replaceable at coarse boundaries:** renderer pipeline,
  physics, audio, input, asset pipeline. A boundary is crossed once per
  frame or per batch, never per object.
- **Good defaults:** a new project runs with no configuration, and every
  default can be overridden at the level it lives at.
- **No art-style lock-in** (added 2026-10-25, ADR 0010): Crucible's own
  games are stylized, but a game can use any art style.

## Why

One reflection path (C#) instead of two keeps the editor simple and avoids
a C++ parser dependency, and it matches ADR 0002 (C++ never extends the
editor). Coarse boundaries keep replaceability out of the hot paths, so
hackability doesn't cost speed in the core.

Given up: C++ types appearing in the editor without being exposed to C#,
and fine-grained per-object replacement.

## Consequences (to be designed later)

- The C#↔C++ binding generator: what it reads (C++ headers, which may
  need a parser, or a separate description that generates both sides) and
  what it emits.
- The frame-phase mechanism for the game loop (0003).
- The interface for each replaceable subsystem.
