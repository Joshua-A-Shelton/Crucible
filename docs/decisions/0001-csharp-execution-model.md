# 0001 — C# execution model

**Status:** Accepted (2026-10-24).

## Context

Goal 2 asks for C# scripting that can "conditionally compile to native".
Goal 1 already fixed the interop style: modern .NET source-generated
`LibraryImport`, not Mono. That commits Crucible to modern .NET, which
offers two ways to run C#:

- **CoreCLR (JIT):** code is compiled to machine code as it runs.
  Assemblies can be loaded at runtime, and unloaded through a collectible
  `AssemblyLoadContext`, which is what makes hot reload and mods possible.
  Costs: JIT warm-up stalls the first time code runs, slower startup, and
  the runtime ships with the game.
- **NativeAOT:** the whole program is compiled ahead of time. No JIT
  stalls, fast startup, can link into a native binary, and `LibraryImport`
  calls can bind directly to a static C++ library. Costs: no loading new
  assemblies at runtime, and reflection is limited by trimming (runtime
  type discovery has to be replaced by source generators).

## Options

- **(a) JIT in the editor, AOT for shipping.** Iterate with hot reload,
  ship native. Risk: code that works under JIT but breaks under AOT.
- **(b) A Burst-like compiler** for a restricted, high-performance C#
  subset (Unity's Burst), layered on top of (a). A compiler project of its
  own.
- **(c) IL→C++ translation** (Unity's IL2CPP). Unity built it because
  Mono's AOT wasn't good enough; NativeAOT now fills that role.

## Decision

Option (a), with one change: **shipping AOT is a per-game choice, not a
requirement.**

- The editor always runs C# on CoreCLR, with hot reload by unloading the
  game assemblies, recompiling, reloading, and restoring serialized state.
- A shipped game is either:
  - **JIT:** CoreCLR ships with the game, which can load C# mod
    assemblies at runtime that plug into and change the running game.
  - **AOT:** NativeAOT, fully native, for games that don't need mods.
- The same game code must work under both. .NET's AOT and trim analyzers
  are **errors** for engine, plugin, and game assemblies, in every build
  configuration including the editor. Mods are exempt: they only run in
  JIT games.
- JIT-shipped games are precompiled with **ReadyToRun** by default; a game
  can opt out.
- Mods are trusted code: Crucible doesn't sandbox them (CLAUDE.md §2).
- (b) is not part of v1; it stays possible later. (c) is ruled out.

## Why

The per-game choice keeps highly moddable games possible (RimWorld- or
Cities: Skylines-style C# mods) without making every game pay for a
shipped runtime and JIT warm-up. Hot reload by full reload handles any
code change, at the cost of a short pause, where .NET's in-place Hot
Reload refuses many kinds of edits.

Analyzers as errors everywhere catch "works in the editor, breaks in the
AOT build" at the moment the code is written, not when a game first ships
AOT. Game code gets no opt-out, so any game can switch to AOT later; revisit
if a JIT-only game ever needs unrestricted reflection.

ReadyToRun ships precompiled native code inside the assemblies while
keeping the JIT for mods and for code it couldn't precompile, which removes
most JIT warm-up stalls (goal 4, low latency). It's part of .NET, not a
third-party dependency. The opt-out exists for games that prefer smaller
assemblies or pure JIT.

.NET removed its sandbox (Code Access Security), so a mod can do anything
the game process can. Like most moddable games, Crucible treats installing
a mod like installing software.

Given up: AOT-only simplicity. Because JIT is a shipping mode, the engine
has to keep both paths working and tested.

## Consequences (to be designed later)

- Hot reload means nothing may keep references into unloaded game
  assemblies: native code can't cache managed function pointers, delegates,
  or GC handles across a reload. This constrains the interop design.
- Code that may ship AOT can't rely on runtime reflection, which shapes
  the reflection system the editor uses (goal 7).
