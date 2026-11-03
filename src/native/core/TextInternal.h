#pragma once

// Text internals (ADR 0057): fonts as shaping and the glyph cache see them,
// and the glyph cache, which UI drawing (M6 step 3) reads.

#include <crucible/Memory.h>
#include <crucible/Text.h>
#include <crucible/Vector.h>

#include <cstddef>
#include <cstdint>

struct FT_FaceRec_;
struct hb_blob_t;
struct hb_face_t;
struct hb_font_t;

namespace crucible::text {

// Allocations for FreeType and HarfBuzz, tagged Text, each with its size in
// front (neither says how large a block it frees is).
void* textAllocate(size_t size);
void* textReallocate(void* block, size_t size);
void textFree(void* block);

// A loaded face, alive while its handle or any layout uses it.
struct Font
{
    Vector<uint8_t> data{MemoryTag::Text}; // the font file, which FreeType and HarfBuzz read in place
    FT_FaceRec_* face = nullptr;
    hb_blob_t* blob = nullptr;
    hb_face_t* hbFace = nullptr;
    hb_font_t* hbFont = nullptr; // at units per em: positions in font units
    uint64_t serial = 0;         // unique per load, for the glyph cache's keys
    uint32_t references = 0;
    float unitsPerEm = 0;
    float ascender = 0;  // font units, up
    float descender = 0; // font units, negative below the baseline
    float lineGap = 0;
    uint32_t faceSize = 0; // the size FreeType's face is set to, 26.6 pixels
};

// The live font behind a handle, or null.
Font* fontOf(FontHandle font);
void retainFont(Font& font);
void releaseFontReference(Font& font);
// The FreeType face set to a size in 26.6 pixels.
FT_FaceRec_* sizedFace(Font& font, uint32_t size);

// ---------------------------------------------------------------------------
// The glyph cache
// ---------------------------------------------------------------------------

constexpr uint32_t glyphPageSize = 1024;
constexpr uint32_t maxExactPages = 8;         // R8: 8 MiB
constexpr uint32_t maxDistanceFieldPages = 4; // RGBA8: 16 MiB
constexpr uint32_t glyphPadding = 1;          // texels around each glyph, for filtering
// Distance fields (ADR 0020's comparison): texels per em, and the distance
// range in texels, which is also each field's margin around the outline.
constexpr double distanceFieldEm = 32.0;
constexpr double distanceFieldRange = 4.0;

struct CachedGlyph
{
    uint32_t page;
    uint16_t x, y; // texels in the page
    uint16_t width, height;
    // From the glyph's origin to its image's top left, y down: physical
    // pixels for exact glyphs, ems for distance fields (whose images are
    // distanceFieldEm texels per em).
    float left, top;
};

// A glyph's image, made if needed: exact ones now, distance fields queued
// on the job system. A glyph without an outline (a space) has an empty
// image. Null while its distance field is generating, or when every page is
// in use this frame. `pixelSize` (physical pixels per em) matters only for
// exact glyphs. Using a glyph marks its page used this frame. The pointer
// is valid until the cache is next used.
const CachedGlyph* cachedGlyph(Font& font, uint32_t glyph, GlyphRendering rendering, float pixelSize);

// Once a frame: places finished distance fields and starts a new frame for
// page eviction.
void updateGlyphCache();
// Waits for every queued distance field and places it (tests, shutdown).
void finishGlyphs();

// A page's pixels (R8 or RGBA8, rows of glyphPageSize texels) and the
// rectangle changed since `clearGlyphChanges` (empty: width 0).
struct GlyphPage
{
    const uint8_t* pixels;
    uint32_t bytesPerTexel;
    uint32_t changedX, changedY, changedWidth, changedHeight;
};

uint32_t glyphPageCount(GlyphRendering rendering);
GlyphPage glyphPage(GlyphRendering rendering, uint32_t page);
void clearGlyphChanges(GlyphRendering rendering, uint32_t page);

struct GlyphCacheStats
{
    uint32_t glyphs;     // cached, of both kinds
    uint32_t pending;    // distance fields generating
    uint32_t evictions;  // pages cleared to make room
    uint64_t exactMade;  // glyphs rasterized so far
    uint64_t fieldsMade; // distance fields generated so far
};

GlyphCacheStats glyphCacheStats();
// Empties the cache (tests, shutdown); waits for queued fields first.
void clearGlyphCache();

} // namespace crucible::text
