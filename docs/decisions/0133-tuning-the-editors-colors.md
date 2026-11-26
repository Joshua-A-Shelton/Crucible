# 0133 — Tuning the editor's colors

**Status:** Accepted (2026-11-25); step 5 of the editor's look
(milestones). Names are placeholders until the owner settles them
(docs/naming.md).

## Context

After step 1 (ADR 0129) the owner found everything behaving but the
colors wanting work. Looked at again with the rest of the look in place,
three things bothered them:

- the selection (#6e2c08) read as a muddy rust block;
- the page, panels, strips, and fields were within a few steps of each
  other, so surfaces blurred and dividers vanished;
- fields were darker than their panel with no edge, so they read as holes.

The orange accent itself was kept.

## Options

**Palettes** (mocked up on the same slice of the editor): A, as it was; B,
lifted surfaces with a cleaner, lighter orange selection and fields lifted
above the panel; C, the same surfaces with a neutral grey selection marked
by an orange edge bar, and dark fields with a faint outline. **The edge
bar** (a theme could set only one border width): `BorderWidth` per side; a
new `EdgeBar` property for left bars only; no bar.

## Decision

Decided by the owner (2026-11-25):

1. **Palette C.**
   - Surfaces a step apart: page #121212, strips #171717, panels #1f1f1f,
     component headers #262626 (a new `header` token), dividers #353535.
   - Selection #3a3a3a, a neutral grey, with a 2px edge in the accent (a
     new `selection-edge` token), on the left of a row and under a tile.
   - Fields #181818 with a #3d3d3d outline (`edge`), in the accent when
     focused.
2. **`BorderWidth` per side, in the UI library:** a `Thickness`, like
   `Padding` and `Margin`. In a .crstyle it's one number for every side,
   or `[left, top, right, bottom]` (`[2, 0, 0, 0]`, a bar on the left). In
   C#, one number converts to a uniform thickness, so `BorderWidth = 1`
   still reads as before.

## How it works

- **Drawing:** a uniform border is stroked as before, following the
  corners. Sides of different widths are filled as straight strips along
  their edges, without the rounding (`Thickness.IsUniform` tells which).
- **Selected and focused:** a selected row keeps its bar when it has focus
  (`ListItem.selected:focus`); other focused rows keep the 1px outline.
- **Component headers:** a band across the section's full width
  (`DockPanel.component-header`). The fields below are inset by their own
  margin (`StackPanel.component-fields`), not the section's padding.
- **Trees on the panel:** the Hierarchy's and the asset folders' lists
  take the panel's color (`TreeView ListBox`), no longer the field's.
  Lists inside forms keep the field's.

## Consequences

- Games' themes can draw a border on some sides only (an underline, a tab
  edge, a card's accent). Code reading `BorderWidth` as a float reads a
  `Thickness` now. Setting it from a number still works.
- The palette lives in `Editor.crstyle`'s tokens: tuning goes on there,
  live, as before.
- `StyleTests` checks one width and widths per side.
