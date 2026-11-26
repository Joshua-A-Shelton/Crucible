# 0130 — Layout and clipping

**Status:** Accepted (2026-11-25); step 2 of the editor's look
(milestones), tried by hand by the owner. Names settled by the owner as
drafted (2026-11-25; docs/naming.md).

## Context

The editor wasted a strip on three play buttons, and text that didn't fit
was cut off or wrapped mid-word: "Orthograph / ic Height" in the
Inspector, an asset path running under a drop-down's arrow, Build
Settings' rows overflowing. Two small fixes from M7 were open: number
fields showing "7836" for 17.7836 (scrolled to the end), and a placed
model landing anywhere in the Hierarchy.

## Options

**The title row:** play centered with the title after the menus; play
after the menus with the title centered; play centered with no title.
**Overflow:** an ellipsis, cut in the middle for paths; an ellipsis at the
end only. **Field names:** an ellipsis and a resizable column; an ellipsis
only; wrapping at words. **Fields not being edited:** their start with an
ellipsis; their start only. **The Hierarchy's order** (found building
it): roots get a real order; creation order only; later.

## Decision

Decided by the owner (2026-11-25):

1. **Play centered in the title row** (`TitleBar.Center`): content
   centered on the window between the leading content and the buttons;
   with it, the title follows the menus, cut with "…" where it meets it.
   The play toolbar's strip is gone.
2. **Trimming in the UI library** (`TextTrimming`: `None`, `End`, `Path`;
   `Label.Trimming`, styled; `Label.IsTrimmed`): `End` cuts and adds "…";
   `Path` keeps the last `/` segment whole ("Tilemaps/…/Tilemap
   4.crtilemap"), cutting at the end when even that doesn't fit. A cut
   label without its own tooltip shows its whole text as one. The editor
   styles it on tabs, list and tree rows, drop-downs (as paths), the
   status bar, asset names, the breadcrumb, Build Settings' rows, and the
   Inspector's title.
3. **The Inspector's names** end in "…" with their tooltip, and the name
   column's edge drags its width (`InspectorPanel.NameWidth`), one width
   for every row, kept in editor.json (`"InspectorNameWidth"`).
4. **Text fields not being edited** show their start, "…" where they're
   cut; editing scrolls as before.
5. **Top-level entities keep an order** (`RootOrder`, a component that
   isn't saved: the file's order is the order). Scene IDs are random, and
   roots had been listed and saved by them, so a placed model landed
   anywhere. Loading puts a scene's roots after the world's in file order;
   an instance made at the top goes last; saving writes this order; the
   Hierarchy lists it, and dragging a root between roots reorders it, one
   undo step. Entities game code makes come after, by scene ID.

## How it works

- **Cutting** happens when a label is arranged narrower than its text:
  the caret nearest the room left (less the ellipsis) is found in the
  shaped text, the text before it kept, and the cut text shaped once and
  kept until the room or the text changes (`TrimmedText`, shared with the
  text fields). Kerning across the cut can make it a little wider, so a
  character more comes off until it fits.
- **The title bar** centers its center content on the window, clamped
  between the menus and the buttons, as it did the title.
- **The name grip** is a 5-unit strip after each name (`name-grip`);
  dragging it sets the Inspector's width, which every row shown follows.
- **Root order** is renumbered 0 to n-1 when a root is inserted (cheap at
  editor scale). Undo and redo of creating, deleting, duplicating, and
  moving carry a root's index as they do a child's.
- **Styled properties of controls** (Label's, TextField's) are now
  registered before a theme file is read, so a theme can set them before
  any such control exists.

**Added while building:** `RootOrder` and its `RootsOf`, `IndexOf`,
`Insert` (asked before building them); `Label.ShownText` (the text as
drawn, for tests and tools); the Inspector check now picks a top-level
house (it had picked the first "House" in query order, which the new
component changed).

## Consequences

- Text cut anywhere in the editor shows a tooltip with the whole of it;
  games can opt their labels in with one style.
- Scene files written by this version list roots in the editor's order;
  older files load in their order (by scene ID) once, then keep it.
- Not yet: middle trimming for text other than paths; multi-line labels
  trimmed after a number of lines.
