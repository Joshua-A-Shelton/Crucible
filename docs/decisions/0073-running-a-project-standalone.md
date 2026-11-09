# 0073 — Running a project standalone

**Status:** Accepted (2026-11-09); part a of M7 step 7 built. Names settled
by the owner as drafted, except `AssetCache` (chosen while building, see
below); those added while building are placeholders (docs/naming.md).

## Context

ADR 0016 promised a "Run standalone" action beside Play in the editor: the
game as its own process in its own window, for code that might crash and
for testing as shipped. The launcher could already load a game assembly
(`--game`) and pass it options, but nothing outside the editor opened a
project: the Diorama sample brought its own setup code, and a project's
built game (Project/Code) has none, so it would start in an empty world.

The owner split M7 step 7 in three: **7a**, Run standalone (this ADR);
**7b**, the C++ runtime extension mechanism; **7c**, interfaces for the
replaceable subsystems. 7b and 7c are designed at their start.

## Options

**What runs:** the project in place (the regular JIT launcher, the
project's built game, and the files in its Cache); or an exported folder
(engine, game, and imported assets copied out first).

**Which scene:** the open scene as it is, unsaved edits included; a start
scene named in the project file; or the open scene's file, saved first.

**Who opens the project in the game's process:** the engine, from the
game arguments; or each project's own setup code.

**The process:** tracked by the editor (output in the Log, Run becomes
Stop, closed with the editor); or started and left alone.

## Decision

Decided by the owner (2026-11-09):

1. **The project in place.** Run starts the game launcher with the
   project's last good build (Cache/Build) and its imported files.
   Packaging waits for cooking (ADR 0021).
2. **The open scene, as it is.** The editor writes it to
   `Cache/Run/<Scene>.crscene` and runs that; while playing, the scene as
   it was at Play.
3. **The engine opens the project.** Given the game arguments `project=`
   and `scene=` (the launcher's `--project` and `--scene`, now in the game
   launcher too), the host opens the project's imported files read-only,
   points the renderer at them, and loads the scene into the main world
   before the game's systems start. A project needs no setup code.
4. **Tracked.** One run at a time; its output goes to the Log as
   "[game] …"; the command reads "Stop Standalone" while it runs; closing
   the editor stops it.

## Why

Running in place starts in a moment from the files the editor already
has, and tests the game's code and scene in the shipping host. The open
scene as it is matches Play: what you see is what runs. Opening projects
in the engine is ADR 0006's good defaults: a new project runs with no
configuration. Tracking the process puts the game's errors where the
editor's are.

## Details

- **`AssetCache` is in the engine, not `AssetDatabase.OpenImported`** as
  first drafted: the asset database is in Crucible.Assets, which references
  the engine and carries the importers and their native libraries, and a
  game references the engine alone. `AssetCache` reads `Cache/imports.json`
  (each asset's import key) and finds `Cache/Imported/<kk>/<key>.*`, with
  no importers; prefab files are found in Assets/ by their sidecars.
  Imports are trusted as they are: the editor imports what changed before
  it runs. The owner chose the name.
- **The editor launcher sets `LaunchOptions.editor`,** so the host leaves
  `project=` and `scene=` to the editor, which opens projects itself.
- **The launcher** is `crucible_launcher` beside the editor's executable;
  the engine's assemblies are the editor's (`--managed-dir`). A project
  without code runs its scene with the engine's systems alone.
- **Menus read `Command.CurrentTitle`** as they open, so one command reads
  Run or Stop.

## Not yet

- Packaging a game (cooking, ADR 0021), and running it under NativeAOT.
- Choosing the backend, window size, or other launcher options from the
  editor.
- A start scene for shipped games.

## As built

- **Native:** `--project` and `--scene` in both launchers;
  `LaunchOptions.editor`.
- **Engine:** `AssetCache` (`Open`, `Read`, `FileOf`, `Prefabs`, `Count`,
  `Root`, `CacheFolder`); `ProjectStartup.Open(world, arguments)`, called
  by the host after the game's components are registered, logging
  "opened <scene>: N entities at the top" and any components nothing
  registered; `Command.CurrentTitle`.
- **Editor:** `StandaloneRun` (`Arguments`, `LauncherPath`, `Start`,
  `TakeLines`, `TakeExit`, `Stop`, `IsRunning`); the shell's
  `RunStandalone`, `StopStandalone`, `Standalone`; the command
  `Play.RunStandalone` ("Run Standalone", Ctrl+Shift+P, in the Edit menu).
  Before running, the editor imports what changed.
- **Tests:**
  - **`ProjectStartupTests`:** the cache reads imported files and prefab
    files, skips records whose file is gone; the scene loads from the
    game arguments, and nothing happens without them.
  - **`StandaloneRunTests`:** the launcher's arguments; a process's
    output and exit code collected; Stop ends it.
  - **`launcher.jit.standalone`:** on a copy of the Diorama's project,
    the village with an unsaved entity added runs headless for 30 frames
    in the game launcher, loading Diorama.Game.dll and 26 entities with
    every component known; the command reads Stop while it runs; the
    output and the clean end reach the Log; a second run is stopped.
