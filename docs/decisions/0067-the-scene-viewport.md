# 0067 — The scene viewport

**Status:** Accepted (2026-11-04); built. Names settled by the owner as
drafted, including those added while building (docs/naming.md).

## Context

M7 step 3: the world rendered inside the editor, with a camera of the
editor's own, picking, and handles to move, rotate, and scale. The
renderer could already draw into an offscreen surface (for reading pixels
back), each surface ran a schedule of its own (ADR 0066), and the UI could
draw textures. Missing were:
- an editor camera that isn't an entity in the scene;
- the viewport's image inside a panel;
- picking and handles;
- something to look at: the editor didn't yet import a project's assets
  or open scenes.

## Options

**The viewport's image:** an offscreen surface the panel's size, drawn
by the UI as a texture; or the scene's steps drawn straight into the
panel's rectangle of the window's own image.

**Picking:** a GPU ID buffer, each entity's ID drawn and the pixel under
the pointer read back; or rays against bounding boxes on the CPU.

**Handles:** drawn by the UI over the image; or meshes drawn by a render
step.

**Camera controls:** Unity's, Blender's, or Unreal's.

## Decision

1. **An offscreen surface,** drawn by the UI as a texture.
2. **A GPU ID buffer.** Exact to the pixel for any mesh, and it outlines
   the selection for free (the edges where IDs change). An answer comes a
   frame or two late.
3. **Handles drawn by the UI.** Crisp, always on top, hit tested on
   screen.
4. **Unity's controls:**
   - the right button held looks around, and flies with WASD, Q and E
     (the wheel sets the speed);
   - Alt and the left button orbit;
   - the middle button pans;
   - the wheel dollies;
   - F frames the selection;
   - W, E, and R pick the handles.

## Details

- **Content:**
  - opening a project now opens its asset database and imports what
    changed (step 5 adds watching);
  - File › Open Scene… (Ctrl+Shift+O) or `--scene <file>` loads a
    `.crscene` into the engine's world, after clearing the entities the
    last scene made;
  - the Diorama's village is checked in as
    `samples/Diorama/Project/Assets/Scenes/Village.crscene`, written once
    by the Diorama sample's new `save-scene` option;
  - the editor finds the asset pipeline's native library beside its
    executable, where the build now copies it.
