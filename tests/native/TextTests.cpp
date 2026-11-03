// Fonts and text (ADR 0057): fonts loaded from .crfont bytes, shaping and
// Latin line breaking, carets and hit tests, pixel snapping, and the glyph
// cache's exact glyphs, distance fields, and page eviction. No GPU.

#include "TextInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Text.h>

#include <doctest/doctest.h>

#include <cmath>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

using namespace crucible;
using namespace crucible::text;

namespace {

std::vector<uint8_t> fontFile()
{
    std::ifstream in(CRUCIBLE_TEST_FONT_DIR "/NotoSans-Regular.ttf", std::ios::binary);
    REQUIRE(in);
    const std::vector<uint8_t> font{std::istreambuf_iterator<char>(in), {}};
    binary::Writer* writer = binary::createWriter(binary::tag("FONT"), 1);
    REQUIRE(binary::addSection(*writer, binary::tag("DATA"), 1, font, 16));
    Vector<std::byte> file(MemoryTag::Assets);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, file));
    binary::destroyWriter(writer);
    const auto* bytes = reinterpret_cast<const uint8_t*>(file.data());
    return {bytes, bytes + file.size()};
}

// The font, loaded once per test and gone with it.
struct Loaded
{
    FontHandle font{};

    Loaded()
    {
        const std::vector<uint8_t> file = fontFile();
        Result<FontHandle> loaded = loadFont(file);
        REQUIRE(loaded);
        font = loaded.value();
    }

    ~Loaded()
    {
        text::shutdown();
    }

    TextLayoutHandle shape(std::string_view text, float wrapWidth = 0, TextStyle style = {.size = 16})
    {
        Result<TextLayoutHandle> layout = shapeText(font, style, text, wrapWidth);
        REQUIRE(layout);
        return layout.value();
    }

    float width(std::string_view text, TextStyle style = {.size = 16})
    {
        return textMetrics(shape(text, 0, style)).width;
    }
};

std::string lineText(std::string_view text, TextLayoutHandle layout, uint32_t line)
{
    const TextLine l = textLine(layout, line);
    return std::string(text.substr(l.start, l.end - l.start));
}

bool whole(float value)
{
    return std::fabs(value - std::round(value)) < 1e-3f;
}

} // namespace

