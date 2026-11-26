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

## More color (after the owner's try)

The owner found the palette readable but short on orange, and asked for
more color where it wouldn't cost readability. They suggested a title bar
whose top is orange, fading to its color. In an interactive mockup they
chose:

- **The title bar's glow:** the accent at 55% at its top, fading into the
  strip (`title-glow`). The menus and play buttons in it are now clear,
  so the glow shows through.
- **The same on active tabs:** the accent at 28% (`tab-glow`), the owner's
  suggestion.
- **Play mode in the accent:** at first the gaps between panels, the
  splitters, and the status bar turned orange while the game ran
  (`playing`), because edits made then are lost when it stops (made
  subtler after the second look, below). The shell marks every window's
  content with the class `playing` (`EditorWindow.ShowsPlaying`). Added
  on the recommendation: it's a warning, not decoration.
- **Left out for now:** a tint on the focused panel's tabs, and Add
  Component in the accent. Each is a line in Editor.crstyle.

**Gradients in the UI library:** `GradientFrom` and `GradientTo` (colors),
and `GradientDirection` (`Down` by default, or `Right`). They're styled
properties drawn over `Background`, interpolated in linear light. A clear
end takes the other end's color at no opacity, so a fade doesn't pass
through black, and `"GradientFrom": "@accent/55%"` alone fades into the
background. They're separate color properties, not one gradient value, so
tokens and opacities work on each end as on any color.

**After the owner's second look:**
- **The play colors were too loud.** Play mode is now only a faint glow
  along the status bar (`playing`, the accent at 25%). The owner wants to
  see it in use before deciding between that and nothing.
- **The menu titles were still grey.** The engine's default theme gives
  `MenuBar` a background; the editor's now clears it.
- **Fades end before the far edge:** `GradientLength`, a fraction of the
  element (1 by default), is where `GradientTo` is reached; the color
  holds after (the shader clamps). The title bar fades over its top 70%,
  active tabs over 75%, the status bar's glow over 80%.

**After the owner's third look:**
- **The owner's own tuning,** kept: active tabs' glow at 35%, and the
  title bar's, tabs', and status bar's fades over their full length (1,
  0.95, and 1).
- **Icons sat low in their buttons** (the owner saw it hovering the play
  buttons): `Icon` centered its text line, and Lucide's glyphs sit on the
  baseline in a line that reaches 1.09 em, so they landed about 0.09 em
  low (1.3px at 15px). Icons now center their glyph's ink, from the
  font's outlines (`textInk`, internal; HarfBuzz's glyph extents). Every
  icon in the editor moves with it.
- **No title in the bar:** the scene's name read as out of place, and with
  editors of their own open the scene isn't necessarily what's being
  worked on, so the owner chose to take it out (rather than move the play
  buttons). `Label.title` is collapsed in Editor.crstyle; `TitleBar.Title`
  is still kept up to date, so it's one line to bring back.
- **Where the title's parts went** (the owner's choice):
  - The Hierarchy's tab names the open scene ("Hierarchy · Village", the
    panel's name kept at the owner's request), with a dot in
    the accent while it has unsaved changes, and its path as the tooltip.
    For this, `EditorPanel` gains `HasUnsavedChanges` (a dot on any
    panel's tab, for extensions' editors too) and `TitleToolTip`.
  - The project's name is at the right end of the status bar
    (`StatusBar.Project`).
  - The system's window title (the taskbar, the window switcher) follows
    what the bar used to say, through a new platform call,
    `setWindowTitle` (SDL's).
  - Play state is already shown by the play buttons and the status bar's
    glow.

**Fixed in passing:** the Inspector's "Nothing selected" was centered on
the divider color, showing lighter blocks either side of it. It now spans
the panel's width and starts at the left, like the other placeholders.

## Consequences

- Games' themes can draw a border on some sides only (an underline, a tab
  edge, a card's accent). Code reading `BorderWidth` as a float reads a
  `Thickness` now. Setting it from a number still works.
- The palette lives in `Editor.crstyle`'s tokens: tuning goes on there,
  live, as before.
- `StyleTests` checks one width and widths per side, and gradients from
  tokens with a direction. `PlayChecks` checks that every window shows
  it's playing, and stops showing it after.
- Games' themes can use gradients the same way.
