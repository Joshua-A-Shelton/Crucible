# Docking and windows: trees of panels, drags between windows

A learning note for M7 step 2 (ADR 0066).

## A layout is a tree

An editor's docked panels look like a grid, but they're a tree. The
whole area is split side by side or top to bottom; each part is split
again, or is a group of tabbed panels. Splits are inner nodes, groups are
leaves. Each split gives its children a share of its room, and a
splitter between two children moves the boundary between their shares.

Docking a panel beside a group replaces the group with a new split
holding both. If the group's parent already splits the same way, the new
group just becomes a sibling instead, so dragging three panels to the
right doesn't nest three splits. Taking panels out runs the other way:
an empty group goes, and a split with one child left gives its place to
that child. With those two rules the tree never fills with splits of
one.

Shares are kept as weights rather than fractions. Building a split
child by child with fractions means rescaling every time one is added,
which skews them; weights only become fractions when the room is shared
out.

## Where a drop goes

While a tab is dragged, the group under the pointer is the target, and
where in it decides how: the middle joins its tabs, and nearer an edge
splits it on that side. A translucent preview shows the result before the
release, which is most of what makes docking feel predictable.

## Three kinds of window placement

Windows and X11 let an app ask where its windows are, where the pointer
is on the desktop, and move a window anywhere. Dragging a torn-off panel
is then just following the pointer each frame and asking which window
it's over.

Wayland doesn't let apps do any of that. Apps can't learn their windows'
positions or move them, so one app can't spy on or cover another, and
compositors can lay windows out freely. What Wayland has is
drag-and-drop: the compositor carries a drag between surfaces, and tells
each surface when it's entered and where the pointer is in it. The
`xdg-toplevel-drag` protocol adds a window to that: the compositor moves
the attached window with the drag. That's how browsers tear tabs off on
Wayland, and how the editor does.

## Talking to Wayland beside SDL

SDL owns the editor's Wayland connection, but doesn't start drags. A
Wayland client can have several *event queues* on one connection, so the
platform layer keeps one of its own: its own pointer listener (to learn
the serial number of the press a drag starts from: the compositor only
starts a drag for a press it knows is held), its own data device for
drags arriving, and the drag manager. SDL reads the socket; the platform
then hands its queue's events to its own listeners.

The catch: the compositor offers each drag to every data device of the
app, and listens to the last answer from any of them. SDL's device
politely declines anything that isn't a file or text, which undid the
platform's acceptance, so SDL is patched at fetch time to leave
Crucible's drag type alone. Finding that meant reading the compositor's
source (KWin's), since no specification says what happens with two
devices.

## Loading a library at run time

The editor shouldn't refuse to start on Windows, or on a Linux machine
without Wayland, just because it can use Wayland. So libwayland-client
is opened with `dlopen` when SDL turns out to run on Wayland, and each
function used is looked up by name. The protocol code that
`wayland-scanner` generates calls those functions by their usual names;
a macro per function points those names at the looked-up pointers. The
protocols' interface descriptions are plain data, generated into the
editor under names of its own, so nothing is looked up for them.

## Pixels and points

A window has two sizes: its pixels, and its coordinates, which the OS
scales for high-DPI screens. On Windows SDL's window coordinates are
pixels. On Wayland they're logical, so at 1.25× scale a pointer at
coordinate 100 is at pixel 125. Converting everything to pixels at the
platform layer means the UI divides by its scale once, the same
everywhere.

## Drawing your own window frame

A window's title bar and borders normally belong to the system: on
Windows and X11 the window manager draws them, and on Wayland the
compositor (or the app, when the compositor won't). Dropping them lets an
app put its menus in the title bar and make the frame part of its own
UI. In return the app does what the frame did:

- **Moving:** dragging the title bar must still move the window the
  system's way, so snapping to screen edges and tiling keep working. The
  app asks the system to start a move from the press that's still held:
  a caption press posted to the window on Windows, `xdg_toplevel.move`
  on Wayland, `_NET_WM_MOVERESIZE` on X11. The press itself is the app's
  to see first, which is what lets a double-click maximize.
- **Resizing:** the system asks the app, as the pointer moves, what's
  under it (a *hit test*): an edge or corner to resize, or the app's
  own content.
- **Buttons:** minimize, maximize, and close are the app's own, and so is
  the window menu on a right-click (the system draws that one when
  asked).

Some frames don't move the window the system's way at all: a floating
editor window's title bar starts a window drag of its own instead, so
other windows hear about it and it can dock where it's dropped.