- **The renderer:**
  - **A surface's own camera:** `setSurfaceCamera` / `clearSurfaceCamera`
    (a position, a rotation, and the camera's settings). It's drawn from
    instead of the world's, whose chosen camera still gives the
    post-processing, so the view looks like the game's.
  - **The image as a texture:** `surfaceTexture` hands it out in the
    render assets' texture table. From then on each frame leaves it ready
    to sample, and the window's frame, later on the same queue, samples
    it.
  - **Resizing:** `resizeOffscreenSurface` keeps the handle.
- **The editor's steps:**
  - `Grid` (after Sky), `Pick` (after Opaque), and `SelectionOutline`
    (after Tonemap), added by `addEditorSteps` (C#
    `RenderSchedule.AddEditorSteps`).
  - The scene's steps keep their pipelines in globals, so they can't run
    in a second schedule. The editor's steps therefore sit in the default
    schedule and draw only on surfaces that ask: `setSurfaceGrid`,
    `setSurfaceSelection` (the meshes of up to 16 entities: a prefab
    instance's parts), and `requestPick` / `pickResult`.
- **The steps themselves:**
  - **Grid:** the ground plane under each pixel's ray, depth-tested
    against the scene through the depth it writes. Lines every metre
    (faded where they'd crowd) and every ten metres, the X axis red and
    the Z axis blue, fading by 120 m.
  - **Pick:** every opaque instance again into an R32Uint target with a
    depth of its own (entity index plus one), only while a pick is asked
    for or something is selected. A requested pixel is copied into a small
    readback buffer per frame slot, and its answer read once the GPU has
    finished that frame. Alpha is ignored: a leafy card picks as its
    whole quad.
  - **SelectionOutline:** orange where a selected entity's pixels meet
    anything else's, two pixels wide, over the finished image.
- **The editor:**
  - **`EditorCamera`:** pivot, distance, yaw, and pitch; orbiting,
    looking, panning (the pivot's depth tracks the pointer), dollying
    (15% a notch), flying, framing; the renderer's matrices, rays,
    projections, and on-screen sizes.
  - **`Selection`:** one entity. A click selects the topmost entity above
    the mesh picked, a prefab instance's root. A click on nothing, or
    Escape, clears it.
  - **`TransformHandles`:**
    - **move:** axes, and plane squares that hide when nearly edge-on;
    - **rotate:** three rings;
    - **scale:** axes, and a uniform center;
    - all 90 pixels long whatever the distance;
    - local or world space (scaling always local);
    - Ctrl snaps to 0.5 m, 15°, and 0.1;
    - edits set the entity's `LocalTransform`, through its parent's space
      when it has one. Step 4 makes them undoable.
  - **`SceneView`:** the offscreen surface sized to the element in
    pixels and rendered each frame before the window (an
    `EditorViewportSystem` before the `RenderSystem`). It draws the image,
    then the handles.
  - **`ViewportPanel`:** a toolbar (Move, Rotate, Scale, the space, the
    grid, the frame's CPU time) over the view.

## Why

An offscreen surface keeps a viewport independent of its window: it can
float, there can be several, and the UI draws over it like any image. An
ID buffer picks exactly what's drawn, which bounds can't, and gives the
outline with nothing more. Handles on top of the image are what editors
draw and the simplest to hit. Unity's controls are the owner's choice.

## Not yet

- The main window still builds its draw list for the world each frame
  (its schedule draws only the UI); harmless at the village's size.
- Picking ignores transparent materials and alpha.
- The outline covers at most 16 entities of a selection.
- Selecting several entities, box selection, and undo (step 4).

## As built

- **Native:**
  - `Render.h`'s `setSurfaceCamera`, `clearSurfaceCamera`,
    `surfaceTexture`, `resizeOffscreenSurface`, `setSurfaceGrid`,
    `setSurfaceSelection`, `requestPick`, `pickResult`, `PickResult`;
  - `RenderCommands.h`'s `addEditorSteps`;
  - `ecs::entityAtIndex`;
  - `RenderStepContext::surface`;
  - shaders `editor.slang` (Pick) and `editorOverlay.slang` (Grid,
    SelectionOutline).
- **C#:**
  - `World.Handle` (public);
  - `RenderSchedule.AddEditorSteps`;
  - `Crucible.Editor`'s `EditorCamera`, `Selection`, `TransformHandles`,
    `HandleMode`, `HandleSpace`, `SceneView`, `ViewportPanel`,
    `EditorViewportSystem`, and `ViewportChecks`;
  - the shell's `Database`, `Prefabs`, `OpenScene`, and `ScenePath`;
  - the launcher's `--scene`.
- **Tests:**
  - **native, on every backend:** a surface's own camera, picking a hit
    and a miss, the outline, the grid, and the texture kept through a
    resize;
  - **`ViewportTests`:** the camera's views, rays, orbiting, looking,
    panning, framing; the handles' hit tests, local space, dragging,
    snapping, rings, and scaling;
  - **`launcher.jit.viewport`:** the village opened in the editor, the
    scene drawn, a house picked and outlined alone, dragged along X by
    its handle, a click on the sky clearing the selection, saved as
    `viewport.png`.
- **Tried by the owner on KWin (2026-11-04):** the camera, picking,
  outlines, handles, the toolbar, resizing, and floating the viewport all
  work. Snapping didn't: mouse events carried no modifiers (only key and
  wheel events did), so a drag never saw Ctrl, and a press never saw Alt
  or Shift. Mouse motion and button events now carry the modifiers held
  (native test), and `launcher.jit.viewport` drags with Ctrl held and
  checks the half-meter snap.
- **Tried again by the owner on KWin:** snapping and Alt+drag orbiting
  work; everything else still does.
- **Tried by the owner on Windows (2026-11-04):** every item on the
  checklist works.