TEST_SUITE("text")
{
    TEST_CASE("fonts load from .crfont bytes and reject anything else")
    {
        Loaded loaded;
        CHECK(fontOf(loaded.font) != nullptr);
        const uint8_t junk[] = {1, 2, 3, 4};
        CHECK(loadFont(junk).status() == Status::InvalidData);

        binary::Writer* writer = binary::createWriter(binary::tag("FONT"), 1);
        REQUIRE(binary::addSection(*writer, binary::tag("DATA"), 1, junk, 16));
        Vector<std::byte> file(MemoryTag::Assets);
        REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, file));
        binary::destroyWriter(writer);
        CHECK(loadFont({reinterpret_cast<const uint8_t*>(file.data()), file.size()}).status() == Status::InvalidData);

        unloadFont(loaded.font);
        CHECK(fontOf(loaded.font) == nullptr);
        CHECK(shapeText(loaded.font, {.size = 16}, "stale", 0).status() == Status::NotFound);
    }

    TEST_CASE("shaping measures and kerns text and rejects bad input")
    {
        Loaded loaded;
        const TextLayoutHandle hello = loaded.shape("Hello");
        const TextMetrics metrics = textMetrics(hello);
        CHECK(metrics.lineCount == 1);
        CHECK(metrics.width > 20);
        CHECK(metrics.width < 60);
        CHECK(metrics.height > 16);
        CHECK(textGlyphs(hello).size() == 5);

        // Noto Sans kerns A against V.
        CHECK(loaded.width("AV") < loaded.width("A") + loaded.width("V") - 0.5f);
        // Twice the size, twice the width (exact positions round, a little).
        CHECK(loaded.width("Hello", {.size = 32}) == doctest::Approx(2 * metrics.width).epsilon(0.05));

        const TextMetrics empty = textMetrics(loaded.shape(""));
        CHECK(empty.lineCount == 1);
        CHECK(empty.width == 0);
        CHECK(empty.height == metrics.height);

        CHECK(shapeText(loaded.font, {.size = 16}, "\xC3\x28", 0).status() == Status::InvalidData); // bad continuation
        CHECK(shapeText(loaded.font, {.size = 16}, "\xC0\xAF", 0).status() == Status::InvalidData); // overlong
        CHECK(shapeText(loaded.font, {.size = 16}, "\xED\xA0\x80", 0).status() == Status::InvalidData); // a surrogate
        CHECK(shapeText(loaded.font, {.size = 0}, "zero", 0).status() == Status::InvalidData);
        CHECK(shapeText(loaded.font, {.size = 16}, "caf\xC3\xA9", 0));
    }

    TEST_CASE("lines break after spaces (keeping them) and at line breaks")
    {
        Loaded loaded;
        const std::string_view text = "the quick brown fox";
        const float wrap = loaded.width("the quick brown") - 1;
        const TextLayoutHandle layout = loaded.shape(text, wrap);
        REQUIRE(textMetrics(layout).lineCount == 2);
        CHECK(lineText(text, layout, 0) == "the quick ");
        CHECK(lineText(text, layout, 1) == "brown fox");
        CHECK(textLine(layout, 0).width == doctest::Approx(loaded.width("the quick")));
        CHECK(textLine(layout, 1).top == doctest::Approx(textLine(layout, 0).height));
        CHECK(textMetrics(layout).height == doctest::Approx(2 * textLine(layout, 0).height));

        const std::string_view paragraphs = "one\ntwo\r\n\nthree";
        const TextLayoutHandle broken = loaded.shape(paragraphs);
        REQUIRE(textMetrics(broken).lineCount == 4);
        CHECK(lineText(paragraphs, broken, 0) == "one");
        CHECK(lineText(paragraphs, broken, 1) == "two");
        CHECK(lineText(paragraphs, broken, 2).empty());
        CHECK(lineText(paragraphs, broken, 3) == "three");
    }

    TEST_CASE("hyphens break between letters but no-break spaces never do and long words split")
    {
        Loaded loaded;
        const std::string_view hyphen = "well-known";
        const TextLayoutHandle hyphenated = loaded.shape(hyphen, loaded.width("well-kno"));
        REQUIRE(textMetrics(hyphenated).lineCount == 2);
        CHECK(lineText(hyphen, hyphenated, 0) == "well-");

        // A minus sign before a digit isn't a hyphen between letters.
        const std::string_view minus = "x -5";
        CHECK(lineText(minus, loaded.shape(minus, loaded.width("x -")), 0) == "x ");

        const std::string_view spaced = "aaa bbb";
        const std::string_view joined = "aaa\xC2\xA0"
                                        "bbb";
        const float wrap = loaded.width("aaa b");
        CHECK(lineText(spaced, loaded.shape(spaced, wrap), 0) == "aaa ");
        const TextLayoutHandle kept = loaded.shape(joined, wrap);
        for (uint32_t line = 0; line < textMetrics(kept).lineCount; ++line)
        {
            CHECK(textLine(kept, line).start != 5); // never right after the no-break space
        }

        const std::string_view word = "abcdefghijkl";
        const float narrow = loaded.width("abc") + 0.5f;
        const TextLayoutHandle split = loaded.shape(word, narrow);
        CHECK(textMetrics(split).lineCount >= 4);
        uint32_t covered = 0;
        for (uint32_t line = 0; line < textMetrics(split).lineCount; ++line)
        {
            const TextLine l = textLine(split, line);
            CHECK(l.start == covered);
            CHECK(l.end > l.start);
            CHECK(l.width <= narrow);
            covered = l.end;
        }
        CHECK(covered == word.size());

        // Narrower than any letter: still one per line, never none.
        CHECK(textMetrics(loaded.shape("abc", 1)).lineCount == 3);
    }

    TEST_CASE("exact glyphs and baselines land on whole physical pixels")
    {
        Loaded loaded;
        for (const float scale : {1.0f, 1.25f, 1.5f, 2.0f})
        {
            CAPTURE(scale);
            const TextLayoutHandle layout = loaded.shape("Typography, kerned: AVAW", 0, {.size = 13, .scale = scale});
            for (const PositionedGlyph& glyph : textGlyphs(layout))
            {
                CHECK(whole(glyph.x * scale));
                CHECK(whole(glyph.y * scale));
            }
            CHECK(whole(textLine(layout, 0).baseline * scale));
            CHECK(whole(textLine(layout, 0).height * scale));
        }
        // Distance fields keep fractional positions.
        bool fractional = false;
        const TextLayoutHandle field =
            loaded.shape("Typography", 0, {.size = 13, .scale = 1.25f, .rendering = GlyphRendering::DistanceField});
        for (const PositionedGlyph& glyph : textGlyphs(field))
        {
            fractional = fractional || !whole(glyph.x * 1.25f);
        }
        CHECK(fractional);
    }

    TEST_CASE("carets and hit tests agree")
    {
        Loaded loaded;
        const std::string_view text = "one two\nthree";
        const TextLayoutHandle layout = loaded.shape(text);
        CHECK(textCaret(layout, 0).x == 0);
        CHECK(textCaret(layout, 0).line == 0);
        CHECK(textCaret(layout, 7).x == doctest::Approx(textLine(layout, 0).width));
        CHECK(textCaret(layout, 7).line == 0); // the end of a paragraph stays on its line
        CHECK(textCaret(layout, 8).line == 1);
        CHECK(textCaret(layout, 8).x == 0);
        CHECK(textCaret(layout, 100).line == 1); // past the end: the end
        for (uint32_t offset = 0; offset <= text.size(); ++offset)
        {
            if (offset == 7 || text[offset == text.size() ? 0 : offset] == '\n')
            {
                continue;
            }
            const TextCaret caret = textCaret(layout, offset);
            CAPTURE(offset);
            CHECK(hitTestText(layout, caret.x + 0.1f, caret.top + caret.height / 2) == offset);
        }
        CHECK(hitTestText(layout, -50, -50) == 0);
        CHECK(hitTestText(layout, 1000, 1000) == text.size());

        // A wrapped line's end is the next line's start.
        const TextLayoutHandle wrapped = loaded.shape("aaa bbb", loaded.width("aaa b"));
        CHECK(textCaret(wrapped, 4).line == 1);

        // Inside a multi-byte character: its start.
        const TextLayoutHandle accented = loaded.shape("caf\xC3\xA9!");
        CHECK(textCaret(accented, 4).x == textCaret(accented, 3).x);
    }

    TEST_CASE("a layout keeps its font after the font is unloaded")
    {
        Loaded loaded;
        const TextLayoutHandle layout = loaded.shape("still here");
        const float width = textMetrics(layout).width;
        unloadFont(loaded.font);
        CHECK(textMetrics(layout).width == width);
        destroyTextLayout(layout);
        CHECK(textMetrics(layout).lineCount == 0); // stale
    }

    TEST_CASE("exact glyphs are rasterized at their size into an R8 page")
    {
        Loaded loaded;
        clearGlyphCache();
        (void)loaded.shape("H", 0, {.size = 16});
        Font* font = fontOf(loaded.font);
        REQUIRE(font != nullptr);
        const uint32_t h = textGlyphs(loaded.shape("H"))[0].glyph;
        const CachedGlyph* small = cachedGlyph(*font, h, GlyphRendering::Exact, 16);
        REQUIRE(small != nullptr);
        const CachedGlyph smallCopy = *small;
        CHECK(smallCopy.width > 5);
        CHECK(smallCopy.height > 8);
        CHECK(smallCopy.top < 0); // above the baseline
        const CachedGlyph* large = cachedGlyph(*font, h, GlyphRendering::Exact, 32);
        REQUIRE(large != nullptr);
        CHECK(large->height > smallCopy.height * 1.8);

        REQUIRE(glyphPageCount(GlyphRendering::Exact) == 1);
        const GlyphPage page = glyphPage(GlyphRendering::Exact, 0);
        CHECK(page.bytesPerTexel == 1);
        CHECK(page.changedWidth > 0);
        uint32_t covered = 0;
        for (uint32_t y = 0; y < smallCopy.height; ++y)
        {
            for (uint32_t x = 0; x < smallCopy.width; ++x)
            {
                covered += page.pixels[(smallCopy.y + y) * glyphPageSize + smallCopy.x + x] > 128 ? 1 : 0;
            }
        }
        CHECK(covered > 10);
        clearGlyphChanges(GlyphRendering::Exact, 0);
        CHECK(glyphPage(GlyphRendering::Exact, 0).changedWidth == 0);

        const uint32_t space = textGlyphs(loaded.shape(" "))[0].glyph;
        const CachedGlyph* blank = cachedGlyph(*font, space, GlyphRendering::Exact, 16);
        REQUIRE(blank != nullptr);
        CHECK(blank->width == 0);
    }

    TEST_CASE("distance fields generate on the job system as one field per glyph for every size")
    {
        Loaded loaded;
        clearGlyphCache();
        const TextStyle field{.size = 16, .rendering = GlyphRendering::DistanceField};
        const TextLayoutHandle layout = loaded.shape("O", 0, field);
        (void)loaded.shape("O", 0, {.size = 64, .rendering = GlyphRendering::DistanceField});
        finishGlyphs();
        Font* font = fontOf(loaded.font);
        const uint32_t o = textGlyphs(layout)[0].glyph;
        const CachedGlyph* glyph = cachedGlyph(*font, o, GlyphRendering::DistanceField, 16);
        REQUIRE(glyph != nullptr);
        CHECK(glyph == cachedGlyph(*font, o, GlyphRendering::DistanceField, 64)); // the same field
        CHECK(glyphCacheStats().fieldsMade == 1);
        // About 0.7 em tall at 32 texels per em, plus the margins.
        CHECK(glyph->height > 0.6 * distanceFieldEm + 2 * distanceFieldRange);
        CHECK(glyph->height < 0.9 * distanceFieldEm + 2 * distanceFieldRange + 1);
        CHECK(glyph->top < 0);

        // Inside the ring is far from the outline (the median under 0.5),
        // the stroke inside it (over 0.5).
        const GlyphPage page = glyphPage(GlyphRendering::DistanceField, glyph->page);
        CHECK(page.bytesPerTexel == 4);
        const auto median = [&](uint32_t x, uint32_t y) {
            const uint8_t* t = &page.pixels[((glyph->y + y) * glyphPageSize + glyph->x + x) * 4];
            return std::max(std::min(t[0], t[1]), std::min(std::max(t[0], t[1]), t[2]));
        };
        CHECK(median(glyph->width / 2, glyph->height / 2) < 100);
        CHECK(median(0, 0) < 100);
        uint8_t brightest = 0;
        for (uint32_t x = 0; x < glyph->width; ++x)
        {
            brightest = std::max(brightest, median(x, glyph->height / 2));
        }
        CHECK(brightest > 160);
    }

    TEST_CASE("distance fields from worker threads arrive once updated")
    {
        REQUIRE(jobs::initialize(3));
        {
            Loaded loaded;
            clearGlyphCache();
            const TextLayoutHandle layout =
                loaded.shape("Workers, fields!", 0, {.size = 24, .rendering = GlyphRendering::DistanceField});
            // Until a frame places them, none is there.
            Font* font = fontOf(loaded.font);
            CHECK(cachedGlyph(*font, textGlyphs(layout)[0].glyph, GlyphRendering::DistanceField, 24) == nullptr);
            while (glyphCacheStats().pending > 0)
            {
                updateGlyphCache();
            }
            for (const PositionedGlyph& glyph : textGlyphs(layout))
            {
                CHECK(cachedGlyph(*font, glyph.glyph, GlyphRendering::DistanceField, 24) != nullptr);
            }
            // Shutdown with fields queued waits for them.
            (void)loaded.shape("XYZ", 0, {.size = 24, .rendering = GlyphRendering::DistanceField});
        }
        CHECK(glyphCacheStats().pending == 0);
        jobs::shutdown();
    }

    TEST_CASE("full pages evict the least recently used but never one used this frame")
    {
        Loaded loaded;
        clearGlyphCache();
        std::string ascii;
        for (char c = '!'; c <= '~'; ++c)
        {
            ascii += c;
        }
        // Large sizes fill pages fast; a new frame for each lets old pages go.
        uint32_t size = 60;
        while (glyphCacheStats().evictions == 0 && size < 400)
        {
            (void)loaded.shape(ascii, 0, {.size = static_cast<float>(size)});
            updateGlyphCache();
            size += 4;
        }
        CHECK(glyphPageCount(GlyphRendering::Exact) == maxExactPages);
        CHECK(glyphCacheStats().evictions > 0);

        // Used this frame, every page is kept: what doesn't fit isn't made.
        Font* font = fontOf(loaded.font);
        const uint32_t glyph = textGlyphs(loaded.shape("W"))[0].glyph;
        const uint32_t evictions = glyphCacheStats().evictions;
        for (uint32_t s = 0; s < 64; ++s)
        {
            (void)cachedGlyph(*font, glyph, GlyphRendering::Exact, 300.0f + static_cast<float>(s));
        }
        uint32_t failed = 0;
        for (uint32_t s = 0; s < 400; ++s)
        {
            failed += cachedGlyph(*font, glyph, GlyphRendering::Exact, 100.0f + static_cast<float>(s) * 0.5f) == nullptr
                          ? 1
                          : 0;
        }
        CHECK(glyphCacheStats().evictions - evictions <= maxExactPages);
        CHECK(failed > 0);
    }

    TEST_CASE("FreeType and HarfBuzz allocate through Crucible and shutdown frees it all")
    {
        {
            Loaded warm; // the first run sizes the tables that stay
            (void)warm.shape("warm up", 0, {.size = 16, .rendering = GlyphRendering::DistanceField});
        }
        const MemoryUsage before = memoryUsage(MemoryTag::Text);
        {
            Loaded loaded;
            const MemoryUsage loadedUsage = memoryUsage(MemoryTag::Text);
            CHECK(loadedUsage.bytes > before.bytes + 500'000); // the font file, and FreeType's face
            (void)loaded.shape("warm up", 0, {.size = 16, .rendering = GlyphRendering::DistanceField});
            CHECK(memoryUsage(MemoryTag::Text).bytes > loadedUsage.bytes); // HarfBuzz, layouts, pages
        }
        CHECK(memoryUsage(MemoryTag::Text).bytes == before.bytes);
        CHECK(memoryUsage(MemoryTag::Text).allocations == before.allocations);
    }
}
