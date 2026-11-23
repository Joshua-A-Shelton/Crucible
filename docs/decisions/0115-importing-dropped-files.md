# 0115 — Importing dropped files

**Status:** Accepted (2026-11-23). Names marked `*` in docs/naming.md are
placeholders until the owner has tried it.

## Context

Asked for by the owner (2026-11-18): files dragged from the system's file
manager onto the editor should be copied into the project and imported,
with the import settings they need, without opening the project's
folders. The editor got no dropped files: the platform layer passed on
only Crucible's own window drags (ADR 0066).

How drops reach a program differs by system: OLE's `IDropTarget` on
Windows, the XDND protocol on X11, and data offers on a Wayland seat's
data device. SDL 3 handles all three and sends `SDL_EVENT_DROP_POSITION`
while files are over a window, one `SDL_EVENT_DROP_FILE` per path on the
drop, and `SDL_EVENT_DROP_COMPLETE` when it ends, dropped or not. On
Wayland, Crucible's own data device (ADR 0066) takes only its window
drags (`application/x-crucible-window-drag`) and leaves other programs'
offers to SDL's, so no platform code of our own is needed. The paths
arrive only with the drop: while dragging, a program learns where the
pointer is, not what's carried.

## Options

**Where files go:** only on the Assets panel, or anywhere in the editor.
**A name already there:** replace, keep both, or ask each time. **Images:**
imported with the defaults, or asked about with a guess. **Companions:**
a `.gltf`'s buffers and images, and a sidecar beside a file. **Files no
importer reads:** refused, or copied anyway. **Several files asking:**
each asked, or each with "Do this for the rest". **While dragging:** no
feedback, or where they'll go.

## Decision

Decided by the owner (2026-11-23):

1. **Platform events.** `InputEventType.FileDragMoved`* (over a window,
   in pixels), `FileDropped`* (one per file or folder, its path in the
   polled text, as typed text is), and `FileDragEnded`* (after the
   dropped paths, or on leaving without a drop), translated from SDL's.
2. **A UI drag.** The shell carries the files as a `FileDrag`* payload
   through the window's drag routing (`ContinueDrag`, `DropAt`, as a drag
   carried between windows is), so the element under the pointer says
   where they'd go:
   - **Assets panel:** the folder shown; a folder tile, that folder. The
     folder (or the panel) is highlighted (`drop-over`).
   - **Viewport, hierarchy:** the Assets panel's folder; a model, prefab,
     or image imported as pixel art is placed where it was dropped (on the
     viewport: on the surface under the pointer, pixel art on the 2D
     plane; on the hierarchy: under the row, or at the top).
   - **Anywhere else:** the Assets panel's folder.
   A note beside the pointer says where: "Drop to import into Textures/"*.
3. **Copying** (`Shell.ImportFiles`*). A folder is copied whole (hidden
   files left out); a `.gltf` brings its buffers and images named by
   relative URI. A companion that would land on a different file of the
   same name gets a free name, and the copy's URI follows it; one already
   there as it is isn't copied again. A sidecar beside a dropped file
   comes with it, its settings kept, with a GUID of its own. Files no
   importer reads are copied anyway, and the Log says so: "Copied
   Props/notes.xyz: nothing imports .xyz files"*.
4. **A name already there** asks: "Hero.png is already there"*, with
   **Replace***, **Keep Both*** (the first guess: "Hero 2.png"), and
   **Skip***. A replaced file keeps its sidecar, so its GUID and settings
   stay and what uses it keeps working.
5. **A new image** asks: "Import Hero.png as"*, with **Pixel Art***
   (`Sprite` usage), **Texture*** (`Color`), and **UI*** (`Ui`); the
   guess (`ImageGuess`*) is the primary button. Pixel art if it's at most
   512x512, at most 256 colors, and its alpha only clear or opaque; else
   a texture, or UI when block compression can't store its size (not a
   multiple of 4), where Texture isn't offered. Its sidecar is written
   before the image, so no rescan imports it with the defaults. Exactly
   one image made pixel art opens the Sprite Sheet Editor (ADR 0107).
6. **One question at a time** (`DropQuestion`*), each with **Do this for
   the rest*** while more files would ask the same. Escape cancels the
   drop: nothing is copied. A drop while one is still asking is refused.

## Consequences

- Assets arrive with the settings they need in one step; pixel art never
  imports first as a block-compressed texture (which fails at sizes that
  aren't multiples of 4).
- A headless run gets no drop from the system, so the checks
  (`DropChecks`*, CTest `launcher.jit.drops`*) hand the shell the events
  the platform would give; the native test pushes SDL's drop events
  through the translation.
- The guess reads the whole image once (a sprite import, the source's
  pixels exactly); a large image is decoded only to be measured.
- Dropping a `.gltf` and a different buffer of the same name renames
  the buffer, not the model.
