# 0124 — Icons and tooltips

**Status:** Accepted (2026-11-24); step 7c of editor extensions (the
owner's notes from map mode). Names marked `*` in docs/naming.md are
placeholders until the owner has tried it.

## Context

The owner's notes on map mode (open items, 2026-11-20 and 2026-11-24):
Stamp and Eraser put the brush's bottom-left cell under the pointer, so a
brush grows up and right of it; and the editor's buttons should have
images and tooltips, "not specific to the map mode", to make it look
nicer. The UI library had images but no tooltips, and the editor's few
symbols (the map tools, the play toolbar, the Inspector's small buttons)
were drawn by hand in code.

## Options

**The brush:** held where it was grabbed; centered. **Tooltips:** text on
any element; any content. **Images:** an icon set; the owner's art; drawn
in code. **Where:** the editor's toolbars; map mode only; everywhere
useful. **The icon set:** Lucide (ISC); Tabler Icons (MIT); Phosphor
(MIT); Material Symbols (Apache 2.0, a variable font only).

## Decision

Decided by the owner (2026-11-24):

1. **The brush is held where it was grabbed** (`MapEditor.BrushGrab`*):
   the cell its drag started on (in the Palette, picking up on the map, or
   the Select tool's region) stays under the pointer for Stamp and
   Eraser, flipping with the brush. Rectangle and Fill repeat it from the
   cell pressed, as before.
2. **Tooltips: text on any element** (`UIElement.ToolTip`*), shown near the
   pointer once it rests on the element for `UIRoot.ToolTipDelay`* (half a
   second), styled by the theme's `tooltip`* class. An element without one
   shows its nearest ancestor's. It's drawn in a layer of its own above
   the popups and takes no input or focus; a press, a key, the wheel, or
   leaving the element hides it, and it doesn't come back on that element
   until the pointer has left it.
3. **An icon set: Lucide** 1.79.0 (ISC, its icons from Feather MIT),
   checked in beside Noto Sans as its icon font (`Fonts/lucide.ttf`*) with
   its names (`Fonts/lucide-codepoints.json`*) and license
   (`Fonts/lucide-LICENSE.txt`*, `licenses/lucide.txt`*). An icon is one
   character of it, drawn by the UI library's new `Icon`* control
   (`IconFont`*, a font loaded in code, and `Glyph`*) in its `Foreground`, so
   icons follow the theme as text does and stay sharp at any size.
   (`IconFont` rather than the drafted `Font`, which every element already
   has for a font asset.) The editor's `EditorIcons`* gives extensions and
   itself icons by Lucide's names: `Icon(name)`*, `Button(name, toolTip)`*,
   `Toggle(name, toolTip)`* (lit while on, no box), `Has`*, `Glyph`*.
4. **Everywhere useful.** The Viewport's tools (`ViewportTool.Icon`*, null
   keeping an extension's title as text), World/Local, and Grid; the play
   toolbar (`PlayToolButton.IconName`*); map mode's tools, flips, and Show
   Collision; the Layers panel's buttons and each layer's eye and
   collision toggles; the Palette's add, flips, Clear, and Custom; the
   Inspector's fold, menu, and Open buttons (Open's editors named in its
   tooltip, which ADR 0120 put on the button for want of tooltips); the
   Build Settings' and Sprite Sheet Editor's row buttons. Buttons that are
   words (Done, Add Component, Apply, Build) stay text. A workspace's
   command can show one (`WorkspaceToolbar.Add(command, icon)`*).

## Consequences

- The hand-drawn symbols are gone: `PlayGlyph`, the play buttons'
  drawing, `IconButton.Icon` (now `IconButton(name, toolTip)`), and map
  mode's `TileGlyph`.
- Lucide's version is pinned by the checked-in files; updating it is
  copying a newer release's three files.
- The style system has no descendant selectors, so an icon keeps its own
  color when its button is hovered (`Icon.editor-icon`, `Icon.small`), and
  disabled play buttons dim by opacity.
- Checks that read buttons' text read icon buttons' tooltips
  (ViewportToolChecks, OpenWithChecks); TilemapChecks checks the grabbed
  brush, from the Palette and picked up on the map. ControlsTests check
  tooltips' timing and dismissal and the Icon control.
