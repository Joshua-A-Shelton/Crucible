# 0120 — Opening editors

**Status:** Accepted (2026-11-24); step 5 of editor extensions, placed by
the owner before viewport tools, tried by hand by the owner. Names settled
by the owner as drafted (2026-11-24; docs/naming.md).

## Context

A workspace (ADR 0119) opened only from its extension's own code: a menu
command, a toolbar button. The editor's own editors were each wired by
hand: map mode from an Edit Map button in the Inspector and an item in
the Hierarchy's menu, the Sprite Sheet Editor and the tileset Palette from
a double-click in Assets. Nothing said which editor goes with which
asset or component, so an extension's editor couldn't be opened from
them, and a tilemap file's double-click did nothing. The owner wants
editors for single assets and components opened from where those are
seen, keeping menus for editors of bigger things.

## Options

**When:** now, before viewport tools; with custom inspectors (step 8).
**The Inspector:** a button on the component's header; its right-click
menu; double-clicking the header. **The Hierarchy:** double-click opens
its editor; right-click only; double-click frames it. **Several editors
for one kind:** the first registered, the rest in Open With; ask on each
double-click; a choice remembered. **Where a choice is remembered:** the
person's editor settings; the project file. **Undoing it:** from Open
With; a settings window. **A tilemap file:** find the entity using it;
always ask.

## Decision

Decided by the owner (2026-11-23):

1. **Now, step 5.** The steps after are renumbered; custom inspectors
   (now step 9) keep drawers.
2. **Editors say what they open.** `editor.Workspaces.OpensAssets(id,
   kinds…)` (importers' names, "Texture", or file extensions, ".crtilemap")
   and `editor.Workspaces.OpensComponent<T>(id)`. The editor's own
   register the same way inside it: the Sprite Sheet Editor textures, the
   Palette tilesets, map mode `.crtilemap` files and the `TilemapLayers`
   and `Tilemap` components.
3. **Assets: double-click and right-click.** A double-click opens the
   editor; the right-click menu has "Open in Sprite Sheet Editor" for
   one, **Open With** ▸ for several. Folders and scenes open as before.
4. **The Inspector: a button and right-click.** A component's header has
   an Open button when an editor opens it, and "Open in Map" items in
   its right-click menu. The UI has no tooltips, so the button says where
   it opens, "Open in Map", or **Open…** with several, rather than
   "Open" with a tooltip (changed while building). It replaces the Edit
   Map button.
5. **The Hierarchy: right-click only.** "Open in Map" (and extensions')
   in its menu, replacing Edit Map; double-clicking an entity does
   nothing new.
6. **Several: ask, or the one always chosen.** With several editors, a
   double-click (or the Inspector's button) asks "Open Grass.png with:",
   with **Always Open With** to remember the answer for that kind (or
   component). Open With checks the remembered one and offers **Ask Each
   Time** to forget it. Remembered in the person's editor settings
   (`"OpenWith"`), not the project.
7. **A tilemap file opens through its entity.** Map mode edits a map
   entity, so a `.crtilemap` opens map mode on the one entity of the open
   scene using it; with several, "Which map uses Level 1.crtilemap?";
   with none, "No entity in Walk.crscene uses Level 1.crtilemap" in the
   Log.

## Consequences

- An editor for something bigger than an asset or a component (a whole
  level's lighting, say) still opens from a menu command.
- The question reuses the dropped files' (ADR 0115), its checkbox's text
  given.
- `OpenWithChecks` (CTest `launcher.jit.open-with`) runs an extension's
  workspace opening textures and transforms beside the Sprite Sheet
  Editor: the question, Always Open With, Open With, Ask Each Time, and
  the Inspector's button. The map checks open a map's file, and one no
  entity uses; the sprite sheet checks find Open in Sprite Sheet Editor.
