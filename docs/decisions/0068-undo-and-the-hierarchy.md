# 0068 — Undo and the hierarchy

**Status:** Accepted (2026-11-04); part a built (the inspector, part b,
follows). Names settled by the owner as drafted; those added while
building are placeholders (docs/naming.md).

## Context

M7 step 4: a tree of the scene's entities, an inspector, undo and redo
across the editor, and saving the scene. The viewport (ADR 0067) already
moved entities, with nothing to take a move back, and the editor could
open scenes but not save them. Entities had no names: the scene format
saved only components. The owner split the step in two: **4a**, the
hierarchy, undo, and saving; **4b**, the inspector from generated
metadata.

## Options

**Undo:** commands (each edit a class that does and undoes itself); or
edit records (what changed, before and after, recorded once it's done); or
snapshots (the whole scene saved before each edit).

**Names:** a saved component; or a name kept by the editor beside the
scene.

## Decision

1. **Edit records,** each naming entities by scene ID, never by entity
   handle:
   - a component's value as JSON, before and after (null for absent);
   - a subtree made or deleted, kept as a scene file with one root and
     its place (parent and index);
   - an entity moved to another parent or place, with its local
     transform before and after.
2. **Groups:** `BeginGroup` and `EndGroup` make a gesture (a handle drag)
   one step; inside a group, a component's edits of one entity merge.
3. **`EntityName`,** a saved component with one string, `Value`. Entities
   without one are labeled by their prefab's file name, else their main
   component, else "Entity".
4. **Two parts:** 4a here, 4b next.

## Details

- **Why scene IDs:** undoing a delete brings entities back as new
  handles. Steps that came after the delete still find them by scene ID,
  which the restore keeps.
- **The history (`UndoHistory`):**
  - steps in order made; those undone, which a new step clears;
  - the newest 500 kept;
  - `IsDirty`: whether the newest step done is the one done when last
    saved (an open group with steps in it counts as a change).
- **Steps (`UndoStep`, internal ones in `SceneEdits.cs`):**
  - `ComponentEdit` writes a saved component from JSON through the
    engine's own serializers. That's why the engine now gives the editor
    its internals (`InternalsVisibleTo`), as the asset library already
    did.
  - `SubtreeEdit` captures with `Prefab.Save` and restores with
    `Scene.Load`, keeping scene IDs; a prefab instance is kept as an
    instance.
  - `ParentEdit` keeps the world transform: the new local transform is the
    world matrix times the new parent's inverse.
- **The scene document (`Shell.Scene.cs`):**
  - its file, its asset ID, its history;
  - the title shows "Village.crscene* — Diorama — Crucible" while
    there are unsaved changes;
  - **New** (Ctrl+N) clears the scene; **Save** (Ctrl+S) writes it, or
    asks where first; **Save As** (Ctrl+Shift+S) uses the platform's
    save dialog (`showSaveFileDialog`), starting in the project's
    `Assets/Scenes`;
  - with unsaved changes, New, Open Scene, Quit, the main window's close
    button, and the OS asking to quit open a dialog: **Save**, **Don't
    Save**, **Cancel**. Saving through Save As goes on once the file is
    written; canceling the save dialog cancels what was waiting.
- **Edits (`Edit` menu, and the hierarchy's right-click menu):**
  - **Rename** (F2) edits the row's text in place; Enter or leaving keeps
    it, Escape doesn't; an empty name removes `EntityName`.
  - **Duplicate** (Ctrl+D): the subtree loaded again with new scene IDs,
    just after the original.
  - **Delete** (Delete).
  - **Create Empty** (Ctrl+Shift+N, or under the row right-clicked): a
    transform and the name "Empty".
  - Moving, deleting, and duplicating part of a prefab instance below its
    root are refused with a message: ADR 0045's overrides change
    components and add children, but can't remove or move the prefab's
    own entities.
- **Handle drags** are a group named by the handle (Move, Rotate, Scale).
- **The hierarchy panel:**
  - roots ordered by scene ID, the order scenes are saved in; children in
    their order;
  - selection goes both ways: a pick in the viewport expands the row's
    parents and scrolls to it;
  - dragging a row onto another: the top quarter of a row puts it before,
    the bottom quarter after, the middle inside (as its last child). Among
    roots, only "to the top" means anything, since roots have no order;
  - read again a frame after the scene changes.
- **Model prefabs** now give each node its `EntityName` from the model's
  node names, so the parts of an instance are named in the hierarchy (and
  instances don't save them: they match the prefab).
- **New UI (`Crucible.UI`):**
  - `Popup.IsModal`: a press outside is used up and doesn't close it;
  - `Dialog`: a modal popup with a message and buttons, raising `Chosen`
    with the button's index, or -1 for Escape;
  - `TreeView`, `TreeRow`, `TreeExpander`: a `ListBox` over the nodes in
    view, so it's virtualized the same way. Nodes are `ulong` keys the
    caller chooses. Selection follows focus. Right expands then goes in,
    Left goes out then collapses. Rows rename inline and drag onto one
    another, with a line or an outline showing where.

## Why

Edit records are small, and the same three kinds cover everything the
hierarchy and inspector change, including components the editor has never
heard of, through the JSON the scene format already writes. Commands
would need a class per kind of edit; snapshots cost the whole scene per
step. Scene IDs were already each entity's lasting name in files (ADR
0041), so steps use them too. A saved name component keeps names in the
scene, where games can read them.

## Not yet

- Roots can't be reordered: a world keeps no order of roots, and saving
  orders them by scene ID.
- Selecting several entities, and so deleting or dragging several.
- Part b: the inspector, and its edits as component steps.
- Undo of edits outside the scene (layouts, settings): not undone.

## As built

- **Native:** `Platform.h`'s `showSaveFileDialog` (answered by
  `FileDialogResult`, empty when canceled).
- **Engine C#:** `EntityName` (saved); `Popup.IsModal`, `Dialog`,
  `TreeView`, `TreeRow`, `TreeExpander`, `TreeDrop`, `TreeDropPosition`;
  `ListBox` fixed for zero items; model nodes named.
- **Editor:** `UndoHistory`, `UndoStep`, `HierarchyPanel`, `SceneIds`,
  `ComponentEdit`, `SubtreeEdit`, `ParentEdit`; the shell's `History`,
  `SceneId`, `IsDirty`, `SceneName`, `NewScene`, `SaveScene`,
  `SaveSceneAs`, `WriteScene`, `ConfirmDiscard`, `RequestQuit`, `Undo`,
  `Redo`, `Rename`, `CreateEmpty`, `Duplicate`, `Delete`, `Reparent`,
  `LabelOf`; commands `File.NewScene`, `File.SaveScene`,
  `File.SaveSceneAs`, `Edit.Rename`, `Edit.Duplicate`, `Edit.Delete`,
  `Hierarchy.CreateEmpty`, and `Edit.Undo` and `Edit.Redo` now enabled.
- **Tests:**
  - **`TreeViewTests`:** rows for expanded nodes, the arrows, refreshes
    keeping state, expanders, drops before, after, and inside but never
    into the dragged node, right-clicks, renaming and Escape, and modal
    dialogs;
  - **`UndoTests`:** order, groups, merging, the saved state, the limit;
  - **`HierarchyTests`:** component edits as JSON, subtrees deleted and
    brought back with their IDs and place, moves keeping the world
    transform;
  - **`launcher.jit.hierarchy`:** the village listed by prefab names,
    selection both ways, F2, Create Empty, a drag onto a house, Delete,
    Duplicate, a grouped drag, each undone and redone; saved beside the
    capture and loaded back; the unsaved-changes dialog's Cancel and Don't
    Save; saved as `hierarchy.png` mid-drag.
- **Tried by the owner on KWin (2026-11-04):** everything worked except
  that a few rows showed no name until renamed. A row scrolled out of
  view is detached, and its label lets go of its shaped text; reused for
  the same text, nothing asked it to shape again. `Label` and `TextField`
  now measure again when attached (`TreeViewTests` scrolls rows of
  repeated text away and back; `launcher.jit.hierarchy` checks every
  row's text is shaped, with every top expanded).
