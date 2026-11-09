# Drag and drop, and file watching

A learning note for M7 step 5 (ADR 0071).

## A drag is a mode the pointer enters

Normally a press "captures" the pointer: the element pressed gets every
move until the button is released, wherever the pointer goes. That's
right for a slider, wrong for a drag, where what matters is what's under
the pointer now. So starting a drag switches modes: capture is let go,
and each move asks "which element under the pointer takes drops?"

That element is told when the drag enters it, as it moves over it, and
when it leaves. While the pointer moves over it, it says whether it would
accept what's dragged (a viewport accepts a model, not a texture). The
drop goes to it only if it accepted. Because acceptance is decided on
every move, a target can depend on where in it the pointer is, as the
hierarchy's rows do.

The thing being dragged, the payload, is just an object: the UI doesn't
know what an asset is. The editor's viewport looks at the payload and
decides.

## Crossing windows

Inside one window, the UI does everything. But the editor can have
several OS windows, and the operating system sends pointer events to the
window under the pointer (or, with a button held, to the one where the
press began). To carry a drag from one window to another, the editor uses
the platform's own drag, the same one tab drags use (ADR 0066), with no
window attached: the platform then reports which of the editor's windows
the pointer is over, and where. The shell hands the drag to that window's
UI. On Wayland this is the only way: a program can't learn where its
windows are on the screen.

## A live preview

When a model is dragged into the viewport, the editor makes the instance
right away and moves it with the pointer, so you see it where it will
land. Where is "under the pointer"? The viewport already draws every
object's ID into a hidden image for picking (ADR 0067); now it also
draws depth into a second image. One pixel of each is read back, and the
depth turns into a distance: with the renderer's reverse-Z projection,
the distance along the view direction is the near plane divided by the
depth value. Walk that far along the pointer's ray and you're on the
surface.

One catch: the instance being placed is always under the pointer, so it
would always be what's picked. Picking is told to look through it. The
GPU draws objects in batches (all houses in one draw), so a batch holding
the preview is drawn one instance at a time, skipping it.

## Watching files

Operating systems can tell a program when files in a folder change
(inotify on Linux, ReadDirectoryChangesW on Windows); .NET wraps both as
`FileSystemWatcher`. Two things make it less simple than it sounds:

- **Saves come in pieces.** An image editor may write a temporary file,
  delete the old one, and rename the new one into place: several events
  for one save. Reacting to the first would import a half-written file.
  So changes are collected until none have come for a moment (0.3 s),
  then handled together. That's called debouncing.
- **Events can be lost.** The watcher has a buffer; when too many changes
  arrive at once (a branch switch in version control), it overflows and
  reports an error instead. Then the only safe thing is to rescan
  everything. The editor also rescans when its window regains focus, the
  moment you return from another program, which covers anything missed.

The rescan itself is cheap when nothing changed: the asset database
compares sizes and dates before it hashes anything, and imports only what
its records say is out of date.
