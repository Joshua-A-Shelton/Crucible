# 0057 — Fonts and text

**Status:** Accepted (2026-11-03). Names settled by the owner (docs/naming.md).

## Context

ADR 0020, amended, keeps two glyph paths: exact-size FreeType atlases by
default, and multi-channel distance fields (msdfgen) for text that is
large, scaled, turned, or animated. Shaping and rasterization are native,
behind a shaping interface (ADR 0019), with Latin text in v1. M6 step 1
decides W: the font asset, atlas management, and the text API between C#
and native code. Drawing comes with steps 2 and 3.

## Options

**When distance-field glyphs are made:** on demand at run time, on the
job system (msdfgen ships); at import, a chosen character set baked into
the font file (msdfgen stays in the editor, characters outside the set
can't be drawn); or both.

**Atlases:** fixed-size pages, shelf-packed, evicting whole pages; or
per-glyph eviction, which needs a 2D allocator that frees.

**What C# gets from shaping:** a handle to a layout native code keeps; or
positioned glyphs copied into C# arrays.

**Line breaking:** a Latin subset of UAX #14; or all of it now.

## Decision

1. **The font asset** is the font file itself (`.ttf`, `.otf`), imported
   by `FontImporter` into a `.crfont` (kind `FONT`): its bytes in a `DATA`
   section. One asset is one face; bold and italic are separate assets,
   with no synthetic styles or variable fonts in v1.
2. **Glyphs are made on demand** at run time. Exact glyphs are rasterized
   immediately, on the calling thread (about 4 µs each). Distance-field
   glyphs are generated on the job system (about 0.7 ms each) and appear a
   frame or two later.
3. **Atlas pages:** 1024² per kind (R8 coverage, RGBA8 distance fields),
   shelf-packed. A full set of pages gets another page, up to a limit;
   past it, the least recently used page not used this frame is cleared
   whole, and its glyphs are made again when next needed.
4. **The text API is a layout handle:** `TextLayout.Shape(font, style,
   text, wrapWidth)` shapes and breaks the text natively and keeps the
   positioned glyphs and lines. C# reads its size and lines, hit tests and
   asks for caret positions; draw commands (step 3) will take the handle.
5. **Line breaking is a Latin subset of UAX #14:** a line may break after
   spaces, after hyphens between letters, and at a zero-width space, never
   at a no-break space; `\n` always breaks. A word wider than the line
   alone breaks between characters.
6. **Glyph rendering** is chosen per style (`GlyphRendering`: `Exact` or
   `DistanceField`).

## Details

- **Shaping** uses HarfBuzz's own OpenType functions, in font units, so a
  layout is independent of size until it is placed on pixels. FreeType's
  light hinting moves outlines only vertically and keeps advances
  unhinted, so exact glyphs line up with these positions.
- **Pixels:** sizes are logical pixels times the DPI scale. For exact
  text, every glyph origin and baseline lands on a whole physical pixel;
  distance-field text keeps fractional positions.
- **Lines** are cut from one shaped paragraph: kerning across a break is
  lost, which Latin text never shows.
- **Positions** are UTF-8 byte offsets natively; the C# layout converts
  them to string indices.
- **Memory:** FreeType and HarfBuzz allocate through Crucible's allocator,
  tagged `Text`. msdfgen uses the global heap for its short-lived outline
  and field while it generates a glyph, an exception to ADR 0032.
- **Shutdown:** the launcher calls `text::shutdown` after the renderer
  and before the job system, which waits for fields still generating.
- **The GPU side** of the atlas (textures, uploading changed rectangles
  each frame) comes with step 3, which first draws glyphs. Until then the
  cache keeps its pages in CPU memory.

## Why

On-demand generation is one path for both kinds and handles text nobody
saw coming, like player names. Whole-page eviction never fragments and is
simple to reason about. A layout handle keeps glyphs out of C# and lets
an element reshape only when its text, width, or style changes. A Latin
subset of line breaking covers v1's text; full UAX #14 comes with
non-Latin scripts.

Given up, for now: pre-baked glyphs, per-glyph eviction, font fallback,
and kerning across line breaks.

## As built (M6 step 1)

- `FontImporter` (C#, `Crucible.Assets`): checks the sfnt header and
  refuses collections; nothing else to set yet.
- Native, in `crucible::text`: `Text.h` (the API above), `Font.cpp`
  (FreeType and HarfBuzz through Crucible's allocator, the font table),
  `Shaping.cpp` (HarfBuzz, line breaking, layouts, carets), and
  `GlyphCache.cpp` (pages, shelves, eviction, msdfgen jobs), with
  `TextInternal.h` for what step 3 reads.
- C#: `Fonts` and `TextLayout` (`Crucible.Text`), which converts byte
  offsets to string indices.
- `MemoryTag::Text`; `licenses/freetype.txt`, `harfbuzz.txt` and
  `msdfgen.txt`.
- Tests (no GPU): loading and refusing fonts; kerning; breaking after
  spaces, at hyphens between letters, never at a no-break space, inside
  over-long words, and at `\n` and `\r\n`; whole physical pixels for
  exact glyphs at 1×, 1.25×, 1.5× and 2×; carets and hit tests agreeing;
  layouts outliving their font; exact glyphs and distance fields in their
  pages, the fields also from worker threads; eviction that never clears a page used this frame; and every
  FreeType and HarfBuzz byte freed by shutdown. From C#: lines and carets
  as string indices across `é`, and stale fonts and disposed layouts
  refused, under JIT and NativeAOT.
