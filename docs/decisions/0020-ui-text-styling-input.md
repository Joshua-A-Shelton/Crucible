# 0020 — UI system: text, styling, input

**Status:** Accepted (2026-10-25).

## Context

ADR 0019 set the UI core: a retained C# element tree, native text, Latin
text in v1 with non-Latin after v1 (shaping behind an interface). Goal 5
(clarity) applies to UI and text too.

## Options

**Shaping** (string to positioned glyphs; Latin still needs kerning and
ligatures):

- **HarfBuzz** ("Old MIT" license): the standard shaper (browsers, Godot,
  Unreal), correct for every script; a sizable C++ dependency.
- **Simple kerning from font tables** (e.g. stb_truetype): enough for
  Latin, tiny, but replaced once non-Latin text arrives.

**Rasterization:** exact-size glyph atlases are the sharpest; distance
fields (SDF/MSDF) scale and animate freely but are softer at small sizes.

| Option | License | Quality |
|---|---|---|
| FreeType | FTL (BSD-style, asks for a credit in product documentation) or GPLv2 | Best, with hinting at small sizes |
| stb_truetype | Public domain / MIT | Good antialiasing, no hinting |
| msdfgen | MIT | Distance fields, as an addition |

## Decision

1. **Shaping uses HarfBuzz** from the start, behind the shaping interface.
2. **Rasterization uses FreeType,** into exact-size glyph atlases by
   default (used under the FTL, not the GPL). Distance-field text for
   large, scaled, or animated text may be added later.
3. **Styling:** themes made of style classes with a small selector set
   (element type, class, state: hover, focus, pressed, disabled) and
   properties such as colors, borders, padding, fonts, and transitions. No
   full CSS cascade. The editor's theme and a game's theme are just
   different style sets.
4. **Input and navigation:** events route from the element under the
   pointer up through its parents; keyboard focus and tab order; gamepad
   directional navigation that moves focus spatially, with explicit
   per-element overrides; focus scopes that trap focus in modal dialogs and
   menus.
5. **DPI:** layout in logical units scaled by the monitor's scale factor,
   with every element's final rectangle snapped to whole pixels.
6. **Animation in v1:** property transitions and tweens only.
7. **Accessibility (screen readers) is after v1, and low priority** even
   then: Crucible's limited scope and audience (ADR 0009) make it unlikely
   to matter.

## Why

HarfBuzz avoids building a shaper only to replace it when non-Latin text
arrives (ADR 0019). Exact-size rasterization is the clarity choice, and
FreeType's hinting keeps the editor's small, dense text sharp; its FTL
credit line is acceptable (a license screen is nice but not a goal, ADR
0007). A small selector set keeps styles predictable. Pixel snapping
avoids blurry edges on half pixels (goal 5). Directional navigation and
focus scopes are what gamepad-driven RPG menus need.

Given up: CSS's full power, and distance-field text by default.
