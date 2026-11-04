// The glyph cache's pages on the GPU (ADR 0059): a texture per CPU page,
// each with a fixed resource-heap slot, and the GlyphUpload pass, declared
// before the frame's steps run and filled after them, so glyphs made while
// steps draw (a page cleared and refilled) still arrive before the UI step
// samples them. Only the rectangle that changed in each page is copied.

#include "RenderInternal.h"
#include "TextInternal.h"

#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <slag/Limits.h>

#include <algorithm>
#include <cstring>

namespace crucible::render {

using text::GlyphRendering;

namespace {

struct GpuPage
{
    slag::Texture* texture = nullptr;
    bool uploaded = false; // in the ShaderRead layout, holding the page
};

struct Copy
{
    slag::Texture* texture;
    uint32_t x, y, width, height;
    uint64_t offset; // in the staging buffer
    uint32_t rowPitch;
};

struct GlyphGpu
{
    std::array<GpuPage, text::maxExactPages> exact{};
    std::array<GpuPage, text::maxDistanceFieldPages> fields{};
    // This frame's: the pages imported (and declared), and the copies.
    Vector<GpuPage*> declared{MemoryTag::Render};
    Vector<Copy> copies{MemoryTag::Render};
    slag::Buffer* staging = nullptr; // this frame's, destroyed after its work
};

GlyphGpu gpu;

std::span<GpuPage> pagesOf(GlyphRendering rendering)
{
    return rendering == GlyphRendering::Exact ? std::span<GpuPage>(gpu.exact) : std::span<GpuPage>(gpu.fields);
}

uint32_t slotOf(GlyphRendering rendering, uint32_t page)
{
    return glyphPageDescriptors + (rendering == GlyphRendering::Exact ? 0 : text::maxExactPages) + page;
}

Result<void> ensureTexture(GlyphRendering rendering, uint32_t page)
{
    GpuPage& entry = pagesOf(rendering)[page];
    if (entry.texture != nullptr)
    {
        return {};
    }
    slag::GraphicsDevice* device = renderDevice();
    slag::Result<slag::Texture*> texture = device->createTexture2D(
        {.format = rendering == GlyphRendering::Exact ? slag::PixelFormat::R8Unorm : slag::PixelFormat::R8G8B8A8Unorm,
         .width = text::glyphPageSize,
         .height = text::glyphPageSize,
         .debugName = rendering == GlyphRendering::Exact ? "crucible glyph page" : "crucible distance-field page"});
    if (!texture)
    {
        return Status::OutOfDeviceMemory;
    }
    entry.texture = texture.value();
    entry.uploaded = false;
    device->writeTexture(slotOf(rendering, page), {.texture = entry.texture});
    return {};
}

uint32_t roundUp(uint32_t value, uint32_t alignment)
{
    return (value + alignment - 1) / alignment * alignment;
}

} // namespace

uint32_t glyphPageDescriptor(GlyphRendering rendering, uint32_t page)
{
    const std::span<GpuPage> pages = pagesOf(rendering);
    return page < pages.size() && pages[page].texture != nullptr ? slotOf(rendering, page) : 0;
}

Result<void> beginGlyphUpload(slag::rendergraph::RenderGraph& graph)
{
    gpu.declared.clear();
    gpu.copies.clear();
    gpu.staging = nullptr;
    for (GlyphRendering rendering : {GlyphRendering::Exact, GlyphRendering::DistanceField})
    {
        const uint32_t count = text::glyphPageCount(rendering);
        for (uint32_t page = 0; page < count; ++page)
        {
            if (Result<void> made = ensureTexture(rendering, page); !made)
            {
                return made;
            }
            gpu.declared.pushBack(&pagesOf(rendering)[page]);
        }
    }
    if (gpu.declared.empty())
    {
        return {};
    }
    // Imported in their layout before the frame; every page is declared,
    // since which ones change is known only after the steps run.
    Vector<slag::rendergraph::RenderGraphTexture> handles{MemoryTag::Render};
    for (GpuPage* page : gpu.declared)
    {
        handles.pushBack(graph.importTexture(
            page->texture,
            {.initialLayout = page->uploaded ? slag::TextureLayout::ShaderRead : slag::TextureLayout::Undefined,
             .finalLayout = slag::TextureLayout::ShaderRead}));
        page->uploaded = true;
    }
    graph.addPass(
        "GlyphUpload", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            for (slag::rendergraph::RenderGraphTexture handle : handles)
            {
                pass.use(handle, slag::rendergraph::Usage::CopyDest);
            }
        },
        [](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
            for (const Copy& copy : gpu.copies)
            {
                commands->copyBufferToTexture({.texture = copy.texture, .x = copy.x, .y = copy.y},
                                              {.buffer = gpu.staging, .offset = copy.offset, .rowPitch = copy.rowPitch},
                                              copy.width, copy.height);
            }
        });
    return {};
}

