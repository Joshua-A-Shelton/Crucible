# 0066 — The editor shell: windows, menus, docking

**Status:** Accepted (2026-11-04); built. Names settled by the owner
(docs/naming.md), the alternatives where offered: `DockSpace`,
`DockGroup`, `EditorPanel`, `Shortcut`.

## Context

M7 step 2. ADR 0016 makes the editor a C++ launcher running C# on
Crucible's own UI. It needs a main window with menus, dockable panels
(tabs, splits, a saved layout), and floating windows: panels torn off
into OS windows of their own, docked back by dragging (owner,
2026-11-04). The editor has 1:1 parity on Windows and Linux, Wayland
included (ADR 0008). That's the hard part. Wayland doesn't let an app
place its windows or see the pointer outside them, and SDL3 can't start
a drag.

Rendering had one global schedule (the scene's steps, Tonemap, then the
UI) that every surface ran.

## Options

**Several windows rendering:** per-surface schedules (each surface runs
a schedule of its own, today's becoming the default); or one schedule
whose scene steps skip surfaces without a world.

**Docking back on Wayland:** the same everywhere, tearing off by
dragging and docking back with a button; dragging back where the
platform allows it, with a button on Wayland; or no floating windows
yet. The owner chose dragging back everywhere, with Wayland extensions
if possible and a button only where not.

**Shortcuts:** keys by keycode, as the user's layout labels them (InputEvent
gains SDL's keycode); or keep scancodes.

**Where settings live:** the layout per project, in a git-ignored folder
of the project; or everything per user.

## Decision

1. **Per-surface render schedules.** A schedule is an object;
   `RenderSchedule.Default` is the scene's, and a new one holds only
   `Clear`. Editor windows render Clear and their UI. Step 3's viewport
   can render the scene into an offscreen surface shown in the UI.
2. **Windows drag back into each other everywhere.**
   - **Windows and X11:** SDL. The dragged window follows the pointer
     on the desktop.
   - **Wayland:** the platform's own small Wayland connection beside
     SDL's, using `xdg-toplevel-drag-v1`. That's a staging protocol
     made for browser tabs: a toplevel attached to a drag-and-drop
     follows the pointer.
   - **Three tiers on Wayland:**
     1. the protocol: `Full`;
     2. plain drag-and-drop, where drop targets work but the window
        doesn't follow: `DropOnly`;
     3. a dock-back command: `None`.
   - **Dependencies,** approved by the owner and all MIT:
     libwayland-client, loaded at run time; wayland-scanner, at build
     time; the protocol's XML, vendored.
3. **Keycodes for shortcuts:** `InputEvent::keycode`. Shortcuts and the
   text fields' Ctrl keys match it; navigation keys stay on scancodes.
4. **All settings per user:** `editor.json` in the preferences folder,
   holding the recent projects and one layout for every project.

## Details

- **The platform:**
  - **Events:** `DragMoved` (the window under the pointer, 0 for none of
    ours, and the position in it), `DragDropped`, `DragCanceled`, and
    `FileDialogResult` (its path in the polled text).
  - **Window drags:** `windowDragSupport`; `startWindowDrag(window,
    draggedWindow, offset)`, started from a press still held;
    `stopWindowDrag`.
  - **Placement:** `windowPosition` / `setWindowPosition` /
    `pointerScreenPosition`, which give zero and do nothing on Wayland.
  - **The rest:** `showOpenFileDialog` (SDL's, through the portal on
    Linux; SDL's dialog subsystem is on now) and `preferencesPath`.
  - **Pointer positions** in input events are physical pixels on every
    platform now. SDL's are logical on Wayland, which the UI's divide by
    its scale had assumed weren't.
- **Wayland (`PlatformWayland.cpp`):**
  - **The connection:** SDL's display, our own event queue, read after
    SDL reads the socket.
  - **Its objects:** a pointer (for the press serial a drag starts from,
    which SDL doesn't expose), a data device (for drags over our
    windows), and the toplevel-drag manager.
  - **The drag:** a data source of type
    `application/x-crucible-window-drag` (no data moves; the process
    knows what's dragged), with the window attached before the drag
    starts, as the protocol asks.
  - **Generated code:** the protocols' interface descriptions are our
    own, generated with every name prefixed `crucible_`, so they never
    clash with SDL's, and only libwayland's functions are looked up.
  - **The SDL patch:** both data devices are offered every drag, and the
    compositor takes the last accept or action change from any of them
    (read in KWin's source). SDL declines anything but files and text,
    so it would cancel our drops. `cmake/PatchSdl.cmake` makes SDL leave
    our type alone. Recheck it on every SDL update.
  - **Checked on KWin 6.8.1:** the connection finds `Full`.
- **Schedules:**
  - natively, by ID (0 the default), prepared and released with the
    renderer like the default;
  - `setSurfaceSchedule`, falling back to the default if a schedule is
    destroyed;
  - C#: `RenderSchedule.Create`, `Default`, `SetSurfaceSchedule`, and
    `Dispose`; the static edits became the default's
    (`RenderSchedule.Default.InsertAfter(...)`).
- **Menus and commands** are in `Crucible.UI`, for games too:
  - **`Command`** (`Id`, `Title`, `Execute`, `CanExecute`, `Shortcut`,
    `IsChecked`) and **`CommandRegistry`** (`HandleShortcut`, for keys
    no focused element used).
  - **`Shortcut`** (`Ctrl+Shift+S`, by keycode).
  - **`MenuBar`, `Menu`, `MenuItem`** (with a check, a shortcut, a
    submenu) and **`MenuSeparator`, `ContextMenu`**.
  - **The pointer:** a click opens a bar's menu; hovering switches menus
    while one is open; hovering an item opens its submenu.
  - **Keys:** up and down move; right opens a submenu; left closes it;
    in a bar's menu, left and right step along the bar; Escape closes.
  - **Refreshing:** opening a menu shows each command's state now.
  - **Popups** gained `Position`, for context menus and submenus, and
    close as a chain when they're menus: a press in a menu below closes
    the ones above and acts there.
- **The editor (`Crucible.Editor`, JIT only):**
  - **The launcher:** `crucible_editor` is the JIT launcher defaulting
    its game to `Crucible.Editor.dll`, beside the engine's assemblies or
    in the build tree's artifacts. `--project <file>` opens a project.
  - **`EditorWindow`:** a root, a schedule of its own, and a
    `DockSpace`; the main one has a menu bar (File, Edit, Window, Help)
    and a `StatusBar`.
  - **`DockSpace`:** a tree of **`DockSplit`**s (weights between
    **`Splitter`**s, with a minimum of 48 units) whose leaves are
    **`DockGroup`**s of **`DockTab`**s over **`EditorPanel`**s.
  - **Docking at a group:** its middle half joins its tabs; nearer an
    edge splits it (`DockTarget`). A split going the same way gains a
    sibling instead of nesting. An emptied group goes, and a split left
    with one child gives way to it.
  - **Dragging a tab:**
    - within a window, by the UI's pointer capture, with a translucent
      preview of where it lands;
    - out of the window, the panel tears off into a floating window that
      the platform then drags;
    - a floating window's only panel drags the window itself;
    - dropped on another window, the panels dock there and the floating
      window closes;
    - closing a floating window docks its panels back. So does the
      tab's right-click menu, which also offers Close. Each tab has a
      close button.
  - **Panels:**
    - registered by ID in a **`PanelRegistry`**;
    - placeholders until their steps: **`HierarchyPanel`**,
      **`InspectorPanel`**, **`ViewportPanel`**, **`AssetsPanel`**;
    - **`LogPanel`** shows the editor's messages;
    - the Window menu checks and toggles each; Reset Layout restores the
      default.
  - **`EditorSettings`:**
    - saved on exit, atomically, and on opening a project;
    - the layout is the main tree and each floating window's size,
      position (where known), and tree;
    - unknown panels are skipped, and nothing usable gives the default.
  - **Opening projects:** File › Open Project… (Ctrl+O, the system's
    dialog) or Recent Projects. Undo and Redo wait for step 4's undo
    model.
  - **Its font:** Noto Sans ships beside the editor.

## Why

Per-surface schedules make each window pay only for what it draws, and
give the viewport a natural shape: a scene schedule on an offscreen
surface. Dragging between windows is what the owner asked for. On
Wayland the toplevel-drag protocol is the only way to do it. KWin (6.4
on) and GNOME (48 on) have it, and wayland.app lists many wlroots
compositors too; the drop-only and command tiers keep the rest working. A connection of our own beside SDL's, rather
than reaching into SDL's internals, keeps the patch to SDL to five lines.
Keycodes put Ctrl+Z on the key marked Z on every layout. One layout per
user is the owner's choice.

Given up: zero patches to SDL, and stable ground. The protocol is in
testing and may change (as a new version). The patch needs rechecking
with every SDL update.

## Not yet

- Windows draws no drop shadow around undecorated windows (DWM can be
  asked for one); left for the custom decorations' styling (owner).
- Floating windows on Wayland reopen wherever the compositor puts them
  (apps can't place windows there).
- The menu bar has no Alt access key, and menus no access letters.
- Docking only offers a group's sides: no docking against the whole
  space's edges.

## As built

- **Native:**
  - `Platform.h`: drag events, `FileDialogResult`, `keycode`,
    `WindowDragSupport`, and the functions above.
  - `PlatformWayland.cpp` and `PlatformInternal.h`; `cmake/PatchSdl.cmake`
    and `cmake/WaylandProtocol.cmake`;
    `third_party/wayland/xdg-toplevel-drag-v1.xml` (wayland-protocols
    1.45).
  - `createRenderSchedule`, `destroyRenderSchedule`, and the steps'
    functions taking a schedule; `setSurfaceSchedule`; the Clear step;
    `crucible_editor`.
- **C#:**
  - `Keycode`; `KeyEvent.Keycode`; `RenderSchedule` as an object.
  - `Crucible.UI`'s `Shortcut`, `Command`, `CommandRegistry`, `Menu`,
    `MenuItem`, `MenuSeparator`, `MenuBar`, `ContextMenu`;
    `Popup.Position`.
  - `Crucible.Editor`; `Crucible.Assets` internals visible to the
    editor (its atomic write).
- **Tests:**
  - **native:** keycodes, drag events in order, drags needing a
    platform, new schedules holding Clear and their own steps, and,
    opt-in on a Wayland desktop (`CRUCIBLE_TEST_WAYLAND=1`), the drag
    support found (`Full` on KWin 6.8.1);
  - **C#:** `MenuTests` (JIT and NativeAOT) covers shortcuts, bars,
    hover switching, keys and submenus, presses in menus below, and
    context menus; `ShortcutsFollowTheLayoutsLabels` in
    `TextFieldTests`;
  - **`DockingTests`** (`Crucible.Editor.Tests`, JIT) covers docking
    beside and among tabs and collapsing, drop targets, tab drags with
    the preview, splitters, closing and reopening panels, layouts and
    settings round-tripping, and shortcuts;
  - **`launcher.jit.editor`:** the editor headless checks itself (the
    layout drawn, a menu opened, a tab dragged with the preview, a
    tear-off into a floating window drawn, closing it docking back),
    saving `editor.png`.
- **By hand** (a real desktop: Wayland on KWin, and Windows): tear a
  panel off, drag it back onto another group, and dock a floating window
  back by closing it. With the debug preset built:

  ```bash
  cd /path/to/Crucible && build/debug/src/native/launcher/crucible_editor --managed-dir build/debug/artifacts/bin/Crucible.Engine/debug --project samples/UiShowcase/Project/UiShowcase.crproject
  ```

  `CRUCIBLE_DEBUG_DRAGS=1` prints the drag's events as the compositor
  sends them (enter and leave with the window each maps to, the drop, and
  how the drag ended) and as the editor reads them.
- **Tried by the owner on KWin 6.8.1 (2026-11-04):**
  - a tab torn off, followed by its window;
  - the blue preview over the main window;
  - docked on release;
  - a floating window restored from the saved layout, dragged back by
    its tab, and docked.

  One drag back by the title bar didn't dock (above). Windows isn't
  tried by hand yet.

## Amendment: the editor draws its own window frames (2026-11-04)

**Context.** Tried by hand on KWin, a floating window dragged back by its
title bar didn't dock. The title bar was KWin's, which moves the window
without telling the app (on Wayland apps hear nothing of moves).

**Options.** Docking only by a floating window's tab everywhere; noticing
title-bar moves where the platform tells apps (Windows, X11), breaking
parity; or undecorated windows drawing their own title bars, which the
owner meant to do anyway.

**Decision** (owner):
1. **Undecorated windows** with the editor's own title bars.
2. **The main window's menus inside its title bar.**
3. **Floating windows' tab strips as their title bars** (a single
   group's strip; with several groups, a slim title bar above them).
4. **Minimize, maximize, and close on every window.**
5. **The shadow on Windows later.**
6. **Moves are the editor's own `startWindowMove`,** not SDL's hit test,
   which on Wayland and X11 swallows a press in a move area: the title
   bar would never see a double-click or a right-click there. That's a
   real system move everywhere (snapping and tiling kept):
   - Windows: a caption press, `WM_NCLBUTTONDOWN`;
   - Wayland: `xdg_toplevel.move` through the platform's connection;
   - X11: `_NET_WM_MOVERESIZE`, with libX11 (X11 license, MIT-style)
     loaded at run time, as SDL does.

   SDL's hit test only resizes, from a border 6 units wide.

**Details.**
- **The platform:** `setWindowBordered`, `setWindowResizeBorder`
  (pixels; the hit test carries the width in its data pointer),
  `startWindowMove`, `minimizeWindow`, `maximizeWindow`, `restoreWindow`,
  `windowMaximized`, and `showWindowMenu` (the system's window menu,
  `SDL_ShowWindowSystemMenu`). They're in `PlatformWindows.cpp`,
  `PlatformX11.cpp` (Crucible's headers before Xlib's, whose macros
  clash), and `PlatformWayland.cpp` (xdg-shell's client header, generated
  like the others).
- **`TitleBar`** (`Crucible.UI`):
  - its `Leading` content on the left, the `Title` centered on the whole
    bar (in the room left when it doesn't fit), and three
    **`WindowButton`**s (`WindowButtonKind`; classes `minimize`,
    `maximize`, `close`);
  - its empty parts and title: a press moving 4 units starts the
    system's move, a double-click maximizes or restores, and a
    right-click shows the window menu;
  - presses on the leading content (the menus) or buttons stay theirs;
  - `OnMoveStarted` lets a window move otherwise;
  - minimize and maximize act on the root's window; close is the
    owner's (`Activated`);
  - the maximize button draws two squares while maximized;
  - its default theme styles: 32 units high, buttons 44 wide, close red
    under the pointer.
- **The editor:**
  - **The main window:** its title bar holds the menus, its title is
    "<project> — Crucible", and close quits.
  - **A floating window with one group:** the group's tab strip ends in
    the window's buttons, and its empty part works as a title bar.
  - **With several groups:** a slim title bar above them.
  - **Dragging a floating window's frame** drags the window, with
    `startWindowDrag`, so it docks where it's dropped. Where the
    platform's drags can't move it (`DropOnly`, `None`), the system
    moves it instead.
  - **Close** docks a floating window back.

**As built.**
- **Tests:**
  - **native:** the frame calls are safe (the offscreen driver has no
    frames);
  - **`TitleBarTests`** (JIT and NativeAOT, the window calls recorded):
    layout, moving, double-click, right-click, buttons;
  - **`DockingTests`:** the main title bar and close, a floating
    window's frame following its tree;
  - **`launcher.jit.editor`:** the title bar's menus and buttons drawn,
    the floating window's buttons on its strip.
- **By hand:** to be tried on KWin and Windows. Moves, resizing from the
  edges, double-click, the window menu, and docking by a floating
  window's strip all need a real desktop.

**Fixed after trying it on KWin (2026-11-04):**
- **Tearing a panel off sent the main window behind other apps'
  windows:** the floating window was an unrelated toplevel. Floating
  windows are now children of the main window (`setWindowParent`, SDL's
  `SDL_SetWindowParent`). They stay above it, follow it between desktops
  and when minimized, and on Windows leave the task bar to it.
- **After dropping a floating window outside, then moving and dropping
  it outside again, it sometimes couldn't be grabbed again.** Two causes:
  - the platform's pointer forgot its surface on any leave, so a leave
    arriving after the next window's enter (as one does when a window
    under the pointer is destroyed) left every later press refused. A
    leave now only forgets the surface it names, and a press on an
    unknown surface is left to the compositor to judge;
  - a compositor quietly ignores `start_drag` for a stale press, which
    would leave the drag waiting forever. A round trip after starting
    ends a drag the compositor never answered (kitty's approach).
- **Tried again on KWin:** repeated tear-offs, drops outside, and grabs
  by the strip and the tab, with two dock-backs; nothing stuck.

