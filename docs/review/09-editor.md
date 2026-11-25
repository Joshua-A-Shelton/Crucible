# Review 9: the editor

The editor itself: the shell and docking, the scene viewport and its
tools, undo, the hierarchy and inspector, the assets panel and imports,
play mode and the project's code, and the extensions API that plugins
and official plugins are built on. Decisions: ADRs 0016, 0066, 0067,
0068, 0069, 0070, 0071, 0072, 0087, 0088, 0089, 0115 to 0128.

## What it does

A C# application on Crucible's own UI, run by a C++ launcher like a game.
It opens a project, shows its scene in viewports, edits it with undo,
imports what's dropped on it, builds and hot-reloads the project's code,
and plays the game in its own process. Plugins add panels, workspaces,
tools, drawers, and commands through one public API.

```
 Launcher (C++) ─► .NET ─► Crucible.Editor (C#, Crucible.UI)
   Shell: windows, docking, menus, toolbar ── layout and settings per user
     ├─ Viewport / SceneView: offscreen surface, GPU ID pick, tools (Move…)
     ├─ Hierarchy, Inspector (field tables + JSON, drawers), Assets, Log
     ├─ Workspaces (map mode, Sprite Sheet, plugins'), previews
     └─ UndoHistory: edits by scene ID, as JSON before and after
   Project: Code/ ─► Game.dll + Game.Editor.dll (built, hot-reloaded)
            Plugins/, official plugins ─► EditorContext (public API)
   Play: snapshot the scene ─► run the game's systems on it ─► restore
```

## How it works

### The shell and the viewport (ADR 0016, 0066, 0067, 0121)

- **The editor is C#,** on the same UI library as games (page 5). Panels
  dock, tab, and float into their own windows and back, on Windows, X11,
  and Wayland (its own drag protocol, with fallbacks).
- **Settings and layout per user** (`editor.json`), not in the project.
- **A viewport is an offscreen surface** drawn as a texture, with its own
  render schedule. Picking reads a **GPU ID buffer** (exact for any mesh,
  and the selection outline for free), a frame or two late.
- **Unity's camera controls** (right-drag flies, Alt orbits, F frames).
- **Tools** (`ViewportTool`): one on at a time per view; Move, Rotate,
  Scale, and Edit Points are tools on the same API as an extension's.

### Undo, hierarchy, and inspector (ADR 0068, 0069, 0089, 0126, 0127)

- **Undo records name entities by scene ID,** never handle: a component's
  JSON before and after, a subtree as a small scene file, a move with its
  transforms. Groups make a gesture one step.
- **The inspector edits JSON:** each `[Serialized]` type has a generated
  field table; a field edit rewrites the component's JSON. Hints (range,
  tooltip, color, header, asset kinds, entities with a component, a
  model's meshes) shape the fields; drawers replace them (ADR 0126).
- **Prefab instances** show overrides, with Revert and Revert All.
- **Add Component** is a dialog grouped by category (ADR 0127).

### Assets and imports (ADR 0071, 0087, 0115)

- **The Assets panel** watches the folder; changes settle for 0.3 s, then
  the database rescans and imports. Models and prefabs drag into the
  viewport with a live preview.
- **Dropping files** from the desktop copies them in (a glTF with its
  buffers, a sidecar with its settings), asks about clashes, and asks how
  to import an image (pixel art, texture, UI), guessing.
- **Import settings** in the inspector, applied explicitly; every core
  message goes to the Log.

### Play and the project's code (ADR 0072, 0073, 0088, 0116)

- **A generated project:** `.cs` under `Code/` build into `Game.dll`; any
  `Editor/` folder into `Game.Editor.dll`. Both reload on save, playing or
  not, components migrated.
- **Play in the editor's process:** the scene is snapshot to memory, the
  game's systems run on the same world, Stop restores it (selection and
  undo survive by scene ID). Pause holds gameplay and sound, Step runs one
  tick. **Run Standalone** starts the game in its own process.

### Extensions (ADR 0117 to 0125)

- **`EditorExtension.Start(EditorContext)`:** panels, commands, undoable
  scene and asset edits, workspaces, tools, editors that open assets or
  components, scene events (cancel, hold, follow up), previews, drawers.
  Everything added is taken out on Stop, so a reload leaves nothing.
- **Plugins** (source or compiled, with a manifest and uses) and
  **official plugins** shipped with the editor, chosen per project.
- **The API is free to change until 1.0;** the shell stays internal.

## Why it was built this way

- **C# and in-process** for iteration: hot reload, the game's own types in
  the inspector, no serialization across a process boundary.
- **Its own UI,** so the editor is the UI library's hardest user and games
  inherit what it needed.
- **Edits by scene ID as JSON:** undo, play's snapshot, prefabs, and
  reloads all survive entity handles changing.
- **The editor's own features on the public API** (map mode, tools,
  Tilemaps as a plugin), so the API is proven by real use rather than
  examples.

## What I'd question

1. **One crash loses the session.** Play runs in the editor's process, so
   a native crash in game code (or the engine) takes the editor down, and
   there's no autosave or recovery of unsaved scene edits. Autosaving
   the open scenes (or a recovery file on Play) would cover it cheaply.
2. **One entity selected at a time.** `Selection` holds a single entity:
   no multi-select, so moving, deleting, duplicating, or editing a field
   across several (all the torches, a row of crates) is one at a time.
   Level building will hit this early, and it touches tools, the
   inspector (mixed values), and undo groups.
3. **Prefabs can't be made or changed from the scene.** Instances show
   overrides and can revert them, but there's no "make a prefab from this",
   "apply overrides to the prefab", or opening a prefab to edit it on its
   own; nested prefab edits are by hand.
4. **Shortcuts follow focus.** W, E, R, P and tools' keys work only while
   a view has focus, so selecting in the hierarchy then pressing W does
   nothing (open items); a rule for editor-wide keys is needed before
   more tools add theirs. The M7 small fixes (hierarchy order by entity
   slot, clipped number fields) are still open.
5. **The editor API has no changelog.** It's free to change until 1.0,
   with every change to be listed, but where the changelog lives (and what
   else it records) is an open owner decision, and plugins now depend on
   the API.
