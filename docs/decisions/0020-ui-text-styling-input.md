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

## Amended (2026-11-03): exact-size and distance fields together

Before M6 step 1, exact-size atlases were compared against multi-channel
signed distance fields (MSDF, msdfgen) on the same HarfBuzz-shaped Noto
Sans text (release, RX 7900 XT, Vulkan):

| | Exact-size (FreeType, light hinting) | MSDF (32 px per em, range 4) |
|---|---|---|
| 11–16 px at 1× | crisper: stems and x-height on whole pixels | slightly soft and light |
| 2× DPI, 24 px and up | the same | the same |
| Scaled 0.8–1.2× | crisp only by rasterizing every size it passes; scaled bitmaps blur | crisp from one atlas |
| Turned | soft, slightly jagged | sharp |
| 95 ASCII glyphs | 5.7 ms for 1,520 (6 sizes × 3 scales); 1,253 KB | 69.5 ms for 95, once; 183 KB |
| GPU, 10,000 glyphs at 16 px | 0.042 ms | 0.054 ms |

**Decision (owner): both.** Exact-size atlases stay the default, for
static and small text; distance-field text is used for text that is large,
scaled, turned, or animated, chosen per style or element. msdfgen (MIT) is
added, built from its core alone and fed outlines by FreeType.

**Why:** each is clearly better at what the other does poorly, and the
cost of having both (a second glyph path and pipeline) is small. The
comparison also showed the exact path needs atlases that grow while the
game runs, since an unseen size means new glyphs.
