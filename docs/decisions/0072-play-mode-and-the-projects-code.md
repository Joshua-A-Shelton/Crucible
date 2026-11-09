# 0072 — Play mode and a project's code

**Status:** Accepted (2026-11-09); part a built (GPU assets reloaded in
place, part b, follows). Names settled by the owner as drafted; those added
while building are placeholders (docs/naming.md).

## Context

M7 step 6: play in the editor, and edit C# while playing. It decides how
play mode treats the world and how the editor builds games. The engine
already loads a game into a collectible load context and hot-reloads it,
moving every world's component values to the new types (`GameLoader`, ADR
0001, 0035). But projects had no code of their own (the Diorama's systems
were a CMake-built sample), and the editor ran only its own assembly. The
owner split the step: **6a**, a project's code built, played, and
hot-reloaded; **6b**, assets reloaded on the GPU.

## Options

**A project's code:** .cs files in Project/Code with a project file the
editor writes and `dotnet build`; a .csproj the user owns; or Roslyn in the
editor's process.

**The world while playing:** a snapshot saved at Play and loaded back at
Stop, in the same world; or a second world.

**Where the game shows:** a Game panel through the world's camera, beside
the Viewport; or the Viewport switching to the game's camera.

**When code reloads:** on save; or from a command.

## Decision

1. **A generated project.** You write .cs files under Project/Code. The
   editor writes `Cache/Build/<Project>.Game.csproj` (rewritten only when it
   changes, so builds stay incremental): every .cs under Code, the engine
   referenced (not copied), the engine's source generators, the engine's
   AOT-safe settings. `GameBuilder` runs `dotnet build` on it.
2. **Snapshot and restore.** Play saves the scene to memory, scene IDs and
   all, and runs the game's systems on the same world; Stop destroys what
   was made since and loads the snapshot back. Selection and undo go by
   scene ID, so they survive; the undo steps recorded while playing are
   dropped with the edits.
3. **A Game panel** (`GamePanel`) draws the world through its own camera,
   with its post-processing; the Viewport keeps the editor camera.
4. **Reload on save,** playing or not.
5. **The Diorama's gameplay moves into its project:** a `Spin` component
   and `SpinSystem` in `samples/Diorama/Project/Code`, which the sample's
   own .csproj compiles too. Only gameplay moves: the sample's harness
   (importing, building the scene, capturing, measuring) stays a sample,
   and gives the heroes their `Spin`. The village's hero has one.

## Details

- **Opening a project** builds its code at once and loads it, before any
  scene: a scene's game components must be known when it loads (unknown
  ones are skipped, and would be lost on save). Then the Code folder is
  watched (`AssetWatcher`), and a settled change starts a build in the
  background.
- **The build** runs with the dotnet the editor runs on (else
  `DOTNET_ROOT`, else PATH), from the temp folder: a repository the project
  sits in may have a global.json asking for an SDK that dotnet hasn't (as
  this one does). The repository's Directory.Build.props and the like are
  turned off on the command line. Errors are read from dotnet's output as
  file, line, and message, and logged; the game keeps its last good build.
- **Loading:** the first build with `GameLoader.Load`, later ones with
  `GameLoader.Reload`, which migrates component values (its notes are
  logged); the old version is unloaded and checked through a weak reference
  (ADR 0011), and a version that won't unload is logged.
- **Running the game's systems:** the game's own schedule is built at Play
  (its systems, in the default phases, without the engine's), and one
  editor system per phase (`PlayBeginFrame` … `PlayEndFrame`) runs that
  phase's game systems. A reload while playing stops the old systems and
  starts the new ones. Exceptions are logged and play goes on, as in the
  frame loop (ADR 0036).
- **Input:** the game sees the frame's events only while the Game panel
  has the input: a click on it, or Play, gives it; Escape or focus moving
  elsewhere takes it back. Pointer events are moved into the panel's
  pixels; the editor's shortcuts don't fire while the game has the keys.
- **Play** (`Play.Toggle`, Ctrl+P, in the Edit menu) shows the Game panel
  and gives it the input; the title reads "… — Playing".
- The shell's old `Stop()` (shutting the editor down) is now `Shutdown()`.

## Why

A generated project keeps building in step with the engine with nothing
for the user to maintain; `dotnet build` is incremental, out of process,
and the SDK is needed to develop with Crucible anyway. Snapshot and
restore reuses the scene round trip that's already exact, and needs no
change to a renderer and frame loop built for one world. A Game panel
keeps the editor camera while playing.

## Not yet

- Assets reloaded on the GPU (6b).
- Pause and step; play in a separate window; several game views.
- The first build blocks while the project opens.
- NuGet packages in a project's code.

## As built

- **Editor:** `GameBuilder` (`AssemblyName`, `ProjectFile`, `OutputPath`,
  `HasCode`, `IsBuilding`, `Start`, `TakeFinished`, `BuildNow`,
  `ProjectText`), `BuildError`, `GameBuild`; the shell's `Play`, `Stop`,
  `IsPlaying`, `PlayStateChanged`, `Game`, `Builder`, `BuildCode`,
  `Shutdown`; `GamePanel`, `GameView` (`HasInput`, `TakeInput`);
  `PlayPhaseSystem` and one per phase; `UndoHistory`'s internal `Save` and
  `Restore`; the command `Play.Toggle`.
- **Sample:** `Diorama.Spin` and `Diorama.SpinSystem` in
  `samples/Diorama/Project/Code`; the village's hero has a `Spin`.
- **Tests:**
  - **`GameBuildTests`:** the generated project, a real build of a tiny
    game, a build error's file and line; errors read from dotnet's output;
  - **`PlayModeTests`:** the history at Play comes back at Stop;
  - **`launcher.jit.play`:** on a copy of the Diorama's project, its code
    built and its `Spin` known before the village opens; Play turning the
    hero and showing the Game panel with the input; Escape; Stop undoing
    an entity made and a move made while playing, and their undo steps; a
    file added to Code built and hot-reloaded with the hero keeping its
    `Spin`; a build error logged with its file and line while the last
    build stays loaded; saved as `play.png` while playing.
