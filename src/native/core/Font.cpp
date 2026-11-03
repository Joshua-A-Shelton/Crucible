// Fonts (ADR 0057): .crfont files loaded for FreeType (rasterizing) and
// HarfBuzz (shaping), both reading the font file in place, and both
// allocating through Crucible's allocator.

#include "RenderInternal.h"
#include "TextInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/HashMap.h>

#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_MODULE_H
#include <hb.h>

#include <cstdio>
#include <cstring>

namespace crucible::text {

// ---------------------------------------------------------------------------
// Memory
// ---------------------------------------------------------------------------

namespace {

constexpr size_t sizePrefix = 16; // keeps the block 16-byte aligned

size_t blockSize(void* block)
{
    size_t size;
    std::memcpy(&size, static_cast<std::byte*>(block) - sizePrefix, sizeof(size));
    return size;
}

} // namespace

void* textAllocate(size_t size)
{
    auto* block = static_cast<std::byte*>(tryAllocate(size + sizePrefix, 16, MemoryTag::Text));
    if (block == nullptr)
    {
        return nullptr;
    }
    std::memcpy(block, &size, sizeof(size));
    return block + sizePrefix;
}

void textFree(void* block)
{
    if (block != nullptr)
    {
        free(static_cast<std::byte*>(block) - sizePrefix, blockSize(block) + sizePrefix, MemoryTag::Text);
    }
}

void* textReallocate(void* block, size_t size)
{
    if (block == nullptr)
    {
        return textAllocate(size);
    }
    if (size == 0)
    {
        textFree(block);
        return nullptr;
    }
    void* moved = textAllocate(size);
    if (moved != nullptr)
    {
        const size_t old = blockSize(block);
        std::memcpy(moved, block, old < size ? old : size);
        textFree(block);
    }
    return moved;
}

} // namespace crucible::text

// HarfBuzz's allocator (HB_CUSTOM_MALLOC, set where it's built).
extern "C" void* hb_malloc_impl(size_t size)
{
    return crucible::text::textAllocate(size);
}

extern "C" void* hb_calloc_impl(size_t count, size_t size)
{
    if (size != 0 && count > SIZE_MAX / size)
    {
        return nullptr;
    }
    void* block = crucible::text::textAllocate(count * size);
    if (block != nullptr)
    {
        std::memset(block, 0, count * size);
    }
    return block;
}

extern "C" void* hb_realloc_impl(void* block, size_t size)
{
    return crucible::text::textReallocate(block, size);
}

extern "C" void hb_free_impl(void* block)
{
    crucible::text::textFree(block);
}

