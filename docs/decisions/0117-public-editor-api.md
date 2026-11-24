# 0117 — The public editor API

**Status:** Accepted (2026-11-23); step 2 of editor extensions, tried by
hand by the owner. Names settled by the owner as drafted (2026-11-23;
docs/naming.md).

## Context

ADR 0116 gave a project somewhere to put editor code, compiled into
`<Game>.Editor.dll` and reloaded with the game, but nothing for that code
to use: the shell, its panels, commands, menus, undo, and selection were
internal, and the editor's own panels were registered inside the shell.
Step 2 decides how editor code plugs in and what it can reach, and builds
step 1's leftover: a project's panels reopened in place after a reload.

## Options

**How editor code plugs in:** an entry-point class; attributes on
panels and commands, found by scanning; both. **What it can touch:** a
facade handed to the entry point; the shell made public. **What it
promises:** free to change until 1.0; stable now. **A panel's state across
a reload:** a save and load hook writing plain values; fields marked to be
copied by name; lost. **Where a plugin's commands go:** menu paths, Tools
by default; a Tools menu only. **Component edits:** a typed edit call that
records the undo step; plugins' own steps only. **File edits:** whole-file
undo steps; no undo. **A shortcut already in use:** the first registered
keeps it, with a warning; the plugin's wins.

## Decision

Decided by the owner (2026-11-23):

1. **An entry-point class.** Every non-abstract `EditorPlugin` in the
   editor assembly with a public parameterless constructor is made and
   its `Start(EditorContext editor)` called after each load, in the order
   of their full names. Before the next load (or another project), `Stop()`
   runs and everything the plugin added is taken out again: one explicit
   place to read, and nothing left over from an old version.
2. **A facade.** `EditorContext` (one per plugin; `Editor` would clash
   with the `Crucible.Editor` namespace) offers `Panels` (`Add`, `Open`,
   `Close`), `Commands` (`Add(command, menu = "Tools")`), `Selection`,
   `Undo` (the `UndoHistory`), `Scene` (`Edit<T>`, `Add<T>`, `Remove<T>`),
   `Assets` (`Database`, `Write`, `WriteText`), `Log`, `LogWarning`,
   `LogError`, and `Status`. The shell stays internal, so the editor's
   insides can change without breaking plugins. Panels are `EditorPanel`s,
   commands the UI's `Command`s.
3. **Free until 1.0.** Any minor version may change the API, every change
   listed in a changelog (Crucible has none yet: an open item); revisited
   once map mode (step 5) and the first game have used it.
4. **A save and load hook.** `EditorPanel.SaveState(PanelState)` and
   `LoadState(PanelState)`, a bag of numbers, strings, and bools
   (`PanelState`), nothing that belongs to the code being unloaded. Before
   a reload, a plugin's open panels are replaced in place by placeholders
   holding their state (`PendingPanel`: "Walker Stats opens when the
   project's editor code has built"); after it, made again and given it.
   A placeholder whose panel the new version doesn't register closes. The
   saved layout keeps every panel's state and marks plugins' panels
   (`"PanelStates"`), so after a restart a placeholder holds the place
   until the project's code has loaded.
5. **Menu paths, Tools by default.** A command's menu is a path
   ("Tools/Walker", "Edit"): a missing top-level menu is made before
   Window (Tools appears only when something's in it), and submenus as
   items. In a menu of the editor's own, plugins' items go after a
   separator at its end; in Window, plugins' panels go before Reset
   Layout. All are taken out with the plugin; menus it made, once empty.
6. **A typed edit call.** `editor.Scene.Edit(entity, value, title)` sets a
   component the entity has and records a step from its saved form, as the
   Inspector does; `Add` and `Remove` likewise. Only components scenes save
   can be changed (the step is their JSON). Plugins can still push steps
   of their own, subclassing `UndoStep`.
7. **Whole-file undo steps.** `editor.Assets.Write(path, bytes, title)`
   (and `WriteText`) writes a file under Assets, keeping its contents
   before (or that it wasn't there) and after; undo writes them back, and
   the asset imports at once. Paths leaving the Assets folder are refused.
8. **The first registered keeps a shortcut.** The editor's own register
   first; a plugin's command whose shortcut is taken registers without
   it, still in its menu: "Walker.BakePaths wants Ctrl+S, already used by
   File.SaveScene; it has no shortcut".

Plugin code runs guarded: an exception from `Start`, `Stop`, a command,
or a panel's making or state is logged as an error ("Walker.Tools.WalkerPlugin:
Walker.Boom failed: InvalidOperationException: boom") and the editor goes
on.

## Unloading

A reload must leave nothing that keeps the old code loaded (ADR 0116's
leak check). Taking out what the plugin added covers the panels,
commands, and menu items. Two more things are reached through the facade
and outlive a plugin:

- **Handlers** a plugin adds to `Selection.Changed` or
  `UndoHistory.Changed` are removed when it stops: those whose method or
  target comes from the unloading code.
- **Undo steps** of the plugin's own types can't stay in the history. The
  newest of them goes, with every step only reachable through it: the
  steps done before it, and those to redo after it ("Undo can't go back
  past "Plugin Step": its step came from the editor code just reloaded (5
  steps dropped)"). The history kept at Play is cut the same way. Steps
  the facade records (component edits, file writes) are the editor's own
  types and survive.

## Consequences

- The editor's own panels and commands still register inside the shell;
  moving them onto the API is for when it's cheap (map mode, step 5).
- Editor code sees one plugin context per entry point; several entry
  points in one assembly are allowed.
- What a plugin needs and the facade lacks (viewport tools, workspaces,
  preview worlds, inspectors) comes in steps 3 to 7.
- `EditorApiChecks` (CTest `launcher.jit.editor-api`) runs a plugin
  through five versions on a copy of the Walker: its panel, commands, and
  menus; a command that throws; component, file, and its own undo steps;
  the old version unloading; its panel remade in place with its state;
  the layout's state and placeholder; and everything gone with its code.
  `UndoTests` and `PanelStateTests` check the history's cutting and the
  state bag.
