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

#include <crucible/HashMap.h>

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

// What decides a material's pipelines: its shader (zero for the engine's)
// and material type, whether it's alpha-tested, and whether both faces are
// drawn.
struct PipelineKey
{
    assets::AssetId shader;
    uint32_t type;
    bool mask;
    bool doubleSided;

    bool operator==(const PipelineKey&) const = default;
};

// A game's material shader (a .crshader with MTYP), loaded for its
// materials and kept until the renderer stops, with the pipelines made from
// it (shader hot reload, with the editor, will replace them).
struct GameShader
{
    ShaderFile* file = nullptr;
    Vector<uint8_t> types{MemoryTag::Render}; // the MTYP section
};

struct AssetIdHash
{
    uint64_t operator()(const assets::AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
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
    float alphaCutoff = 0;
    assets::AssetId shader{}; // zero for the engine's
    uint32_t type = 0;
    // Acquired by asset ID (counted), rather than made for a model.
    bool acquired = false;
    uint32_t uses = 0;
    assets::AssetId asset{};
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
    HashMap<assets::AssetId, GameShader*, AssetIdHash> shaders{MemoryTag::Render};
    HashMap<assets::AssetId, MaterialHandle, AssetIdHash> byAsset{MemoryTag::Render};
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

bool isZero(const assets::AssetId& id)
{
    for (uint8_t byte : id.bytes)
    {
        if (byte != 0)
        {
            return false;
        }
    }
    return true;
}

// A pipeline from a loaded game shader's entry points.
Result<slag::Pipeline*> createGamePipeline(const ShaderFile& file, const char* vertexEntry, const char* pixelEntry,
                                           slag::GraphicsPipelineDesc desc)
{
    const int32_t vertex = findShaderEntry(file, vertexEntry);
    const int32_t pixel = findShaderEntry(file, pixelEntry);
    if (vertex < 0 || pixel < 0)
    {
        return Status::InvalidData;
    }
    shaderStage(file, static_cast<uint32_t>(vertex), desc.vertex);
    shaderStage(file, static_cast<uint32_t>(pixel), desc.pixel);
    slag::Result<slag::Pipeline*> pipeline = state.device->createGraphicsPipeline(desc);
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    return pipeline.value();
}

// The name of material type `type` in a game shader's MTYP section, or empty.
std::string_view typeName(const GameShader& shader, uint32_t type, uint32_t* size = nullptr)
{
    const Vector<uint8_t>& types = shader.types;
    uint32_t count = 0;
    if (types.size() < 8)
    {
        return {};
    }
    std::memcpy(&count, types.data(), 4);
    if (type >= count || 8 + (type + 1) * sizeof(assets::MaterialTypeInfo) > types.size())
    {
        return {};
    }
    assets::MaterialTypeInfo info;
    std::memcpy(&info, types.data() + 8 + type * sizeof(info), sizeof(info));
    if (uint64_t{info.nameOffset} + info.nameLength > types.size())
    {
        return {};
    }
    if (size != nullptr)
    {
        *size = info.size;
    }
    return {reinterpret_cast<const char*>(types.data()) + info.nameOffset, info.nameLength};
}

Result<void> createPipelines(const PipelineKey& key, MaterialPipelines& out)
{
    const uint32_t samples = msaaSamples();
    // Clockwise front faces (ADR 0024); double-sided materials cull nothing.
    const slag::RasterizerState rasterizer{.cullMode = key.doubleSided ? slag::CullMode::None : slag::CullMode::Back,
                                           .frontCounterClockwise = false};
    if (!isZero(key.shader))
    {
        GameShader* const* found = state.shaders.find(key.shader);
        if (found == nullptr)
        {
            return Status::InvalidData;
        }
        const ShaderFile& file = *(*found)->file;
        const std::string_view name = typeName(**found, key.type);
        char forwardEntry[256], maskEntry[256];
        std::snprintf(forwardEntry, sizeof(forwardEntry), "%.*s_forward", static_cast<int>(name.size()), name.data());
        std::snprintf(maskEntry, sizeof(maskEntry), "%.*s_mask", static_cast<int>(name.size()), name.data());
        const slag::DepthStencilState depthState{
            .depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::Greater};
        Result<slag::Pipeline*> depth = key.mask ? createGamePipeline(file, "materialVertex", maskEntry,
                                                                      {.rasterizer = rasterizer,
                                                                       .depthStencil = depthState,
                                                                       .depthStencilFormat = sceneDepthFormat,
                                                                       .sampleCount = samples,
                                                                       .debugName = "DepthPrepass (alpha-tested)"})
                                                 : createPipeline("mesh", "depthVertex", nullptr,
                                                                  {.rasterizer = rasterizer,
                                                                   .depthStencil = depthState,
                                                                   .depthStencilFormat = sceneDepthFormat,
                                                                   .sampleCount = samples,
                                                                   .debugName = "DepthPrepass"});
        if (!depth)
        {
            return depth.status();
        }
        const slag::PixelFormat formats[] = {sceneColorFormat};
        Result<slag::Pipeline*> forward = createGamePipeline(
            file, "materialVertex", forwardEntry,
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

// A game's material shader, read through the asset reader on first use.
Result<GameShader*> loadGameShader(const assets::AssetId& id)
{
    if (GameShader** found = state.shaders.find(id))
    {
        return *found;
    }
    Vector<uint8_t> bytes(MemoryTag::Render);
    if (Status read = readAsset(id, bytes); read != Status::Ok)
    {
        std::fprintf(stderr, "crucible: a material's shader couldn't be read (status %d)\n", static_cast<int>(read));
        return read;
    }
    Result<binary::File*> opened = binary::parseFile(bytes.span(), shaderFileKind, shaderFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    auto* shader = create<GameShader>(MemoryTag::Render);
    const int32_t types = binary::findSection(*opened.value(), assets::shaderMaterialTypesTag);
    Status status = types < 0 ? Status::InvalidData : Status::Ok;
    if (status == Status::Ok)
    {
        shader->types.resize(binary::sectionInfo(*opened.value(), static_cast<uint32_t>(types)).size);
        status = binary::readSection(*opened.value(), static_cast<uint32_t>(types), shader->types.span()).status();
    }
    binary::closeFile(opened.value());
    Result<ShaderFile*> file = status == Status::Ok ? loadShaderFile(bytes.span()) : Result<ShaderFile*>(status);
    if (!file)
    {
        destroy(MemoryTag::Render, shader);
        return file.status();
    }
    shader->file = file.value();
    state.shaders.insertOrAssign(id, shader);
    return shader;
}

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
    for (auto& entry : state.shaders)
    {
        destroyShaderFile(entry.value->file);
        destroy(MemoryTag::Render, entry.value);
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
    assets::AssetId shader;
    std::memcpy(shader.bytes, info.shader, 16);
    if (isZero(shader))
    {
        // The engine's two types.
        if (info.type > static_cast<uint32_t>(assets::DefaultMaterialType::Pbr) ||
            parameters.size() != sizeof(shaders::DefaultMaterial))
        {
            return Status::InvalidData;
        }
    }
    else
    {
        Result<GameShader*> loaded = loadGameShader(shader);
        if (!loaded)
        {
            return loaded.status();
        }
        uint32_t size = 0;
        if (typeName(*loaded.value(), info.type, &size).empty() || parameters.size() != size)
        {
            std::fprintf(stderr, "crucible: a material doesn't match its shader's material type (reimport it)\n");
            return Status::InvalidData;
        }
    }
    Result<MaterialPipelines*> pipelines =
        pipelinesFor({shader, info.type, info.alphaMode == AlphaMode::Mask, info.doubleSided != 0});
    if (!pipelines)
    {
        return pipelines.status();
    }

    auto* material = create<LoadedMaterial>(MemoryTag::Render);
    material->pipelines = pipelines.value();
    material->size = static_cast<uint32_t>(parameters.size());
    material->alphaCutoff = info.alphaMode == AlphaMode::Mask ? info.alphaCutoff : 0.0f;
    material->shader = shader;
    material->type = info.type;
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
        if (material->acquired)
        {
            state.byAsset.erase(material->asset);
        }
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
    return {material->pipelines, material->offset, material->alphaCutoff};
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

Result<MaterialHandle> createMaterialFromRecords(std::span<const uint8_t> section, uint32_t index)
{
    if (section.size() < uint64_t{index + 1} * sizeof(MaterialInfo))
    {
        return Status::InvalidData;
    }
    MaterialInfo info;
    std::memcpy(&info, section.data() + index * sizeof(info), sizeof(info));
    const uint64_t textureBytes = uint64_t{info.textureCount} * sizeof(MaterialTexture);
    if (uint64_t{info.parameterOffset} + info.parameterSize > section.size() ||
        info.textureOffset + textureBytes > section.size())
    {
        return Status::InvalidData;
    }
    Vector<MaterialTexture> textures(MemoryTag::Render);
    textures.resize(info.textureCount);
    if (textureBytes != 0)
    {
        std::memcpy(textures.data(), section.data() + info.textureOffset, textureBytes);
    }
    return createMaterial(info, section.subspan(info.parameterOffset, info.parameterSize), textures.span());
}

Result<MaterialHandle> acquireMaterial(assets::AssetId id)
{
    if (state.device == nullptr)
    {
        return Status::Unsupported;
    }
    if (MaterialHandle* found = state.byAsset.find(id))
    {
        ++find(*found)->uses;
        return *found;
    }
    Vector<uint8_t> bytes(MemoryTag::Render);
    if (Status read = readAsset(id, bytes); read != Status::Ok)
    {
        return read;
    }
    Result<binary::File*> opened =
        binary::parseFile(bytes.span(), assets::materialFileKind, assets::materialFileVersion);
    if (!opened)
    {
        return Status::InvalidData;
    }
    Vector<uint8_t> section(MemoryTag::Render);
    const int32_t index = binary::findSection(*opened.value(), assets::meshMaterialsTag);
    Status status = index < 0 ? Status::InvalidData : Status::Ok;
    if (status == Status::Ok)
    {
        section.resize(binary::sectionInfo(*opened.value(), static_cast<uint32_t>(index)).size);
        status = binary::readSection(*opened.value(), static_cast<uint32_t>(index), section.span()).status();
    }
    binary::closeFile(opened.value());
    if (status != Status::Ok)
    {
        return status;
    }
    Result<MaterialHandle> made = createMaterialFromRecords(section.span(), 0);
    if (!made)
    {
        return made.status();
    }
    LoadedMaterial& material = *find(made.value());
    material.acquired = true;
    material.uses = 1;
    material.asset = id;
    state.byAsset.insertOrAssign(id, made.value());
    return made.value();
}

void releaseMaterial(MaterialHandle handle)
{
    LoadedMaterial* material = find(handle);
    if (material == nullptr)
    {
        return; // stale or none
    }
    if (!material->acquired)
    {
        std::fprintf(stderr, "crucible: releaseMaterial on a material that wasn't acquired; ignored\n");
        return;
    }
    if (--material->uses == 0)
    {
        destroyMaterial(handle);
    }
}

Result<void> setMaterialParameters(MaterialHandle handle, std::span<const uint8_t> parameters)
{
    LoadedMaterial* material = find(handle);
    if (material == nullptr)
    {
        return Status::NotFound;
    }
    if (parameters.size() != material->size)
    {
        return Status::InvalidData;
    }
    // A fresh copy, so frames in flight keep reading the old one, which is
    // freed once they're done.
    const uint32_t offset = allocateRange(material->size);
    if (offset == UINT32_MAX)
    {
        std::fprintf(stderr, "crucible: the material buffer is full\n");
        return Status::OutOfDeviceMemory;
    }
    std::memcpy(static_cast<uint8_t*>(state.buffer->mappedData()) + offset, parameters.data(), parameters.size());
    freeRange(material->offset, material->size);
    material->offset = offset;
    return {};
}

assets::AssetId materialShader(MaterialHandle handle)
{
    const LoadedMaterial* material = find(handle);
    return material != nullptr ? material->shader : assets::AssetId{};
}

std::string_view materialTypeName(MaterialHandle handle)
{
    const LoadedMaterial* material = find(handle);
    if (material == nullptr)
    {
        return {};
    }
    if (isZero(material->shader))
    {
        return material->type == static_cast<uint32_t>(assets::DefaultMaterialType::Pbr) ? "DefaultPbr" : "DefaultToon";
    }
    GameShader* const* shader = state.shaders.find(material->shader);
    return shader != nullptr ? typeName(**shader, material->type) : std::string_view();
}

} // namespace crucible::render
