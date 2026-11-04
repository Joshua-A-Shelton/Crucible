// Shaping and line breaking (ADR 0057): HarfBuzz shapes each paragraph in
// font units; lines are cut from it at Latin break opportunities (a subset
// of UAX #14) and placed in logical pixels, on whole physical pixels for
// exact glyphs. HarfBuzz appears only in this file: the shaping interface
// ADR 0019 asks for.

#include "RenderInternal.h" // glyphPageDescriptor
#include "TextInternal.h"

#include "ui.g.h" // UiInstance

#include <hb.h>

#include <cmath>
#include <cstring>

namespace crucible::text {

namespace {

// Where a caret can sit on a line: before each cluster, and at the end of
// a line that ends its paragraph.
struct Stop
{
    uint32_t offset;
    float x;
};

struct LineRecord
{
    TextLine line;
    uint32_t firstStop;
    uint32_t stopCount;
    uint32_t firstGlyph;
    uint32_t glyphCount;
    bool endsParagraph; // a caret at its end stays on it (a wrapped line's end is the next line's start)
};

struct Layout
{
    Font* font = nullptr;
    TextStyle style{};
    TextMetrics metrics{};
    Vector<PositionedGlyph> glyphs{MemoryTag::Text};
    Vector<LineRecord> lines{MemoryTag::Text};
    Vector<Stop> stops{MemoryTag::Text};
};

struct LayoutSlot
{
    Layout* layout = nullptr;
    uint32_t generation = 1;
};

struct Layouts
{
    Vector<LayoutSlot> slots{MemoryTag::Text};
    Vector<uint32_t> free{MemoryTag::Text};
    hb_buffer_t* buffer = nullptr; // reused
};

Layouts layouts;

Layout* layoutOf(TextLayoutHandle handle)
{
    return handle.index < layouts.slots.size() && layouts.slots[handle.index].layout != nullptr &&
                   layouts.slots[handle.index].generation == handle.generation
               ? layouts.slots[handle.index].layout
               : nullptr;
}

void destroyLayout(Layout* layout)
{
    releaseFontReference(*layout->font);
    destroy(MemoryTag::Text, layout);
}

// Decodes the code point at `offset`, or returns false for malformed UTF-8
// (overlong forms, surrogates, and values past U+10FFFF included).
bool decode(std::string_view text, uint32_t offset, uint32_t& codePoint, uint32_t& length)
{
    const auto byte = [&](uint32_t i) {
        return static_cast<uint8_t>(text[i]);
    };
    const uint8_t first = byte(offset);
    uint32_t minimum;
    if (first < 0x80)
    {
        codePoint = first;
        length = 1;
        return true;
    }
    if ((first & 0xE0) == 0xC0)
    {
        codePoint = first & 0x1F;
        length = 2;
        minimum = 0x80;
    }
    else if ((first & 0xF0) == 0xE0)
    {
        codePoint = first & 0x0F;
        length = 3;
        minimum = 0x800;
    }
    else if ((first & 0xF8) == 0xF0)
    {
        codePoint = first & 0x07;
        length = 4;
        minimum = 0x10000;
    }
    else
    {
        return false;
    }
    if (offset + length > text.size())
    {
        return false;
    }
    for (uint32_t i = 1; i < length; ++i)
    {
        if ((byte(offset + i) & 0xC0) != 0x80)
        {
            return false;
        }
        codePoint = codePoint << 6 | (byte(offset + i) & 0x3F);
    }
    return codePoint >= minimum && codePoint <= 0x10FFFF && (codePoint < 0xD800 || codePoint > 0xDFFF);
}

bool validUtf8(std::string_view text)
{
    for (uint32_t offset = 0; offset < text.size();)
    {
        uint32_t codePoint, length;
        if (!decode(text, offset, codePoint, length))
        {
            return false;
        }
        offset += length;
    }
    return true;
}

// The code point that ends just before `offset` (valid UTF-8), or 0 at the start.
uint32_t previous(std::string_view text, uint32_t offset, uint32_t start)
{
    if (offset <= start)
    {
        return 0;
    }
    uint32_t at = offset - 1;
    while (at > start && (static_cast<uint8_t>(text[at]) & 0xC0) == 0x80)
    {
        --at;
    }
    uint32_t codePoint, length;
    return decode(text, at, codePoint, length) ? codePoint : 0;
}

bool isSpace(uint32_t c)
{
    return c == ' ' || c == '\t';
}

bool isHyphen(uint32_t c)
{
    return c == '-' || c == 0x2010;
}

// Letters and digits, as far as breaking after a hyphen cares: anything
// not a space, punctuation, or symbol in Latin text.
bool isWordCharacter(uint32_t c)
{
    return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
           (c >= 0xC0 && c <= 0x24F && c != 0xD7 && c != 0xF7);
}

// A line may start at `offset` (inside a paragraph from `start`): after
// spaces, after a hyphen between word characters, or after a zero-width
// space. Never before a space (spaces hang at the end of the line) and
// never after a no-break space, which isn't a space here.
bool canBreakBefore(std::string_view text, uint32_t offset, uint32_t start)
{
    uint32_t here, length;
    if (offset <= start || !decode(text, offset, here, length) || isSpace(here))
    {
        return false;
    }
    const uint32_t before = previous(text, offset, start);
    if (isSpace(before) || before == 0x200B)
    {
        return true;
    }
    if (isHyphen(before))
    {
        const uint32_t hyphenLength = before < 0x80 ? 1 : 3;
        return isWordCharacter(here) && isWordCharacter(previous(text, offset - hyphenLength, start));
    }
    return false;
}

bool isSpaceAt(std::string_view text, uint32_t offset)
{
    uint32_t c, length;
    return offset < text.size() && decode(text, offset, c, length) && isSpace(c);
}

struct Shaped
{
    uint32_t glyph;
    uint32_t cluster; // byte offset of its cluster's first character
    float pen;        // logical pixels from the paragraph's start
    float advance;
    float xOffset, yOffset; // logical pixels, y up
};

} // namespace

void stopLayouts()
{
    for (LayoutSlot& slot : layouts.slots)
    {
        if (slot.layout != nullptr)
        {
            destroyLayout(slot.layout);
            slot.layout = nullptr;
            ++slot.generation;
        }
    }
    layouts.free.clear();
    for (uint32_t i = 0; i < layouts.slots.size(); ++i)
    {
        layouts.free.pushBack(i);
    }
    if (layouts.buffer != nullptr)
    {
        hb_buffer_destroy(layouts.buffer);
        layouts.buffer = nullptr;
    }
}

Result<TextLayoutHandle> shapeText(FontHandle fontHandle, TextStyle style, std::string_view text, float wrapWidth)
{
    Font* font = fontOf(fontHandle);
    if (font == nullptr)
    {
        return Status::NotFound;
    }
    if (!(style.size > 0) || !(style.scale >= 0) || !(style.lineHeight >= 0) || text.size() >= UINT32_MAX ||
        !validUtf8(text))
    {
        return Status::InvalidData;
    }
    const float scale = style.scale > 0 ? style.scale : 1.0f;
    const bool exact = style.rendering == GlyphRendering::Exact;
    // Exact glyphs sit on whole physical pixels.
    const auto snap = [&](float logical) {
        return exact ? std::round(logical * scale) / scale : logical;
    };

    const float units = style.size / font->unitsPerEm; // logical pixels per font unit
    const float ascent = font->ascender * units;
    const float descent = -font->descender * units;
    const float natural = ascent + descent + font->lineGap * units;
    const float lineHeight = snap(natural * (style.lineHeight > 0 ? style.lineHeight : 1.0f));
    // The ascent and descent centered in the line, as CSS does.
    const float baselineInLine = snap((lineHeight - ascent - descent) * 0.5f + ascent);

    auto* layout = create<Layout>(MemoryTag::Text);
    layout->font = font;
    layout->style = style;
    retainFont(*font);
    if (layouts.buffer == nullptr)
    {
        layouts.buffer = hb_buffer_create();
    }
    hb_buffer_t* buffer = layouts.buffer;
    Vector<Shaped> shaped(MemoryTag::Text);

    const auto addLine = [&](uint32_t start, uint32_t end, std::span<const Shaped> glyphs, bool endsParagraph) {
        LineRecord record{};
        record.line.start = start;
        record.line.end = end;
        record.line.top = static_cast<float>(layout->lines.size()) * lineHeight;
        record.line.baseline = record.line.top + baselineInLine;
        record.line.height = lineHeight;
        record.firstStop = static_cast<uint32_t>(layout->stops.size());
        record.firstGlyph = static_cast<uint32_t>(layout->glyphs.size());
        record.endsParagraph = endsParagraph;
        const float origin = glyphs.empty() ? 0.0f : glyphs[0].pen;
        float width = 0;
        uint32_t previousCluster = UINT32_MAX;
        for (const Shaped& glyph : glyphs)
        {
            const float pen = snap(glyph.pen - origin);
            if (glyph.cluster != previousCluster)
            {
                layout->stops.pushBack({glyph.cluster, pen});
                previousCluster = glyph.cluster;
            }
            layout->glyphs.pushBack(
                {glyph.glyph, pen + snap(glyph.xOffset), record.line.baseline - snap(glyph.yOffset), glyph.cluster});
            if (!isSpaceAt(text, glyph.cluster))
            {
                width = snap(glyph.pen + glyph.advance - origin);
            }
        }
        if (endsParagraph)
        {
            const float right = glyphs.empty() ? 0.0f : snap(glyphs.back().pen + glyphs.back().advance - origin);
            layout->stops.pushBack({record.line.end, right});
        }
        record.line.width = width;
        record.stopCount = static_cast<uint32_t>(layout->stops.size()) - record.firstStop;
        record.glyphCount = static_cast<uint32_t>(layout->glyphs.size()) - record.firstGlyph;
        layout->metrics.width = width > layout->metrics.width ? width : layout->metrics.width;
        layout->lines.pushBack(record);
    };

    const auto size = static_cast<uint32_t>(text.size());
    for (uint32_t start = 0;;)
    {
        // A paragraph runs to the next \n (a \r before it is part of the break).
        uint32_t next = start;
        while (next < size && text[next] != '\n')
        {
            ++next;
        }
        const uint32_t end = next > start && text[next - 1] == '\r' ? next - 1 : next;

        shaped.clear();
        if (end > start)
        {
            hb_buffer_clear_contents(buffer);
            hb_buffer_add_utf8(buffer, text.data(), static_cast<int>(size), start, static_cast<int>(end - start));
            hb_buffer_set_cluster_level(buffer, HB_BUFFER_CLUSTER_LEVEL_MONOTONE_CHARACTERS);
            hb_buffer_guess_segment_properties(buffer);
            hb_shape(font->hbFont, buffer, nullptr, 0);
            unsigned count = 0;
            const hb_glyph_info_t* infos = hb_buffer_get_glyph_infos(buffer, &count);
            const hb_glyph_position_t* positions = hb_buffer_get_glyph_positions(buffer, &count);
            float pen = 0;
            for (unsigned i = 0; i < count; ++i)
            {
                const float advance = static_cast<float>(positions[i].x_advance) * units;
                shaped.pushBack({infos[i].codepoint, infos[i].cluster, pen, advance,
                                 static_cast<float>(positions[i].x_offset) * units,
                                 static_cast<float>(positions[i].y_offset) * units});
                pen += advance;
            }
        }

        // Greedy breaking: the longest run of clusters that fits, cut at
        // the last opportunity; a word wider than the line alone is cut
        // between clusters, keeping at least one per line.
        const auto count = static_cast<uint32_t>(shaped.size());
        uint32_t lineStart = 0;
        uint32_t lineStartOffset = start;
        while (true)
        {
            uint32_t cut = count;
            if (wrapWidth > 0)
            {
                uint32_t lastBreak = lineStart;
                for (uint32_t i = lineStart; i < count; ++i)
                {
                    const bool clusterStart = i == lineStart || shaped[i].cluster != shaped[i - 1].cluster;
                    if (i > lineStart && clusterStart && canBreakBefore(text, shaped[i].cluster, start))
                    {
                        lastBreak = i;
                    }
                    const float right = shaped[i].pen + shaped[i].advance - shaped[lineStart].pen;
                    if (snap(right) > wrapWidth && !isSpaceAt(text, shaped[i].cluster))
                    {
                        if (lastBreak > lineStart)
                        {
                            cut = lastBreak;
                        }
                        else
                        {
                            // Back to this cluster's start; past the first cluster.
                            uint32_t at = i;
                            while (at > lineStart && shaped[at].cluster == shaped[at - 1].cluster)
                            {
                                --at;
                            }
                            if (at == lineStart)
                            {
                                at = i + 1;
                                while (at < count && shaped[at].cluster == shaped[at - 1].cluster)
                                {
                                    ++at;
                                }
                            }
                            cut = at;
                        }
                        break;
                    }
                }
            }
            const bool last = cut >= count;
            const uint32_t lineEnd = last ? end : shaped[cut].cluster;
            addLine(lineStartOffset, lineEnd, shaped.span().subspan(lineStart, cut - lineStart), last);
            if (last)
            {
                break;
            }
            lineStart = cut;
            lineStartOffset = lineEnd;
        }

        if (next >= size)
        {
            break;
        }
        start = next + 1;
    }

    layout->metrics.lineCount = static_cast<uint32_t>(layout->lines.size());
    layout->metrics.height = static_cast<float>(layout->lines.size()) * lineHeight;

    // Ask for the glyphs now: exact ones are made at once, distance fields
    // start generating.
    for (const PositionedGlyph& glyph : layout->glyphs)
    {
        (void)cachedGlyph(*font, glyph.glyph, style.rendering, style.size * scale);
    }

    uint32_t index;
    if (!layouts.free.empty())
    {
        index = layouts.free.back();
        layouts.free.popBack();
    }
    else
    {
        index = static_cast<uint32_t>(layouts.slots.size());
        layouts.slots.pushBack({});
    }
    layouts.slots[index].layout = layout;
    return TextLayoutHandle{index, layouts.slots[index].generation};
}

void destroyTextLayout(TextLayoutHandle handle)
{
    Layout* layout = layoutOf(handle);
    if (layout == nullptr)
    {
        return;
    }
    destroyLayout(layout);
    layouts.slots[handle.index].layout = nullptr;
    ++layouts.slots[handle.index].generation;
    layouts.free.pushBack(handle.index);
}

TextMetrics textMetrics(TextLayoutHandle handle)
{
    const Layout* layout = layoutOf(handle);
    return layout != nullptr ? layout->metrics : TextMetrics{};
}

TextLine textLine(TextLayoutHandle handle, uint32_t line)
{
    const Layout* layout = layoutOf(handle);
    return layout != nullptr && line < layout->lines.size() ? layout->lines[line].line : TextLine{};
}

uint32_t hitTestText(TextLayoutHandle handle, float x, float y)
{
    const Layout* layout = layoutOf(handle);
    if (layout == nullptr)
    {
        return 0;
    }
    uint32_t line = 0;
    while (line + 1 < layout->lines.size() && y >= layout->lines[line + 1].line.top)
    {
        ++line;
    }
    const LineRecord& record = layout->lines[line];
    if (record.stopCount == 0)
    {
        return record.line.start;
    }
    uint32_t best = record.firstStop;
    for (uint32_t i = record.firstStop + 1; i < record.firstStop + record.stopCount; ++i)
    {
        if (std::fabs(layout->stops[i].x - x) < std::fabs(layout->stops[best].x - x))
        {
            best = i;
        }
    }
    return layout->stops[best].offset;
}

TextCaret textCaret(TextLayoutHandle handle, uint32_t offset)
{
    const Layout* layout = layoutOf(handle);
    if (layout == nullptr)
    {
        return {};
    }
    auto line = static_cast<uint32_t>(layout->lines.size()) - 1;
    for (uint32_t i = 0; i < layout->lines.size(); ++i)
    {
        const LineRecord& record = layout->lines[i];
        if (offset < record.line.end || (record.endsParagraph && offset <= record.line.end))
        {
            line = i;
            break;
        }
    }
    const LineRecord& record = layout->lines[line];
    float x = 0;
    for (uint32_t i = record.firstStop; i < record.firstStop + record.stopCount; ++i)
    {
        if (layout->stops[i].offset <= offset)
        {
            x = layout->stops[i].x;
        }
    }
    return {x, record.line.top, record.line.height, line};
}

std::span<const PositionedGlyph> textGlyphs(TextLayoutHandle handle)
{
    const Layout* layout = layoutOf(handle);
    return layout != nullptr ? layout->glyphs.span() : std::span<const PositionedGlyph>{};
}

uint32_t textGlyphCount(TextLayoutHandle handle)
{
    const Layout* layout = layoutOf(handle);
    return layout != nullptr ? static_cast<uint32_t>(layout->glyphs.size()) : 0;
}

uint32_t writeTextQuads(TextLayoutHandle handle, Vector2 origin, Vector4 transform, Vector2 translation, Vector4 color,
                        Vector4 clip, std::span<uint8_t> out)
{
    Layout* layout = layoutOf(handle);
    if (layout == nullptr)
    {
        return 0;
    }
    using shaders::UiInstance;
    const size_t capacity = out.size() / sizeof(UiInstance);
    const TextStyle& style = layout->style;
    const float scale = style.scale > 0 ? style.scale : 1.0f;
    const bool exact = style.rendering == GlyphRendering::Exact;
    const float tolerance = 1e-4f * scale;
    const bool snapped = exact && transform.y == 0 && transform.z == 0 && std::abs(transform.x - scale) < tolerance &&
                         std::abs(transform.w - scale) < tolerance;
    // The layout's origin on a whole pixel; its glyphs' offsets already are.
    const float baseX = std::round(origin.x * scale + translation.x);
    const float baseY = std::round(origin.y * scale + translation.y);
    const float texel = 1.0f / static_cast<float>(glyphPageSize);
    const auto em = static_cast<float>(style.size / distanceFieldEm); // logical units per field texel
    uint32_t written = 0;
    for (const PositionedGlyph& glyph : layout->glyphs)
    {
        if (written == capacity)
        {
            break;
        }
        const CachedGlyph* cached = cachedGlyph(*layout->font, glyph.glyph, style.rendering, style.size * scale);
        if (cached == nullptr || cached->width == 0)
        {
            continue;
        }
        const uint32_t slot = render::glyphPageDescriptor(style.rendering, cached->page);
        if (slot == 0)
        {
            continue;
        }
        UiInstance instance{};
        instance.kind = exact ? 3u : 4u; // uiGlyph, uiFieldGlyph
        instance.texture = slot;
        instance.color = color;
        instance.clip = clip;
        instance.uv = {cached->x * texel, cached->y * texel, (cached->x + cached->width) * texel,
                       (cached->y + cached->height) * texel};
        instance.transform = transform;
        instance.translation = translation;
        if (snapped)
        {
            // In physical pixels, untransformed.
            instance.rect = {baseX + std::round(glyph.x * scale) + cached->left,
                             baseY + std::round(glyph.y * scale) + cached->top, static_cast<float>(cached->width),
                             static_cast<float>(cached->height)};
            instance.transform = {1, 0, 0, 1};
            instance.translation = {0, 0};
        }
        else if (exact)
        {
            instance.rect = {origin.x + glyph.x + cached->left / scale, origin.y + glyph.y + cached->top / scale,
                             cached->width / scale, cached->height / scale};
        }
        else
        {
            instance.rect = {origin.x + glyph.x + cached->left * style.size,
                             origin.y + glyph.y + cached->top * style.size, cached->width * em, cached->height * em};
        }
        std::memcpy(out.data() + written * sizeof(UiInstance), &instance, sizeof(instance));
        ++written;
    }
    return written;
}

} // namespace crucible::text
