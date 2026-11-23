# Dropping files into a program

How a file dragged from a file manager reaches a program, and what the
editor does with it (ADR 0115).

## Three systems, one shape

Every desktop has the same idea, built differently:

- **Windows (OLE).** A window registers an `IDropTarget`. While something
  is dragged over it, Windows calls `DragEnter`, `DragOver` (with the
  pointer's position), and `DragLeave` or `Drop`. What's carried is an
  `IDataObject`; files come as `CF_HDROP`, a list of paths.
- **X11 (XDND).** Not part of X itself but a convention between programs:
  the source sends `XdndEnter`, `XdndPosition`, and `XdndDrop` client
  messages, the target answers `XdndStatus`, and the data is fetched
  through a selection, as a `text/uri-list` of `file://` URIs.
- **Wayland.** Each seat has a data device; a drag arrives as a data
  offer with the MIME types it can give. The program accepts one, and on
  the drop reads the data through a pipe, again a `text/uri-list`.

In all three, the target learns where the pointer is while dragging, but
gets *what* is carried only when it's dropped (it can see the types
offered, not the paths). So the editor can say "Drop to import into
Textures/" during the drag, but can't yet say "3 images".

SDL 3 turns all three into the same events: `SDL_EVENT_DROP_POSITION`
while over a window, one `SDL_EVENT_DROP_FILE` per path on the drop, then
`SDL_EVENT_DROP_COMPLETE` (also sent when the drag leaves without a
drop). Crucible translates them into `FileDragMoved`, `FileDropped`, and
`FileDragEnded`, the paths stored like typed text: one buffer for the
frame, each event pointing at its slice.

On Wayland a seat has one data device per program connection, and
Crucible already has its own, for dragging editor windows between each
other (ADR 0066). It only takes offers of its own window-drag type, so
other programs' files still go to SDL's.

## A drag from outside as a drag inside

The editor already moves things around by dragging: an asset onto the
viewport, a panel onto another window. Those go through the UI's drag
routing, where the element under the pointer that allows drops gets
`DragOver` and `Drop`. A drag from another program is fed through the
same routing, with a `FileDrag` payload, so each panel decides what a
drop means where it is: the Assets panel's folder tiles take files into
that folder, the viewport places what it can where it lands. Nothing in
the routing knows the files came from outside.

## Guessing what an image is for

A texture for a 3D model and a pixel-art sprite want opposite treatment:
the texture is block-compressed (BC7: 4x4 pixel blocks, so sizes must be
multiples of 4) and mipmapped; pixel art must keep its exact pixels and
never be filtered. Picking wrong is a visible mistake, so the editor
asks, with a guess from the pixels:

- **Small:** at most 512x512. Pixel art is drawn a pixel at a time.
- **Few colors:** at most 256. Photos and paintings have thousands.
- **Hard alpha:** every pixel fully clear or fully opaque. Painted
  edges fade; pixel-art edges don't.

Anything else is a texture, unless its size isn't a multiple of 4, when
block compression can't store it and UI (uncompressed) is the guess.

## Bringing what a file needs

A `.gltf` is JSON naming other files by relative URI: its buffers
(`.bin`) and often its images. Copied alone it would import broken, so
they come along. If the project already has a different file of the same
name there, the copy is renamed and the URI in the copied `.gltf`
rewritten (URIs are percent-encoded: `Walker 2.bin` is `Walker%202.bin`).

A file "replaced" keeps its own sidecar, so its GUID, and everything in
scenes that points at it, survives the new contents.
