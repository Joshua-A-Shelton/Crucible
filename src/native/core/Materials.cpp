// Materials at runtime (ADR 0052): records loaded into the material buffer,
// their textures acquired, and their pipelines made while loading, never
// while drawing. A model's generated materials are made when it loads and
// freed with it.

#include "RenderInternal.h"

#include <crucible/MaterialFile.h>
#include <crucible/Render.h>
#include <crucible/Shader.h>

#include "default.g.h" // DefaultMaterial

#include <slag/Slag.h>

#include <cstdio>
#include <cstring>

namespace crucible::render {

namespace {

using assets::AlphaMode;
using assets::MaterialInfo;
using assets::MaterialTexture;

// Room for tens of thousands of materials; a full buffer fails the load.
constexpr uint32_t materialBufferSize = 4u << 20;
constexpr uint32_t materialAlignment = 16;

// What decides a material's pipelines: its material type (the engine's, for
// now), whether it's alpha-tested, and whether both faces are drawn.
struct PipelineKey
{
    uint32_t type;
    bool mask;
    bool doubleSided;

    bool operator==(const PipelineKey&) const = default;
};

struct PipelineEntry
{
    PipelineKey key{};
    MaterialPipelines pipelines{};
};

struct LoadedMaterial
{
    MaterialPipelines* pipelines = nullptr; // into the cache, which never moves an entry
    uint32_t offset = 0;                    // in the material buffer
    uint32_t size = 0;
    Vector<TextureHandle> textures{MemoryTag::Render};
};

struct Slot
{
    LoadedMaterial* value = nullptr;
    uint32_t generation = 1;
};

struct FreeRange
{
    uint32_t offset;
    uint32_t size;
};

struct PendingFree
{
    FreeRange range;
    uint64_t after; // the work semaphore's value
};

struct Materials
{
    slag::GraphicsDevice* device = nullptr;
    slag::Buffer* buffer = nullptr;
    Vector<FreeRange> free{MemoryTag::Render};
    Vector<PendingFree> pending{MemoryTag::Render};
    Vector<Slot> slots{MemoryTag::Render};
    Vector<uint32_t> freeSlots{MemoryTag::Render};
    Vector<PipelineEntry*> pipelines{MemoryTag::Render};
    MaterialHandle fallback{};
};

Materials state;

// ---------------------------------------------------------------------------
// The material buffer
// ---------------------------------------------------------------------------

void collectRanges()
{
    const slag::SemaphoreValue reached = lastWorkSignaled();
    const uint64_t done = reached.semaphore != nullptr ? reached.semaphore->value() : UINT64_MAX;
    for (size_t i = 0; i < state.pending.size();)
    {
        if (state.pending[i].after <= done)
        {
            state.free.pushBack(state.pending[i].range);
            state.pending.removeSwap(i);
        }
        else
        {
            ++i;
        }
    }
}

// First fit; UINT32_MAX when full.
uint32_t allocateRange(uint32_t size)
{
    size = (size + materialAlignment - 1) / materialAlignment * materialAlignment;
    collectRanges();
    for (size_t i = 0; i < state.free.size(); ++i)
    {
        FreeRange& range = state.free[i];
        if (range.size >= size)
        {
            const uint32_t offset = range.offset;
            range.offset += size;
            range.size -= size;
            if (range.size == 0)
            {
                state.free.removeSwap(i);
            }
            return offset;
        }
    }
    return UINT32_MAX;
}

// Frames already submitted may still read it, so it's reused once they're done.
void freeRange(uint32_t offset, uint32_t size)
{
    size = (size + materialAlignment - 1) / materialAlignment * materialAlignment;
    state.pending.pushBack({{offset, size}, lastWorkSignaled().value});
}

// ---------------------------------------------------------------------------
// Pipelines
// ---------------------------------------------------------------------------

Result<slag::Pipeline*> createPipeline(const char* shader, const char* vertexEntry, const char* pixelEntry,
                                       slag::GraphicsPipelineDesc desc)
{
    Result<ShaderFile*> file = loadShaderFile(engineShader(shader));
    if (!file)
    {
        std::fprintf(stderr, "crucible: the embedded %s shader can't be read\n", shader);
        return file.status();
    }
    const int32_t vertex = findShaderEntry(*file.value(), vertexEntry);
    const int32_t pixel = pixelEntry != nullptr ? findShaderEntry(*file.value(), pixelEntry) : -1;
    if (vertex < 0 || (pixelEntry != nullptr && pixel < 0))
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    shaderStage(*file.value(), static_cast<uint32_t>(vertex), desc.vertex);
    if (pixel >= 0)
    {
        shaderStage(*file.value(), static_cast<uint32_t>(pixel), desc.pixel);
    }
    slag::Result<slag::Pipeline*> pipeline = state.device->createGraphicsPipeline(desc);
    destroyShaderFile(file.value());
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    return pipeline.value();
}

void destroyPipelines(MaterialPipelines& pipelines)
{
    if (pipelines.depth != nullptr)
    {
        state.device->destroy(pipelines.depth);
    }
    if (pipelines.forward != nullptr)
    {
        state.device->destroy(pipelines.forward);
    }
    pipelines = {};
}

Result<void> createPipelines(const PipelineKey& key, MaterialPipelines& out)
{
    const uint32_t samples = msaaSamples();
    // Clockwise front faces (ADR 0024); double-sided materials cull nothing.
    const slag::RasterizerState rasterizer{.cullMode = key.doubleSided ? slag::CullMode::None : slag::CullMode::Back,
                                           .frontCounterClockwise = false};
    // Reverse-Z (ADR 0050): nearer is greater.
    Result<slag::Pipeline*> depth =
        key.mask
            ? createPipeline(
                  "default", "materialVertex", "maskPixel",
                  {.rasterizer = rasterizer,
                   .depthStencil = {.depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::Greater},
                   .depthStencilFormat = sceneDepthFormat,
                   .sampleCount = samples,
                   .debugName = "DepthPrepass (alpha-tested)"})
            : createPipeline(
                  "mesh", "depthVertex", nullptr,
                  {.rasterizer = rasterizer,
                   .depthStencil = {.depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::Greater},
                   .depthStencilFormat = sceneDepthFormat,
                   .sampleCount = samples,
                   .debugName = "DepthPrepass"});
    if (!depth)
    {
        return depth.status();
    }
    const slag::PixelFormat formats[] = {sceneColorFormat};
    const char* pixel = key.type == static_cast<uint32_t>(assets::DefaultMaterialType::Pbr) ? "pbrPixel" : "toonPixel";
    // The prepass wrote the depth; GreaterEqual rather than Equal, so a
    // last-bit difference between the two pipelines' positions can't drop
    // a pixel.
    Result<slag::Pipeline*> forward = createPipeline(
        "default", "materialVertex", pixel,
        {.rasterizer = rasterizer,
         .depthStencil = {.depthTest = true, .depthWrite = false, .depthCompareOp = slag::CompareOp::GreaterEqual},
         .colorFormats = formats,
         .depthStencilFormat = sceneDepthFormat,
         .sampleCount = samples,
         .debugName = "Opaque"});
    if (!forward)
    {
        state.device->destroy(depth.value());
        return forward.status();
    }
    out = {depth.value(), forward.value()};
    return {};
}

Result<MaterialPipelines*> pipelinesFor(const PipelineKey& key)
{
    for (PipelineEntry* entry : state.pipelines)
    {
        if (entry->key == key)
        {
            return &entry->pipelines;
        }
    }
    auto* entry = create<PipelineEntry>(MemoryTag::Render);
    entry->key = key;
    if (Result<void> made = createPipelines(key, entry->pipelines); !made)
    {
        destroy(MemoryTag::Render, entry);
        return made.status();
    }
    state.pipelines.pushBack(entry);
    return &entry->pipelines;
}

// ---------------------------------------------------------------------------
// The table
// ---------------------------------------------------------------------------

LoadedMaterial* find(MaterialHandle handle)
{
    return handle.index < state.slots.size() && state.slots[handle.index].value != nullptr &&
                   state.slots[handle.index].generation == handle.generation
               ? state.slots[handle.index].value
               : nullptr;
}

MaterialHandle add(LoadedMaterial* material)
{
    uint32_t index;
    if (!state.freeSlots.empty())
    {
        index = state.freeSlots.back();
        state.freeSlots.popBack();
    }
    else
    {
        index = static_cast<uint32_t>(state.slots.size());
        state.slots.pushBack({});
    }
    state.slots[index].value = material;
    return {index, state.slots[index].generation};
}

void releaseMaterial(LoadedMaterial* material)
{
    freeRange(material->offset, material->size);
    for (TextureHandle texture : material->textures)
    {
        releaseTexture(texture);
    }
    destroy(MemoryTag::Render, material);
}

} // namespace

Result<void> startMaterials(slag::GraphicsDevice& device)
{
    state.device = &device;
    slag::Result<slag::Buffer*> buffer =
        device.createBuffer({.size = materialBufferSize,
                             .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                             .debugName = "crucible materials"});
    if (!buffer)
    {
        return Status::OutOfDeviceMemory;
    }
    state.buffer = buffer.value();
    device.writeRawBuffer(materialBufferDescriptor, {.buffer = state.buffer, .size = materialBufferSize});
    state.free.pushBack({0, materialBufferSize});

    // Slots without a material (or with one out of range) draw white toon.
    shaders::DefaultMaterial white{};
    white.baseColor = {1, 1, 1, 1};
    white.roughness = 1;
    white.normalScale = 1;
    white.occlusionStrength = 1;
    MaterialInfo info{};
    info.type = static_cast<uint32_t>(assets::DefaultMaterialType::Toon);
    info.parameterSize = sizeof(white);
    Result<MaterialHandle> fallback =
        createMaterial(info, {reinterpret_cast<const uint8_t*>(&white), sizeof(white)}, {});
    if (!fallback)
    {
        return fallback.status();
    }
    state.fallback = fallback.value();
    return {};
}

void stopMaterials()
{
    if (state.device == nullptr)
    {
        return;
    }
    destroyMaterial(state.fallback);
    for (Slot& slot : state.slots)
    {
        if (slot.value != nullptr)
        {
            releaseMaterial(slot.value);
        }
    }
    for (PipelineEntry* entry : state.pipelines)
    {
        destroyPipelines(entry->pipelines);
        destroy(MemoryTag::Render, entry);
    }
    if (state.buffer != nullptr)
    {
        state.device->destroy(state.buffer); // the renderer waited for the GPU
    }
    state = Materials{};
}

Result<MaterialHandle> createMaterial(const MaterialInfo& info, std::span<const uint8_t> parameters,
                                      std::span<const MaterialTexture> textures)
{
    if (state.device == nullptr)
    {
        return Status::Unsupported;
    }
    bool zeroShader = true;
    for (uint8_t byte : info.shader)
    {
        zeroShader = zeroShader && byte == 0;
    }
    // Until game material shaders (M5 step 6b): the engine's two types.
    if (!zeroShader || info.type > static_cast<uint32_t>(assets::DefaultMaterialType::Pbr) ||
        parameters.size() != sizeof(shaders::DefaultMaterial))
    {
        return Status::InvalidData;
    }
    Result<MaterialPipelines*> pipelines =
        pipelinesFor({info.type, info.alphaMode == AlphaMode::Mask, info.doubleSided != 0});
    if (!pipelines)
    {
        return pipelines.status();
    }

    auto* material = create<LoadedMaterial>(MemoryTag::Render);
    material->pipelines = pipelines.value();
    material->size = static_cast<uint32_t>(parameters.size());
    material->offset = allocateRange(material->size);
    if (material->offset == UINT32_MAX)
    {
        destroy(MemoryTag::Render, material);
        std::fprintf(stderr, "crucible: the material buffer is full\n");
        return Status::OutOfDeviceMemory;
    }
    // Parameters with each texture's slot written in; a texture that can't
    // be had reads as none.
    auto* target = static_cast<uint8_t*>(state.buffer->mappedData()) + material->offset;
    std::memcpy(target, parameters.data(), parameters.size());
    for (const MaterialTexture& texture : textures)
    {
        if (texture.offset + sizeof(uint32_t) > parameters.size())
        {
            continue;
        }
        assets::AssetId id;
        std::memcpy(id.bytes, texture.texture, 16);
        uint32_t slot = 0;
        if (Result<TextureHandle> acquired = acquireTexture(id))
        {
            material->textures.pushBack(acquired.value());
            slot = textureDescriptor(acquired.value());
        }
        std::memcpy(target + texture.offset, &slot, sizeof(slot));
    }
    return add(material);
}

void destroyMaterial(MaterialHandle handle)
{
    if (LoadedMaterial* material = find(handle))
    {
        releaseMaterial(material);
        state.slots[handle.index].value = nullptr;
        ++state.slots[handle.index].generation;
        state.freeSlots.pushBack(handle.index);
    }
}

MaterialDraw materialDraw(MaterialHandle handle)
{
    const LoadedMaterial* material = find(handle);
    if (material == nullptr)
    {
        material = find(state.fallback);
    }
    return {material->pipelines, material->offset};
}

Result<void> rebuildMaterialPipelines()
{
    for (PipelineEntry* entry : state.pipelines)
    {
        destroyPipelines(entry->pipelines);
        if (Result<void> made = createPipelines(entry->key, entry->pipelines); !made)
        {
            return made;
        }
    }
    return {};
}

} // namespace crucible::render
