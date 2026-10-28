# 0011 — Hosting and the C#↔C++ boundary

**Status:** Accepted (2026-10-25). Amended 2026-10-26 (M2): launch
options.

## Context

Every Crucible game is part C++ (the core, on Slag) and part C# (engine
layer, plugins, game). One side starts first and hosts the other. ADR 0001
fixed the runtimes (CoreCLR in the editor, JIT or NativeAOT when shipping),
ADR 0002 made the game the root plugin, and ADR 0003 made the frame a set of
phases a game can change.

Crossing the boundary is cheap both ways: C#→C++ through `LibraryImport`
with blittable arguments (plus `[SuppressGCTransition]` for tiny
non-blocking calls), C++→C# through unmanaged function pointers to
`[UnmanagedCallersOnly]` methods. The difference that matters is hot
reload: native code holding a pointer into an unloaded assembly is left
pointing at freed code, and a `GCHandle` into one keeps it from unloading.

## Options

**Who owns `main()`:**

- **(A) Native host.** A C++ executable initializes the engine, then starts
  .NET: through `hostfxr`/`nethost` (.NET's own hosting API, MIT) for JIT,
  or by linking the NativeAOT output. Unity, Godot's C# module, Flax.
- **(B) Managed host.** The game is a .NET program and the C++ core a
  native library: .NET's apphost for JIT, a NativeAOT executable that
  statically links the core for AOT. Stride, MonoGame, FNA.

**Where the frame loop driver lives:**

- **In C++,** calling C# phases through function pointers that must be
  refreshed on every hot reload.
- **In C#,** calling coarse native subsystems (cull, submit the frame, step
  physics).

## Decision

1. **Native host (A).** C++ owns `main()`, platform init (window, crash
   handling, Slag device), and shutdown. The executable is a thin C++
   launcher around the C++ core, which is built as a library.
2. **The frame loop driver is in C#.** The launcher starts .NET and calls
   **one managed entry point in the engine's C# layer**, which runs the
   frame loop until quit and then returns. Heavy work stays in coarse
   native subsystems.
3. **Plugins register through source-generated entry points,** the same in
   every mode. Native code only knows the engine's entry point, never game
   code:

   | Mode | Native host reaches C# | Plugins are found |
   |---|---|---|
   | Editor, shipped JIT | `hostfxr` loads the engine's C# assembly and returns its `[UnmanagedCallersOnly]` entry | The engine's C# layer loads `<GameName>.dll` and its plugins and calls each generated registration (editor: in a collectible `AssemblyLoadContext`) |
   | Shipped AOT | Calls the entry exported by the linked NativeAOT output | Compiled in; a generated bootstrap calls each registration directly |

   In JIT and editor modes the loader finds a plugin's registration through
   one assembly-level attribute; nothing else uses reflection.
4. **Linking:** in JIT and editor builds the C++ core is a shared library
   that `LibraryImport` binds to by name. In AOT builds the launcher, the
   core, and the NativeAOT output are statically linked into one
   `<GameName>.exe`, with direct P/Invoke.
5. **Hot-reload rule: native code holds references only into the engine's
   own C# layer, which is never unloaded.**
   - Callbacks from native subsystems (physics contacts, asset events, log
     hooks) go to engine-side C# dispatchers, which call game code.
   - Native code identifies game objects by IDs, never by `GCHandle`s.
   - The editor checks, through a weak reference, that each unloaded
     `AssemblyLoadContext` is really gone, and reports what is holding it
     if not.

## Why

The native host puts crash diagnostics in place before the runtime starts,
controls startup order, and keeps platform setup in C++. The C# loop driver
makes changing the loop (ADR 0003) ordinary C#, while the hot work stays
native: orchestration is a few dozen calls per frame, small next to a frame
budget.

One managed entry point plus the hot-reload rule make hot reload safe by
construction: only the engine's C# layer has to follow the rule, and game
code never touches the native side directly. Silent unload failures are
the most common hot-reload bug in .NET engines, hence the editor check.

Building the core as a library lets `LibraryImport` bind to it normally in
JIT builds; calling into symbols exported from the executable itself would
also work but is fiddlier on both platforms.

Given up: (B)'s standard `dotnet run` style tooling for launching games,
and the option of C++ calling game code directly.

## Amendment (2026-10-26): launch options

Settled with the project owner in M2 step 4.

- **The launcher is the only command-line parser.** It passes a
  `LaunchOptions` struct (a placeholder name) by value to the managed entry
  point, `crucible_engine_run(LaunchOptions)`. The struct is exposed through
  the binding generator (ADR 0017), so its C# mirror and layout check are
  generated. Neither hosting path hands .NET the command line, so C#'s
  `Environment.GetCommandLineArgs` can't be relied on.
- **Launcher options:** `--managed-dir <dir>` (JIT), `--backend d3d12|vulkan`
  (ADR 0015), `--workers N` (ADR 0014), `--frames N` (stop after N frames), and `--headless` (no
  window; frames render into an offscreen surface, M2 step 6). The automated launcher tests run
  headless, since the offscreen video driver can't present; real windows are
  checked by hand.
- **Startup order:** platform, then renderer, then the engine; shutdown in
  reverse. The engine creates the main window and its surface, and destroys
  them before returning.

## Amendment (2026-10-27): loading a game (M3 step 9)

- `--game <file>` loads a game assembly (JIT engines only; NativeAOT
  refuses it), and `--game-arg key=value` passes it options, repeatable.
  The launcher stores both in the core; the host reads them back. See ADR
  0037.
