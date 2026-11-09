# 0071 — The asset browser

**Status:** Accepted (2026-11-09); built. Names settled by the owner as
drafted; those added while building are placeholders (docs/naming.md).

## Context

M7 step 5: the project's assets browsed in the editor, imported again when
files change, and models dragged into the scene. It decides drag and drop
and file watching. The asset database already listed, refreshed, and
imported; the UI's drags lived inside single controls; tab drags already
crossed OS windows through the platform's window drag (ADR 0066).

## Options

**Layout:** two panes (a folder tree, a grid of tiles); one tree; or a
grid with a breadcrumb.

**Watching:** .NET's `FileSystemWatcher`; a rescan when the window regains
focus only; or a native watcher of our own.

**Drag and drop:** in Crucible.UI, for games too; or inside the editor.

**Placing in the viewport:** on what's under the pointer; on the ground
plane; or a live preview that follows the pointer.

## Decision

1. **Two panes:** folders on the left (a `TreeView`), the folder's
   subfolders and files as `AssetTile`s on the right, a breadcrumb above.
2. **`FileSystemWatcher`** (`AssetWatcher`): changes reported once they've
   stopped for 0.3 s; the database then rescans and imports. Everything is
   rescanned when a window regains focus (a new platform event,
   `WindowFocusGained`) or the watcher's buffer overflows, and on Refresh.
3. **Drag and drop in Crucible.UI:** `UIRoot.BeginDrag(payload, preview)`,
   `CancelDrag`, `IsDragging`; elements with `AllowDrop` get `DragEnter`,
   `DragOver` (setting `DragEvent.Accepted`), `DragLeave`, and `Drop`.
4. **A live preview:** a model or prefab dragged into a viewport is
   instanced at once and follows the pointer, landing on what's under it.

## Details

- **Drags:**
  - starting one lets go of the pressed element; moves then go to the
    nearest element under the pointer that allows drops;
  - releasing checks the target once more, then drops if it accepted;
    Escape cancels; a preview element follows the pointer, beside it;
  - **between windows:** when the pointer leaves its window, the root
    tells its host (`DragOutside`); the editor's shell then starts a
    platform window drag with no window attached, and carries the drag to
    whichever of its windows the platform reports it over
    (`ContinueDrag`, `DropAt`). Where window drags aren't supported, a drag
    stays in its window.
- **Picking for placement:**
  - `PickResult.depth`: the pick step writes depth to a second target
    beside the entity IDs, and both pixels are read back (the depth at
    offset 512, where D3D12 wants copies placed). Reverse-Z with no far
    plane, so the distance along the view axis is near ÷ depth;
  - `setSurfacePickExclusion`: entities picking looks through (the
    instance being placed, which is always under the pointer). The draw
    list now keeps a CPU copy of its instance list, so batches holding an
    excluded entity are drawn one instance at a time without it;
  - the instance lands on the picked surface; over nothing, where the
    pointer's ray meets the ground plane (y = 0); looking up at nothing,
    10 m ahead. It's asked again every frame, so a still pointer follows
    a moving scene.
- **Placing:** leaving the viewport removes the instance unrecorded;
  dropping keeps it as one step ("Place House") and selects it. A drop on
  the hierarchy places it under the row (at its parent's origin), or at
  the top below the rows.
- **Tiles:**
  - kinds by importer and extension: Folder, Model, Prefab, Texture,
    Scene, Shader, Font, Style, Material, File;
  - a texture shows its own image (acquired from the renderer); the
    others a drawn icon (a folder, a cube, a landscape, a page);
  - a click selects (the status bar shows its path, importer, and import
    state); a double-click opens a folder or a scene (asking about unsaved
    changes); the right-click menu has Open, Reimport, and Refresh.
- **Reimporting:** `AssetDatabase.Import(id, force)` imports even an up-to-date
  asset. After a refresh, prefabs are read afresh; instances already placed
  keep what they were made from.

## Why

Two panes are what both big engines' browsers show and fit a docked
panel. .NET's watcher needs nothing new and works under AOT; the focus
rescan covers what watchers can drop. Drag and drop in the UI library
serves games' own interfaces too. The owner chose the live preview, as
Unity places assets.

## Not yet

- Rendered thumbnails of models, and a drop preview in the hierarchy.
- Already loaded meshes and textures reloaded after a reimport (step 6
  clears render caches): until then, a changed model shows after a restart.
- Moving, renaming, and deleting files in the browser.
- Cross-window drags on platforms without window drags (`None`).

## As built

- **Native:** `PickResult.depth`, `setSurfacePickExclusion`, the pick
  shader's second target, `DrawList::entities`, the event
  `WindowFocusGained`.
- **Engine:** `UIRoot.BeginDrag`, `CancelDrag`, `IsDragging`,
  `DragPayload`; `DragEvent`; `UIElement.AllowDrop` and the four drag
  events; `AssetDatabase.Import(id, force)`.
- **Editor:** `AssetsPanel`, `AssetTile`, `AssetDrag`, `AssetWatcher`; the
  shell's `AssetsChanged`, `RefreshAssets`, `Reimport`, `CanPlace`,
  `Placing`, `Place`; commands `Assets.Refresh` and `Assets.Reimport`.
- **Tests:**
  - **native:** a pick's depth, and excluded entities looked through
    while others in their batch aren't;
  - **`DragDropTests`:** targets entered, moved over, and left; drops only
    where accepted; Escape; the preview; drags carried in from another
    window;
  - **`AssetWatcherTests`:** changes reported once settled; hidden files
    ignored; MarkAll;
  - **`AssetBrowserTests`:** where a placed model lands;
  - **`launcher.jit.assets`:** on a copy of the Diorama's project (never
    the sources): folders and tiles, a copied file noticed and imported,
    a texture not placed, Escape, a house placed under the pointer and
    following it, dropped as one undo step, removed on leaving, and
    placed under a hierarchy row; saved as `assets.png` mid-drag.
- **Tried by the owner on KWin (2026-11-09):** dragging from a floating
  Assets window into the main window placed nothing; afterwards clicks
  and drags were dead; and placing in the viewport stuttered. Fixed:
  - **the stutter:** every pointer move asked for a new pick and moved the
    frame the answer had to come from, so while the pointer moved no
    answer was ever new enough. Each frame's ask now remembers its pixel,
    and every new answer is used with the pixel it was for;
  - **dead clicks:** once the platform drag takes the pointer, the source
    window never sees the release, so its UI stayed mid-drag and ate the
    next press. A press during a drag now ends it and acts as a press
    (`DragDropTests`); a carried drag no window holds is forgotten;
  - **between windows:** with `CRUCIBLE_DEBUG_DRAGS=1`, the carried drag,
    the main window's handling of it, and where the placed asset lands are
    logged. With the fixes above, the owner's next drag from the floating
    window placed the house; `launcher.jit.assets` checks a drag carried
    in from another window.
