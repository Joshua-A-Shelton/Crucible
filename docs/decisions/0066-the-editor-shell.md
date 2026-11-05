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

- **A floating window moved by its title bar doesn't dock.** Moving a
  window by its frame is the window manager's job, and on Wayland the
  app hears nothing of it, so floating windows dock by dragging their
  tab. Windows and X11 could notice a title-bar move ending over another
  window, but only those platforms, which the parity rule (ADR 0008)
  argues against; undecided.
- Floating windows on Wayland reopen wherever the compositor puts them
  (apps can't place windows there).
- Floating windows aren't children of the main window, so they show in
  the task bar.
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