namespace crucible::text {

namespace {

using assets::AssetId;

constexpr uint32_t fontFileKind = binary::tag("FONT");
constexpr uint32_t fontFileVersion = 1;

void* freetypeAllocate(FT_Memory, long size)
{
    return textAllocate(static_cast<size_t>(size));
}

void freetypeFree(FT_Memory, void* block)
{
    textFree(block);
}

void* freetypeReallocate(FT_Memory, long, long size, void* block)
{
    return textReallocate(block, static_cast<size_t>(size));
}

FT_MemoryRec_ freetypeMemory{nullptr, freetypeAllocate, freetypeFree, freetypeReallocate};

struct AssetIdHash
{
    uint64_t operator()(const AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
};

struct Slot
{
    Font* font = nullptr;
    uint32_t generation = 1; // the handle's; bumped when freed
    uint32_t uses = 0;       // acquires not yet released
    bool acquired = false;   // by asset ID, rather than loaded from bytes
    AssetId asset{};
};

struct Fonts
{
    FT_Library library = nullptr;
    Vector<Slot> slots{MemoryTag::Text};
    Vector<uint32_t> free{MemoryTag::Text};
    HashMap<AssetId, uint32_t, AssetIdHash> byAsset{MemoryTag::Text};
    uint64_t nextSerial = 1;
};

Fonts fonts;

Status startFreetype()
{
    if (fonts.library != nullptr)
    {
        return Status::Ok;
    }
    if (FT_New_Library(&freetypeMemory, &fonts.library) != 0)
    {
        fonts.library = nullptr;
        return Status::OutOfHostMemory;
    }
    FT_Add_Default_Modules(fonts.library);
    FT_Set_Default_Properties(fonts.library);
    return Status::Ok;
}

void destroyFont(Font* font)
{
    hb_font_destroy(font->hbFont);
    hb_face_destroy(font->hbFace);
    hb_blob_destroy(font->blob);
    if (font->face != nullptr)
    {
        FT_Done_Face(font->face);
    }
    destroy(MemoryTag::Text, font);
}

Result<Font*> createFont(std::span<const uint8_t> bytes)
{
    if (const Status started = startFreetype(); started != Status::Ok)
    {
        return started;
    }
    Result<binary::File*> opened = binary::parseFile(bytes, fontFileKind, fontFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    auto* font = create<Font>(MemoryTag::Text);
    Status status = Status::InvalidData;
    const int32_t section = binary::findSection(*opened.value(), binary::tag("DATA"));
    if (section >= 0)
    {
        font->data.resize(binary::sectionInfo(*opened.value(), static_cast<uint32_t>(section)).size);
        status = binary::readSection(*opened.value(), static_cast<uint32_t>(section), font->data.span()).status();
    }
    binary::closeFile(opened.value());

    if (status == Status::Ok && (FT_New_Memory_Face(fonts.library, font->data.data(),
                                                    static_cast<FT_Long>(font->data.size()), 0, &font->face) != 0 ||
                                 !FT_IS_SCALABLE(font->face)))
    {
        status = Status::InvalidData;
    }
    if (status != Status::Ok)
    {
        destroyFont(font);
        return status;
    }
    font->blob = hb_blob_create(reinterpret_cast<const char*>(font->data.data()),
                                static_cast<unsigned>(font->data.size()), HB_MEMORY_MODE_READONLY, nullptr, nullptr);
    font->hbFace = hb_face_create(font->blob, 0);
    font->hbFont = hb_font_create(font->hbFace);
    const unsigned unitsPerEm = hb_face_get_upem(font->hbFace);
    hb_font_set_scale(font->hbFont, static_cast<int>(unitsPerEm), static_cast<int>(unitsPerEm));
    font->unitsPerEm = static_cast<float>(unitsPerEm);
    // The typographic metrics (OS/2, else hhea), as HarfBuzz picks them.
    hb_font_extents_t extents{};
    hb_font_get_h_extents(font->hbFont, &extents);
    font->ascender = static_cast<float>(extents.ascender);
    font->descender = static_cast<float>(extents.descender);
    font->lineGap = static_cast<float>(extents.line_gap);
    if (font->ascender - font->descender <= 0)
    {
        font->ascender = static_cast<float>(font->face->ascender);
        font->descender = static_cast<float>(font->face->descender);
        font->lineGap = static_cast<float>(font->face->height - font->face->ascender + font->face->descender);
    }
    font->serial = fonts.nextSerial++;
    font->references = 1;
    return font;
}

FontHandle addFont(Font* font, bool acquired, const AssetId& asset)
{
    uint32_t index;
    if (!fonts.free.empty())
    {
        index = fonts.free.back();
        fonts.free.popBack();
    }
    else
    {
        index = static_cast<uint32_t>(fonts.slots.size());
        fonts.slots.pushBack({});
    }
    Slot& slot = fonts.slots[index];
    slot.font = font;
    slot.uses = 1;
    slot.acquired = acquired;
    slot.asset = asset;
    return {index, slot.generation};
}

Slot* slotOf(FontHandle font)
{
    return font.index < fonts.slots.size() && fonts.slots[font.index].font != nullptr &&
                   fonts.slots[font.index].generation == font.generation
               ? &fonts.slots[font.index]
               : nullptr;
}

void removeFont(FontHandle handle, bool acquired)
{
    Slot* slot = slotOf(handle);
    if (slot == nullptr)
    {
        return; // stale or none
    }
    if (slot->acquired != acquired)
    {
        std::fprintf(stderr, "crucible: %s\n",
                     acquired ? "release of a font loaded from bytes (use unloadFont)"
                              : "unload of a font acquired by asset ID (use releaseFont)");
        return;
    }
    if (acquired && --slot->uses > 0)
    {
        return;
    }
    if (acquired)
    {
        fonts.byAsset.erase(slot->asset);
    }
    releaseFontReference(*slot->font);
    slot->font = nullptr;
    ++slot->generation;
    fonts.free.pushBack(handle.index);
}

} // namespace

Font* fontOf(FontHandle font)
{
    Slot* slot = slotOf(font);
    return slot != nullptr ? slot->font : nullptr;
}

void retainFont(Font& font)
{
    ++font.references;
}

void releaseFontReference(Font& font)
{
    if (--font.references == 0)
    {
        destroyFont(&font);
    }
}

FT_FaceRec_* sizedFace(Font& font, uint32_t size)
{
    if (font.faceSize != size)
    {
        FT_Set_Char_Size(font.face, 0, static_cast<FT_F26Dot6>(size), 72, 72);
        font.faceSize = size;
    }
    return font.face;
}

Result<FontHandle> acquireFont(assets::AssetId font)
{
    if (const uint32_t* found = fonts.byAsset.find(font))
    {
        ++fonts.slots[*found].uses;
        return FontHandle{*found, fonts.slots[*found].generation};
    }
    Vector<uint8_t> bytes(MemoryTag::Text);
    if (const Status read = render::readAsset(font, bytes); read != Status::Ok)
    {
        if (read == Status::Unsupported)
        {
            std::fprintf(stderr, "crucible: a font was acquired before C# set the asset reader\n");
        }
        return Status::NotFound;
    }
    Result<Font*> created = createFont(bytes.span());
    if (!created)
    {
        return created.status();
    }
    const FontHandle handle = addFont(created.value(), true, font);
    fonts.byAsset.insertOrAssign(font, handle.index);
    return handle;
}

void releaseFont(FontHandle font)
{
    removeFont(font, true);
}

Result<FontHandle> loadFont(std::span<const uint8_t> bytes)
{
    Result<Font*> created = createFont(bytes);
    if (!created)
    {
        return created.status();
    }
    return addFont(created.value(), false, {});
}

void unloadFont(FontHandle font)
{
    removeFont(font, false);
}

void stopLayouts(); // Shaping.cpp

void shutdown()
{
    stopLayouts();
    clearGlyphCache();
    for (uint32_t i = 0; i < fonts.slots.size(); ++i)
    {
        if (fonts.slots[i].font != nullptr)
        {
            releaseFontReference(*fonts.slots[i].font);
            fonts.slots[i].font = nullptr;
            ++fonts.slots[i].generation;
            fonts.free.pushBack(i);
        }
    }
    fonts.byAsset.clear();
    if (fonts.library != nullptr)
    {
        FT_Done_Library(fonts.library);
        fonts.library = nullptr;
    }
}

} // namespace crucible::text
