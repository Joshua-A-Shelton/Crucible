# 0121 — Viewport tools

**Status:** Accepted (2026-11-24); step 6 of editor extensions. Names
marked `*` in docs/naming.md are placeholders until the owner has tried it.

## Context

What a click in a scene view does was built into `SceneView`: chain
points while Edit Points was on (ADR 0095), map mode's painting in its
view (ADR 0112), otherwise the selection's handles (ADR 0067), and a click
selecting what's under it. Extensions had no way to add their own (paint a
path, place props, shape terrain). Step 6 makes "what the pointer does in
a view" something an extension writes, with chain editing and the handles
moved onto it; map mode's own rebuild is step 7.

## Options

**Turning a tool on:** explicitly, like a toolbox; automatically by the
selection; both. **The camera's moves:** always the view's; a tool may
take any. **Drawing:** an overlay on top; that and lines hidden behind
geometry. **Asking what's under the pointer:** the ray and planes; the
entity; the surface point. **The handles:** tools too; built in.
**Extensions' tools' buttons:** the Viewport's toolbar; only where the
extension puts them. **Keys:** the active tool's first; the editor's
first.

## Decision

Decided by the owner (2026-11-24):

1. **Explicit, like a toolbox.** A view has one tool on, `SceneView.Tool`*,
   chosen by its button or key; Escape goes back to Move. A workspace
   sets its own view's tool (map mode's view paints).
2. **A tool may take any input.** `ViewportTool`*'s `PointerDown`*,
   `PointerMove`* (a press it took, or hovering), `PointerUp`*, `Wheel`*,
   and `KeyDown`* say whether they took it; what isn't taken goes to the
   camera (right-drag looks and flies, middle-drag pans, Alt and the
   left button orbit, the wheel dollies) and the editor. Map mode's
   painting takes the right button, for picking tiles up. `Activated`* and
   `Deactivated`* mark it turned on and off.
3. **An overlay now, hidden lines later.** `Draw(view, canvas)`*, each time
   the view draws: `ToolCanvas`*'s lines, rectangles, circles, images, and
   text, in the view's units or at world points through the camera
   (`WorldLine`*, `WorldPoint`*), always on top. The engine's world debug
   lines (ADR 0079) stay too. Lines hidden behind geometry come when a
   tool needs them.
4. **All three under the pointer.** `ToolView`*: `Ray`*, `OnPlane`* (both
   immediate), `PickEntity`* and `PickSurface`*, answered once the GPU
   has drawn the frame (the pick already read the depth, ADR 0071, so the
   surface point needed no new render work).
5. **The handles are tools too.** Move, Rotate, Scale (W, E, R; IDs
   `Scene.Move`*, `Scene.Rotate`*, `Scene.Scale`*) and Edit Points (P,
   `Scene.EditPoints`*) are the editor's own tools on the same API: Move
   the default, a click off the handles selecting what's under it, Edit
   Points' P a toggle as before, and its Inspector button turning it on.
6. **The Viewport's toolbar.** `editor.Tools.Add(tool, shortcut)`* gives an
   extension's tool a button after the editor's, and its key (the first
   registered keeps a key, as with commands); `editor.Tools.Activate(id)`*
   turns it on from the extension's own commands. A workspace's view's
   tool goes on its toolbar its own way.
7. **The tool's keys first**, then the tools' keys, then the editor's.

Tool code runs guarded: an exception is logged ("Marks.Place: KeyDown
failed: InvalidOperationException: j"*) and the editor goes on. An
extension's tools leave with its code, the views using them back on Move.

## Consequences

- `SceneView` keeps the camera, flying, drops, and rendering; chain
  editing, the handles, and map painting are no longer its special cases.
- A view asks one pick at a time; a newer request replaces one not yet
  answered (a tool chaining an entity's pick into a surface's waits for
  the first).
- `ViewportToolChecks`* (CTest `launcher.jit.viewport-tools`*) runs an
  extension's tools through their buttons and keys, a click, the plane and
  picks, drawing, the right button left to the camera, a guarded
  exception, Escape, a command, and a reload.