Result<void> finishGlyphUpload()
{
    if (gpu.declared.empty())
    {
        return {};
    }
    // The changed rectangles of the pages declared, packed into one staging
    // buffer with the alignment copies need.
    struct Pending
    {
        GlyphRendering rendering;
        uint32_t page;
        text::GlyphPage pixels;
    };
    Vector<Pending> pending{MemoryTag::Render};
    uint64_t size = 0;
    for (GlyphRendering rendering : {GlyphRendering::Exact, GlyphRendering::DistanceField})
    {
        const std::span<GpuPage> pages = pagesOf(rendering);
        const uint32_t count = std::min<uint32_t>(text::glyphPageCount(rendering), static_cast<uint32_t>(pages.size()));
        for (uint32_t page = 0; page < count; ++page)
        {
            const text::GlyphPage pixels = text::glyphPage(rendering, page);
            if (pages[page].texture == nullptr || pixels.changedWidth == 0 ||
                std::find(gpu.declared.begin(), gpu.declared.end(), &pages[page]) == gpu.declared.end())
            {
                continue; // a page made during the steps: next frame
            }
            const uint32_t rowPitch =
                roundUp(pixels.changedWidth * pixels.bytesPerTexel, slag::alignment::textureCopyRowPitch);
            size = roundUp(static_cast<uint32_t>(size), slag::alignment::textureCopyOffset);
            gpu.copies.pushBack({pages[page].texture, pixels.changedX, pixels.changedY, pixels.changedWidth,
                                 pixels.changedHeight, size, rowPitch});
            size += static_cast<uint64_t>(rowPitch) * pixels.changedHeight;
            pending.pushBack({rendering, page, pixels});
        }
    }
    if (gpu.copies.empty())
    {
        return {};
    }
    slag::GraphicsDevice* device = renderDevice();
    slag::Result<slag::Buffer*> staging =
        device->createBuffer({.size = size,
                              .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                              .debugName = "crucible glyph upload"});
    if (!staging)
    {
        gpu.copies.clear();
        return Status::OutOfDeviceMemory;
    }
    gpu.staging = staging.value();
    auto* mapped = static_cast<uint8_t*>(gpu.staging->mappedData());
    for (uint32_t i = 0; i < pending.size(); ++i)
    {
        const text::GlyphPage& pixels = pending[i].pixels;
        const Copy& copy = gpu.copies[i];
        const uint32_t rowBytes = pixels.changedWidth * pixels.bytesPerTexel;
        for (uint32_t row = 0; row < pixels.changedHeight; ++row)
        {
            std::memcpy(mapped + copy.offset + static_cast<uint64_t>(row) * copy.rowPitch,
                        pixels.pixels +
                            (static_cast<size_t>(pixels.changedY + row) * text::glyphPageSize + pixels.changedX) *
                                pixels.bytesPerTexel,
                        rowBytes);
        }
        text::clearGlyphChanges(pending[i].rendering, pending[i].page);
    }
    destroyAfterFrame(gpu.staging);
    return {};
}

void stopGlyphUpload()
{
    slag::GraphicsDevice* device = renderDevice();
    for (GlyphRendering rendering : {GlyphRendering::Exact, GlyphRendering::DistanceField})
    {
        for (GpuPage& page : pagesOf(rendering))
        {
            if (page.texture != nullptr)
            {
                device->destroy(page.texture); // the GPU is idle
            }
            page = {};
        }
    }
    gpu.declared.clear();
    gpu.copies.clear();
    gpu.staging = nullptr;
}

} // namespace crucible::render
