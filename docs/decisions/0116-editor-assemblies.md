# 0116 — Editor assemblies

**Status:** Accepted (2026-11-23); step 1 of editor extensions. Names
marked `*` in docs/naming.md are placeholders until the owner has tried it.

## Context

ADR 0002 decided that editor code lives in editor assemblies, kept out of
shipping builds, with an editor symbol for small touches in game code and
a check that the game builds without it. None of it was built: every `.cs`
file under a project's `Code/` compiled into the one game assembly,
which referenced only the engine. Editor extensions (milestones) start
here: a project needs somewhere to put editor code before there's an API
for it to use.

## Options

**Where editor code lives:** `Code/Editor/` only; any folder named
`Editor` under `Code/`; an `Editor/` folder beside `Code/`. **What
reloads:** the game and editor assemblies together in one load context,
or the editor's in a context of its own chained to the game's. **A
project's open panels on reload:** made again in place, or left as
placeholders until the build is done. **The symbol check:** in the
background when game code uses the symbol, with every build blocking, or
only when packaging.

## Decision

Decided by the owner (2026-11-23):

1. **Any `Editor/` folder.** Every `.cs` file under a folder named
   `Editor`, at any depth in `Code/` (`Code/Editor/`,
   `Code/Characters/Editor/`), is editor code, so a game's editor code can
   sit beside the game code it's for. The game's project excludes them.
2. **`<Game>.Editor.dll`*.** The editor writes a second project,
   `Cache/Build/Editor/<Game>.Editor.csproj`*, in a folder of its own (two
   projects in one folder would share `obj/`). It compiles the Editor
   folders' code with `CRUCIBLE_EDITOR`, references the game's project
   and the editor's assemblies (`Crucible.Editor`, `Crucible.Assets`, the
   engine), and builds into the game's output folder. One `dotnet build`
   of it builds both. The build stamp (ADR 0110) covers both projects
   and both assemblies, and the editor's own assembly is an input.
3. **Reloaded together.** The editor assembly is loaded into the game's
   collectible context (`GameLoader.Load`, `Reload`; `LoadedGame.EditorAssembly`*),
   so the pair loads, reloads, and unloads as one, and an editor-only
   change also migrates the game's components (ADR 0035). "Loaded
   Walker.dll and Walker.Editor.dll"*.
4. **Shared, not copied.** The game's context resolves the editor
   assembly's references to the assemblies already loaded: the game's own,
   and the editor's (`GameLoader.SharedAssemblies`*, set by the editor), so
   editor code and the editor use the same types. Found by name: with a
   resolving handler that captured the game's `Assembly`, the old context
   was never collected after a reload.
5. **`CRUCIBLE_EDITOR`*.** Defined in the editor's build of the game's
   code and in the editor assembly; never in a package's. Packages
   (JIT and NativeAOT) build only the game's project, so editor code
   can't reach them.
6. **The symbol check, in the background, when used.** When the game's
   code (not its Editor folders) mentions `CRUCIBLE_EDITOR`, each load is
   followed by a build without it (`Cache/Build/SymbolCheck/`,
   `GameBuilder.StartSymbolCheck`*), and its errors are logged as warnings:
   "Walker doesn't build without CRUCIBLE_EDITOR: Code/UsesEditor.cs(5,…)"*.
7. **Panels reopened in place** after a reload, their state lost unless
   they keep it. A project can't make panels until the public editor API
   (step 2), so this is built there, with the hook for keeping state.

## Consequences

- Editor code can use anything public in the editor. Little is public
  yet (the shell, panels, undo, and selection are internal): step 2
  decides what is.
- Projects with no Editor folder build exactly as before.
- A build of a project with editor code takes a little longer: two
  assemblies, and with the symbol, a third build in the background.
- `EditorCodeChecks`* (CTest `launcher.jit.editor-code`*) builds, loads,
  and reloads the pair on a copy of the Walker, checks the old pair is
  collected, the symbol check, and that a package has none of it;
  `GameBuildTests` checks the projects.
