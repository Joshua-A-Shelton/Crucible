# 0118 — Plugins

**Status:** Accepted (2026-11-23); step 3 of editor extensions, placed by
the owner before workspaces. Names marked `*` in docs/naming.md are
placeholders until the owner has tried it.

## Context

ADR 0002 made the game the root plugin, with every other plugin structured
the same way (runtime code and editor code, the editor code kept out of
shipping builds), but source-only in v1 and prebuilt plugins revisited
later. ADRs 0116 and 0117 built that for the root plugin alone: a game's
`Editor` folders compiled into `<Game>.Editor.dll`, loaded with the game,
and the public editor API it uses. Nothing let a game use code from
anywhere else, a dialogue system, a studio's tools. The owner wants
plugins compiled by and large, built and shared as assemblies, and asked
for it before 1.0, while the editor API still changes (ADR 0117 point 3),
so the version question has to be answered now.

## Options

**When:** after the other steps, now (before workspaces), or a milestone
of its own. **Other versions:** a version check refusing a plugin built
for an incompatible Crucible; freezing the editor API. **The game's own
code:** stays source; may be compiled too. **How a compiled plugin is
written:** a plain .NET project built elsewhere; a plugin project the
editor opens; a plugin folder inside a game, in source, built when shared.
**Where:** `Plugins/<Name>/`; `Code/Plugins/<Name>/`. **What it carries:**
code only; code and assets; code, assets, and native modules. **Plugins
using plugins:** declared; not yet. **A compiled plugin and reloads:**
loaded once; reloaded with the game. **Its description:** a JSON
manifest; assembly attributes. **Turned on by:** being there; a list in
the project file. **Compatible versions:** the same minor before 1.0;
the exact version. **Built by:** a Build Plugin command; the command line
only; both.

## Decision

Decided by the owner (2026-11-23):

1. **Now, step 3.** Built before workspaces, so every later step is built
   with plugins in mind; the steps after are renumbered (milestones).
2. **Source or compiled.** A plugin is a folder `Plugins/<Name>/`* in a
   game's project holding `<Name>.crplugin`* and either source (`Code/`,
   its `Editor/` folders the editor's as in a game, ADR 0116) or a
   compiled pair, `<Name>.dll`* and `<Name>.Editor.dll`*, and `Assets/`.
   A source plugin builds and hot-reloads with the game; it's how plugins
   are written, with immediate feedback. The game's own code stays
   source.
3. **A JSON manifest**: `"Name"`*, `"Version"`* (the plugin's own, shown,
   not checked), `"Uses"`* (other plugins, by name), and `"Crucible"`*,
   the Crucible version a compiled plugin was built with (empty for
   source). The folder is a plugin because its manifest is there; its
   name is its folder's.
4. **Turned on by being there.** Every plugin in `Plugins/` is used.
5. **Declared uses.** A plugin loads after the plugins it uses (then by
   name) and its code references theirs. A plugin using one that's missing
   or can't be used, plugins using each other, and a compiled plugin using
   a source one (which reloads under it) are left out, with why in the Log:
   "Quests uses Inventory, which isn't in Plugins/"*, "Dialogue and Quests
   use each other"*, "Quests is compiled and uses Dialogue, which is
   source: build Dialogue too"*.
6. **A version check, the same minor before 1.0.** A compiled plugin
   loads when its `"Crucible"` has the same major and minor as this
   Crucible's before 1.0, the same major from 1.0 (semantic versioning's
   rule): "Old was built for Crucible 0.0; this is 0.1. Rebuild it for
   0.1"*. The editor API stays free to change until 1.0 (ADR 0117).
   `CrucibleVersion`* (`Crucible.Engine`) is generated from the CMake
   project version.
7. **Code and assets; native modules later.** A plugin's assets are named
   by its name and a colon, `Dialogue:Icons/Talk.png`* (a colon can't be
   in a Windows file name, so they never clash with a game's), keep their
   IDs in their sidecars wherever the folder goes, show in the Assets
   panel as a folder of their own beside Assets, and are packed like the
   game's. A compiled plugin's assets are as it was built with: "Waypoints
   is a compiled plugin; its assets can't be changed here"*. Native
   modules (ADR 0074) in plugins come later.
8. **A compiled plugin loads once** per project, in a load context of its
   own shared with the game's, so the game's reloads leave it be; a
   changed one takes opening the project again ("The compiled plugins
   changed: open the project again to load them"*). Its extensions start
   when the project opens and stop when it closes.
9. **File > Build Plugin…*** builds a source plugin (choosing which, if
   there are several) into a chosen folder's `<Name>/`: its runtime
   assembly in Release without `CRUCIBLE_EDITOR`, its editor assembly,
   its assets with their sidecars, and its manifest stamped with this
   Crucible's version. A previous build there is replaced; anything else
   of that name, or the plugin's own folder, is refused.
10. **`EditorPlugin` renamed `EditorExtension`** (ADR 0117's entry point):
    "plugin" now names the package, which has extensions.

## How it works

- **Building.** Each source plugin with code gets a project in every
  variant of the game's build (the editor's, the symbol check's, a
  package's, NativeAOT's), and an editor project for its `Editor`
  folders in the editor's; compiled plugins are referenced as assemblies.
  The game's editor project references them all, so one `dotnet build`
  builds everything; the build stamp (ADR 0110) covers their manifests,
  sources, and assemblies.
- **Finding plugins at runtime.** The editor writes the game's plugin
  list, in load order, into its build as an assembly attribute
  (`GamePlugins`*, `Plugins.g.cs`*). The game loader resolves each name
  and registers that plugin's systems, components, and services before
  the game's: from the editor's contexts, from the package's assemblies
  (JIT), or compiled in (NativeAOT, where each plugin is a trimming root).
  It doesn't depend on the game's code mentioning the plugin, since a
  reference nothing uses is dropped from the compiled assembly.
- **Loading in the editor.** Source plugins load into the game's
  collectible context before it, their editor assemblies after it, and
  reload and migrate with it (ADR 0035). Compiled plugins load into a
  `Plugins` context (`GameLoader.LoadPlugins`*), which the game's resolves
  through `SharedAssemblies`; a standalone run finds them copied beside
  the game.

## Consequences

- A compiled plugin's runtime assembly is built once and ships as it is:
  code under `CRUCIBLE_EDITOR` in it is gone (that belongs in its
  `Editor` folders), and its information messages stay even in Shipping
  packages (`Log.Info` is compiled out at each call site, ADR 0103).
- A compiled plugin is built against one Crucible line; every minor
  version before 1.0 needs its plugins rebuilt.
- Plugin assets loaded by ID from code go in the game's build settings'
  include list ("Waypoints:Paths"), as the game's do.
- Opening another project that uses the same compiled plugin, while the
  editor runs, loads it again into a new context; the old one unloads once
  the previous game does.
- `PluginChecks`* (CTest `launcher.jit.plugins`*) runs a source plugin
  through building, loading, Play, reloading, packaging, and Build Plugin,
  then a second copy using the compiled build; `samples.plugins-package`*
  and `samples.plugins-package-aot`* package that copy and run it, the
  plugin's system printing. `PluginTests`* check reading, ordering, the
  refusals, the version rule, and assets' paths.
