# 0119 — Workspaces

**Status:** Accepted (2026-11-24); step 4 of editor extensions, tried by
hand by the owner. Names settled by the owner as drafted (2026-11-24;
docs/naming.md).

## Context

Map mode (ADR 0112) is the editor's one specialized editor: entered for a
map, it swaps in a layout of its own (Palette, viewport, Layers), puts its
tools in the viewport's toolbar with their keys, frames the camera on the
map, and gives everything back on Done. All of it is built into the shell.
The character editor that editor extensions lead to (milestones) needs the
same: somewhere of its own to work on one thing. Step 4 makes that
general, for extensions, with map mode moved onto it. Input from the
pointer in a view (painting, dragging) is step 5.

## Options

**The model:** (A) a row of tabs above everything, each tab a whole editor
layout swapped in when shown; (B) a workspace is a tab among the panels,
like the Viewport, holding its own parts. **Its definition:** a subclass;
a description record. **Entered:** from what it's for; always-visible
tabs; both. **Inside a tab:** fixed splits; a dock space of its own.
**Its views:** each visible view renders; only the focused one renders
live. **Its keys:** while it has focus; while its tab is shown. **Closing:**
Done or the tab's ×; also Escape. **The same workspace twice:** the
workspace says; always one; one per subject. **After a restart:** the
normal layout; back in the workspace. **What it works on gone:** the
workspace decides; the editor watches.

## Decision

Decided by the owner (2026-11-23):

1. **A tab among the panels (B).** A workspace opens as a tab beside the
   Viewport, docked, moved, and floated like any panel; the rest of the
   editor stays as it is. The owner's own picture of it; the cost, a
   second scene view, was mostly there already (`SceneView` is a control
   with its own camera and surface).
2. **A `Workspace` subclass** (an `EditorPanel`), registered by an
   extension with `editor.Workspaces.Add(id, title, create)` and opened
   with `editor.Workspaces.Open(id, entity or asset)`, from what it's for.
   Its subject, `Entity` or `Asset`, is set before the subclass's
   constructor runs, so its `Title` can name it; panels may now change
   their title, and their tabs follow.
3. **Fixed splits inside.** `Arrange(left, center, right, bottom)`: the
   center usually a `SceneView`, now public (`new SceneView()`); the
   splits resizable, their sizes remembered.
4. **Its tools and keys.** `Toolbar` (`Add(command)`, `Add(control)`, and
   **Done** at the end); `Commands` whose shortcuts run while focus is in
   its tab, before the editor's.
5. **Every visible view renders**; a view in a tab not shown draws
   nothing.
6. **Done or the tab's ×** close it, calling `Closed()`; Escape doesn't.
7. **The workspace says how many tabs.** `AllowsSeveral` (false by
   default): opening it again for something else moves its one tab there
   (closing the old, the new made in the same place); with several, a tab
   per subject, `Inspect`, `Inspect#2`, …; opened again for the same
   subject, its tab is shown.
8. **Back after a restart.** The layout keeps each workspace's tab, what
   it works on (an entity by its scene ID and its scene, or an asset), its
   splits, and its state (`SaveState`/`LoadState`). Restored, it waits as
   a placeholder ("Map: Level 1 opens again when what it works on is
   open") until its workspace is registered and its subject is open; an
   extension's waits the same way across a reload of its code.
9. **The workspace decides when it's done.** `KeepOpen()`, asked each
   frame: false closes its tab. It's asked as it opens too: one that
   wouldn't stay open isn't opened (found trying the Spin example, which
   flashed a tab open and shut with nothing to spin selected). By default,
   while its entity is in the scene it was opened in and its asset is in
   the project.
10. **Map mode is the editor's own workspace**: a tab "Map: &lt;name&gt;"
    with its Palette, its own view framed on the map, and its Layers; its
    tools, flips, Show Collision, and Done above them; B, R, F, E, S, X, Y
    its keys; one tab (`AllowsSeveral` false). View > Leave Map Mode, the
    Palette's "Map Mode — Done (or Escape) to leave", and Escape leaving
    are gone; Escape still lets go of the Select tool's region. Its tools'
    painting is unchanged until steps 5 and 6.

## Consequences

- Map mode no longer swaps the layout: the Hierarchy, the Inspector, and
  the Viewport stay where they were, the Viewport's camera untouched.
  The Palette panel stays dockable for a tileset shown alone (ADR 0112);
  the map's tab has its own.
- Only one map can be open at once: map mode's state is one `MapEditor`.
  Allowing several waits for map mode's rebuild (step 6).
- A workspace with no `SceneView` is fine: its parts are whatever it
  arranges.
- `WorkspaceChecks` (CTest `launcher.jit.workspaces`) runs an extension's
  two workspaces through opening, several tabs, moving, rendering, keys,
  closing when their entity goes, the layout, and a reload; the map checks
  (`TilemapChecks`) now check map mode's tab, its keys, and Done.
