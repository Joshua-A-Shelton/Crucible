#pragma once

// Fonts and text (ADR 0057): fonts loaded from imported .crfont files, and
// text shaped (HarfBuzz) and broken into lines natively, kept as a layout
// that C# measures, hit tests, and (from M6 step 3) draws. Glyphs come from
// the glyph cache: exact-size (FreeType) or distance fields (msdfgen), per
// style (ADR 0020, amended).
//
// Units: logical pixels, from the layout's top left, y down. A style's
// scale is the DPI scale; for exact text, glyph origins and baselines land
// on whole physical pixels. Positions in the text are UTF-8 byte offsets.
//
// Threading: main thread only, like the renderer. Distance-field glyphs are
// generated on the job system.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>
#include <crucible/RenderAssets.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::text {

// A loaded font, or a shaped text: an index and a generation, so a handle
// kept after it was unloaded is recognized. Generation 0 is no handle.
struct CRUCIBLE_EXPOSE FontHandle
{
    uint32_t index;
    uint32_t generation;
};

struct CRUCIBLE_EXPOSE TextLayoutHandle
{
    uint32_t index;
    uint32_t generation;
};

enum class CRUCIBLE_EXPOSE GlyphRendering : uint32_t
{
    Exact,         // rasterized at the drawn size: crisp, for static and small text
    DistanceField, // one field per glyph: for large, scaled, turned, or animated text
};

struct CRUCIBLE_EXPOSE TextStyle
{
    float size;       // logical pixels per em
    float scale;      // physical pixels per logical one (the DPI scale); 0 is 1
    float lineHeight; // times the font's own line spacing; 0 is 1
    GlyphRendering rendering;
};

struct CRUCIBLE_EXPOSE TextMetrics
{
    float width; // the widest line's, without trailing spaces
    float height;
    uint32_t lineCount; // at least 1, even for empty text
};

struct CRUCIBLE_EXPOSE TextLine
{
    uint32_t start; // byte offsets: the line's text, with its trailing spaces
    uint32_t end;   // but not the line break
    float width;    // without trailing spaces
    float top;
    float baseline;
    float height;
};

// Where a caret before a byte offset sits.
struct CRUCIBLE_EXPOSE TextCaret
{
    float x;
    float top;
    float height;
    uint32_t line;
};

// By asset ID (a .crfont), like the GPU assets: acquire loads on first use
// and counts uses; release unloads after the last. NotFound if the reader
// has no such asset; InvalidData if its bytes aren't a font FreeType reads.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<FontHandle> acquireFont(assets::AssetId font);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void releaseFont(FontHandle font);
// From a .crfont's bytes, outside the asset system.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<FontHandle> loadFont(std::span<const uint8_t> bytes);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void unloadFont(FontHandle font);

// Shapes UTF-8 text and breaks it into lines no wider than `wrapWidth`
// (0 or less: only at line breaks), and asks the glyph cache for its
// glyphs. NotFound for a stale font; InvalidData if the text isn't UTF-8
// or the style's size isn't positive. A layout keeps its font's data, so it
// stays valid after the font is released.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<TextLayoutHandle> shapeText(FontHandle font, TextStyle style,
                                                                   std::string_view text, float wrapWidth);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void destroyTextLayout(TextLayoutHandle layout);

// Zeros for a stale handle.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE TextMetrics textMetrics(TextLayoutHandle layout);
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE TextLine textLine(TextLayoutHandle layout, uint32_t line);
// The byte offset nearest a point: the line at its height (clamped to the
// first and last), then the caret position nearest its x.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t hitTestText(TextLayoutHandle layout, float x, float y);
// An offset inside a ligature or a multi-byte character goes to the start
// of its cluster; past the end, to the end.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE TextCaret textCaret(TextLayoutHandle layout, uint32_t offset);

// Drawing (ADR 0059), for the engine's Canvas: the layout's glyphs, an upper
// bound on the quads writeTextQuads writes.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t textGlyphCount(TextLayoutHandle layout);
// Writes the layout's glyphs as UiInstances (shaders/crucible/ui.slang) into
// `out`, and returns how many: glyphs without an image yet (a distance field
// still generating, a page without its GPU texture) are left out, and appear
// on a later frame. The layout's top left goes to `origin`, in local units
// mapped to physical pixels by `transform` (m11, m12, m21, m22) and
// `translation`. When that mapping is the layout's DPI scale and a
// translation, exact glyphs land texel for texel on whole pixels. `color` is
// linear and premultiplied; `clip` is in physical pixels.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint32_t writeTextQuads(TextLayoutHandle layout, Vector2 origin,
                                                                 Vector4 transform, Vector2 translation, Vector4 color,
                                                                 Vector4 clip, std::span<uint8_t> out);

// Destroys every layout, font, and cached glyph, waiting for distance
// fields still generating. Called by the launcher after the renderer and
// before the job system; fonts and layouts start again on next use.
// Internal in C#, for the engine's tests.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void shutdown();

// What drawing reads (C++ only): each glyph's ID in its font and its origin
// on the baseline, and the byte offset of the cluster it belongs to.
struct PositionedGlyph
{
    uint32_t glyph;
    float x;
    float y;
    uint32_t offset;
};

CRUCIBLE_EXPORT std::span<const PositionedGlyph> textGlyphs(TextLayoutHandle layout);

} // namespace crucible::text
