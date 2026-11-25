# 0123 — Official plugins

**Status:** Accepted (2026-11-24); step 7b of editor extensions (map mode
on the public API, in an assembly of its own). Names marked `*` in
docs/naming.md are placeholders until the owner has tried it.

## Context

Step 7 rebuilds map mode (ADR 0112) on the public editor API alone, in an
assembly of its own, so the compiler proves the API is enough; 7a (ADR
0122) added what it lacked for its scene edits. Map mode lived in
Crucible.Editor, reaching into the shell's internals. An assembly that
references the editor can't be one the editor references, so the editor
has to find and load it. The owner wants subsystems to be swappable:
Crucible ships some "official" plugins holding them, used by default, that
a project can drop when it has no use for them. Map mode is the first;
the other systems that could follow are to be gone over later.

## Options

**Loading:** an engine plugin, always on; the editor's own extensions from
its folder; (the owner) official plugins, used by default, removable.
**Scope:** the editor part only, the runtime staying in the engine until
plugins can carry native code; the editor and the C# parts (importers,
components); native plugins first, then all of it. **Reaching a
project:** listed in the project file; copied into its `Plugins/`.
**A tileset alone in the Palette:** a callback opener; a tileset
workspace. **Its checks:** an assembly of their own given the editor's
internals; the public API only. **Panels each frame:** a virtual method on
`EditorPanel`; a frame event.

## Decision

Decided by the owner (2026-11-24):

1. **Official plugins.** Crucible's own plugins are built with the editor
   into a `Plugins` folder beside it (`OfficialPlugins.Folder`*), each a
   folder as ADR 0118's with its manifest and assemblies, compiled and
   read-only. The editor loads them all once when it starts and keeps them
   until it quits (so their checks outlive the projects they open); a
   project's list decides whose extensions start. A project's plugins may
   use them, and can't take their names.
2. **Listed in the project.** The project file's `"Official"`* lists the
   ones it uses by name; `["*"]`*, the default and what a file without
   the list gets, uses all of them, so new and existing projects have
   them. Leaving one out drops it; engine updates bring their updates. The
   list is edited by hand for now (a Plugins window is an open item).
3. **Tilemaps*, the editor part only.** Map mode, the Palette, the
   Layers, and New Tileset move to `plugins/Tilemaps/`* as
   `Tilemaps.Editor.dll`* (namespace `Crucible.Tilemaps.Editor`*), which
   sees only the editor's public API. Tilemaps' runtime (drawing,
   collision, the components) and its importers stay in the engine until
   plugins can carry native code; then they move in, and a project
   leaving Tilemaps out loses them too.
4. **What the API gained** for it (beside ADR 0122's):
   `editor.Assets.AddOpener(id, title, kinds, open)`* (a callback opener:
   a tileset alone in the Palette, a `.crtilemap` through the entity using
   it), `editor.Assets.AddCreate(path, create)`* (the Assets panel's Create
   menu: Create > Tileset…), `editor.Assets.Changed`*,
   `CanWrite(path)`*, `Delete(path, title)`*, and, found while building,
   `editor.Assets.Project`*, `ApplySettings(asset, settings)`* (a texture
   made a sprite), `Reimport(asset)`* (a map saved as it's painted), and
   `editor.Scene.Path`* (where new maps go) and
   `editor.Scene.AddOpener<T>(id, title, open)`* (map mode from a map's
   or a layer's components). `EditorPanel.Tick()`* runs every open panel
   each frame (the editor's own type tests gone), and
   `SceneView.ShowsOutlines`* and `TakesToolKeys`* replace map mode's
   internal switch on its view.
5. **Its checks in an assembly of their own**, `Tilemaps.Editor.Checks.dll`*,
   given the editor's internals and the plugin's for their test helpers:
   an `OfficialPluginChecks`* the editor makes when it loads the plugin
   and runs each frame. Beside the plugin in every build but shipping
   ones, and loaded when it's there.

## Consequences

- The editor no longer knows map mode: no Hierarchy item, no openers, no
  Delete question, no map renames or copies. The plugin adds all of it,
  the map files following the editor's Duplicate, rename, and Delete
  through ADR 0122's events. Create > 2D > Tilemap now comes after the
  lights in the 2D submenu (an extension's items follow the editor's).
- Checks of the extension API on their own leave the official plugins
  out of their copies (`AssetChecks.LeaveOutLocalCode`), as they leave
  out a local Walker's code; PluginChecks checks such a project has none
  of theirs. Light2DChecks makes its map through the plugin's Create item.
- The other engine systems that could become official plugins are to be
  gone over (open items).
