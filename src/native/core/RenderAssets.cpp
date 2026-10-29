#include "RenderInternal.h"

#include <crucible/BinaryFile.h>
#include <crucible/HashMap.h>
#include <crucible/Memory.h>
#include <crucible/RenderAssets.h>
#include <crucible/TextureFile.h>
#include <crucible/Vector.h>

#include <slag/Slag.h>
#include <slag/util/DeferredDestroyer.h>
#include <slag/util/DescriptorAllocator.h>

#include <algorithm>
#include <cstdio>
#include <cstring>

namespace crucible::render {

namespace {

using assets::AssetId;

// Resource slots below this stay with render steps (per-frame data, ADR 0049).
constexpr uint32_t firstTextureSlot = 1024;
// Sampler slots for Sampler's values, from 1 (0 reads as no sampler).
constexpr uint32_t firstSamplerSlot = 1;
// Stream offsets in a model's buffer: aligned for any view of it.
constexpr uint64_t streamAlignment = 256;

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

// One loaded model or texture, in a slot of a generational table.
struct LoadedMesh
{
    MeshData data{};
    Vector<uint8_t> tables{MemoryTag::Render}; // the file's tables, which data's spans point into
};

struct LoadedTexture
{
    slag::Texture* texture = nullptr;
    uint32_t slot = 0;
};

template <class T>
struct Slot
{
    T* value = nullptr;
    uint32_t generation = 1; // the handle's; bumped when freed
    uint32_t uses = 0;       // acquires not yet released (acquired ones)
    bool acquired = false;   // by asset ID, rather than loaded from bytes
    AssetId asset{};
};

template <class T>
struct Table
{
    Vector<Slot<T>> slots{MemoryTag::Render};
    Vector<uint32_t> free{MemoryTag::Render};
    HashMap<AssetId, uint32_t, AssetIdHash> byAsset{MemoryTag::Render};

    uint32_t add(T* value)
    {
        uint32_t index;
        if (!free.empty())
        {
            index = free[free.size() - 1];
            free.resize(free.size() - 1);
        }
        else
        {
            index = static_cast<uint32_t>(slots.size());
            slots.pushBack({});
        }
        slots[index].value = value;
        slots[index].uses = 0;
        slots[index].acquired = false;
        return index;
    }

    Slot<T>* find(uint32_t index, uint32_t generation)
    {
        return index < slots.size() && slots[index].value != nullptr && slots[index].generation == generation
                   ? &slots[index]
                   : nullptr;
    }

    void remove(uint32_t index)
    {
        slots[index].value = nullptr;
        ++slots[index].generation;
        free.pushBack(index);
    }
};

using AssetReader = int32_t (*)(const AssetId* id, void* context,
                                void (*deliver)(void* context, const uint8_t* data, uint64_t size));

struct Assets
{
    slag::GraphicsDevice* device = nullptr;
    slag::CommandQueue* queue = nullptr;
    slag::Semaphore* work = nullptr; // every frame and upload signals the next value
    uint64_t workSignaled = 0;
    slag::DeferredDestroyer* destroyer = nullptr;
    slag::DescriptorAllocator* slots = nullptr;
    Table<LoadedMesh> meshes;
    Table<LoadedTexture> textures;
};

Assets state;
AssetReader reader = nullptr;
bool stagingOnly = false; // tests

slag::SemaphoreValue lastSignaled()
{
    return {state.work, state.workSignaled};
}

Status toStatus(slag::Status status)
{
    switch (status)
    {
    case slag::Status::DeviceLost:
        return Status::DeviceLost;
    case slag::Status::OutOfDeviceMemory:
        return Status::OutOfDeviceMemory;
    case slag::Status::OutOfHostMemory:
        return Status::OutOfHostMemory;
    default:
        return Status::Unsupported;
    }
}

// Records commands into a one-off command buffer, submits them with the next
// work signal, and waits: uploads are synchronous for now (ADR 0049).
template <class Record>
Result<void> runAndWait(Record&& record)
{
    slag::Result<slag::CommandBuffer*> commands = state.device->createCommandBuffer({.debugName = "crucible upload"});
    if (!commands)
    {
        return toStatus(commands.status());
    }
    slag::CommandBuffer* commandBuffer = commands.value();
    Result<void> result;
    if (!commandBuffer->begin())
    {
        result = Status::Unsupported;
    }
    else
    {
        record(commandBuffer);
        slag::Result<void> done = commandBuffer->end();
        const slag::SemaphoreValue signal = workSignal();
        slag::CommandBuffer* const submitted[] = {commandBuffer};
        if (done)
        {
            done = state.queue->submit({.commandBuffers = submitted, .signals = {&signal, 1}});
        }
        if (done)
        {
            done = signal.semaphore->wait(signal.value, UINT64_MAX);
        }
        if (!done)
        {
            result = toStatus(done.status());
        }
    }
    state.device->destroy(commandBuffer);
    return result;
}

Status readSection(const binary::File& file, uint32_t tag, Vector<uint8_t>& out)
{
    const int32_t index = binary::findSection(file, tag);
    if (index < 0)
    {
        out.clear();
        return Status::Ok;
    }
    out.resize(binary::sectionInfo(file, static_cast<uint32_t>(index)).size);
    return binary::readSection(file, static_cast<uint32_t>(index), out.span()).status();
}

template <class T>
std::span<const T> tableOf(const Vector<uint8_t>& tables, uint64_t offset, uint64_t size)
{
    return {reinterpret_cast<const T*>(tables.data() + offset), static_cast<size_t>(size / sizeof(T))};
}

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

Result<LoadedMesh*> createMesh(std::span<const uint8_t> bytes)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::meshFileKind, assets::meshFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    const binary::File& file = *opened.value();
    auto* mesh = create<LoadedMesh>(MemoryTag::Render);
    Status status = Status::Ok;

    // The tables, kept on the CPU, back to back (each 16-byte aligned).
    const uint32_t tableTags[] = {binary::tag("INFO"), binary::tag("MSHS"), binary::tag("LODS"),
                                  binary::tag("RNGS"), binary::tag("NODE"), binary::tag("MATL")};
    uint64_t tableOffsets[6] = {}, tableSizes[6] = {};
    for (int t = 0; t < 6 && status == Status::Ok; ++t)
    {
        Vector<uint8_t> section(MemoryTag::Render);
        status = readSection(file, tableTags[t], section);
        mesh->tables.resize((mesh->tables.size() + 15) / 16 * 16);
        tableOffsets[t] = mesh->tables.size();
        tableSizes[t] = section.size();
        for (uint8_t value : section)
        {
            mesh->tables.pushBack(value);
        }
    }
    if (status == Status::Ok && tableSizes[0] != sizeof(assets::MeshFileInfo))
    {
        status = Status::InvalidData;
    }

    // The streams, in one GPU buffer.
    const uint32_t streamTags[] = {binary::tag("POSN"), binary::tag("NRML"), binary::tag("TNGT"), binary::tag("UV0_"),
                                   binary::tag("UV1_"), binary::tag("COLR"), binary::tag("INDX")};
    MeshData::Stream* streams[] = {&mesh->data.positions, &mesh->data.normals, &mesh->data.tangents, &mesh->data.uv0,
                                   &mesh->data.uv1,       &mesh->data.colors,  &mesh->data.indices};
    int32_t sectionIndex[7] = {};
    uint64_t total = 0;
    for (int s = 0; s < 7 && status == Status::Ok; ++s)
    {
        sectionIndex[s] = binary::findSection(file, streamTags[s]);
        const uint64_t size =
            sectionIndex[s] < 0 ? 0 : binary::sectionInfo(file, static_cast<uint32_t>(sectionIndex[s])).size;
        total = (total + streamAlignment - 1) / streamAlignment * streamAlignment;
        *streams[s] = {total, size};
        total += size;
    }
    if (status == Status::Ok && total == 0)
    {
        status = Status::InvalidData;
    }

    slag::Buffer* staging = nullptr;
    if (status == Status::Ok)
    {
        // Straight into GPU memory the CPU can write where the device has it
        // (resizable BAR), else through staging and a copy (ADR 0049).
        const bool direct = state.device->capabilities().gpuUploadMemory && !stagingOnly;
        slag::Result<slag::Buffer*> buffer = state.device->createBuffer(
            {.size = total,
             .placement = slag::Placement::automatic(direct ? slag::MemoryKind::GpuUpload : slag::MemoryKind::GpuOnly),
             .debugName = "crucible model"});
        slag::Result<slag::Buffer*> upload =
            direct ? slag::Result<slag::Buffer*>(nullptr)
                   : state.device->createBuffer({.size = total,
                                                 .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                                 .debugName = "crucible model staging"});
        if (!buffer || !upload)
        {
            status = toStatus(!buffer ? buffer.status() : upload.status());
            if (buffer)
            {
                state.device->destroy(buffer.value());
            }
        }
        else
        {
            mesh->data.buffer = buffer.value();
            staging = upload.value();
            auto* target = static_cast<uint8_t*>(direct ? mesh->data.buffer->mappedData() : staging->mappedData());
            for (int s = 0; s < 7 && status == Status::Ok; ++s)
            {
                if (sectionIndex[s] >= 0)
                {
                    status = binary::readSection(file, static_cast<uint32_t>(sectionIndex[s]),
                                                 {target + streams[s]->offset, streams[s]->size})
                                 .status();
                }
            }
        }
    }
    binary::closeFile(opened.value());
    if (status == Status::Ok && staging != nullptr)
    {
        slag::Buffer* destination = mesh->data.buffer;
        status = runAndWait([&](slag::CommandBuffer* commands) {
                     commands->copyBuffer(destination, 0, staging, 0, total);
                 }).status();
    }
    if (staging != nullptr)
    {
        state.device->destroy(staging);
    }
    if (status != Status::Ok)
    {
        if (mesh->data.buffer != nullptr)
        {
            state.device->destroy(mesh->data.buffer);
        }
        destroy(MemoryTag::Render, mesh);
        return status;
    }

    std::memcpy(&mesh->data.info, mesh->tables.data() + tableOffsets[0], sizeof(assets::MeshFileInfo));
    mesh->data.meshes = tableOf<assets::MeshEntry>(mesh->tables, tableOffsets[1], tableSizes[1]);
    mesh->data.lods = tableOf<assets::MeshLod>(mesh->tables, tableOffsets[2], tableSizes[2]);
    mesh->data.ranges = tableOf<assets::MeshRange>(mesh->tables, tableOffsets[3], tableSizes[3]);
    mesh->data.nodes = tableOf<assets::MeshNode>(mesh->tables, tableOffsets[4], tableSizes[4]);
    mesh->data.materials = tableOf<assets::MaterialEntry>(mesh->tables, tableOffsets[5], tableSizes[5]);
    return mesh;
}

void destroyMesh(LoadedMesh* mesh)
{
    state.destroyer->destroyAfter(mesh->data.buffer, lastSignaled());
    destroy(MemoryTag::Render, mesh);
}

// ---------------------------------------------------------------------------
// Textures
// ---------------------------------------------------------------------------

slag::PixelFormat pixelFormat(const assets::TextureInfo& info)
{
    switch (info.format)
    {
    case assets::TextureFormat::Bc7:
        return info.srgb != 0 ? slag::PixelFormat::BC7UnormSrgb : slag::PixelFormat::BC7Unorm;
    case assets::TextureFormat::Bc5:
        return slag::PixelFormat::BC5Unorm;
    case assets::TextureFormat::Bc4:
        return slag::PixelFormat::BC4Unorm;
    case assets::TextureFormat::Rgba8:
        return info.srgb != 0 ? slag::PixelFormat::R8G8B8A8UnormSrgb : slag::PixelFormat::R8G8B8A8Unorm;
    case assets::TextureFormat::Rgba16F:
        return slag::PixelFormat::R16G16B16A16Float;
    }
    return slag::PixelFormat::Undefined;
}

// Bytes in one tightly packed row of the mip as the file stores it: texels,
// or 4x4 blocks for compressed formats.
uint64_t packedRowBytes(assets::TextureFormat format, uint32_t width)
{
    const uint64_t blocks = (width + 3) / 4;
    switch (format)
    {
    case assets::TextureFormat::Bc7:
    case assets::TextureFormat::Bc5:
        return blocks * 16;
    case assets::TextureFormat::Bc4:
        return blocks * 8;
    case assets::TextureFormat::Rgba8:
        return uint64_t{width} * 4;
    case assets::TextureFormat::Rgba16F:
        return uint64_t{width} * 8;
    }
    return 0;
}

Result<LoadedTexture*> createTexture(std::span<const uint8_t> bytes)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::textureFileKind, assets::textureFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    const binary::File& file = *opened.value();
    Vector<uint8_t> infoBytes(MemoryTag::Render);
    Status status = readSection(file, binary::tag("INFO"), infoBytes);
    assets::TextureInfo info{};
    if (status == Status::Ok && infoBytes.size() == sizeof(info))
    {
        std::memcpy(&info, infoBytes.data(), sizeof(info));
    }
    else
    {
        status = Status::InvalidData;
    }
    const slag::PixelFormat format = pixelFormat(info);
    if (status == Status::Ok && (format == slag::PixelFormat::Undefined || info.mipCount == 0 || info.mipCount > 16))
    {
        status = Status::InvalidData;
    }

    slag::Texture* texture = nullptr;
    slag::Buffer* staging = nullptr;
    uint64_t stagingOffsets[16] = {};
    if (status == Status::Ok)
    {
        slag::Result<slag::Texture*> created = state.device->createTexture2D({.format = format,
                                                                              .width = info.width,
                                                                              .height = info.height,
                                                                              .mipCount = info.mipCount,
                                                                              .debugName = "crucible texture"});
        status = created ? Status::Ok : toStatus(created.status());
        texture = created ? created.value() : nullptr;
    }
    // Staging laid out as the copy wants it (Slag's footprints: rows padded
    // to 256 bytes), filled row by row from the file's packed rows.
    uint64_t stagingSize = 0;
    for (uint32_t mip = 0; status == Status::Ok && mip < info.mipCount; ++mip)
    {
        stagingOffsets[mip] = stagingSize;
        stagingSize += (state.device->copyFootprint(texture, mip).size + 511) / 512 * 512;
    }
    if (status == Status::Ok)
    {
        slag::Result<slag::Buffer*> upload =
            state.device->createBuffer({.size = stagingSize,
                                        .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                        .debugName = "crucible texture staging"});
        status = upload ? Status::Ok : toStatus(upload.status());
        staging = upload ? upload.value() : nullptr;
    }
    Vector<uint8_t> mipBytes(MemoryTag::Render);
    for (uint32_t mip = 0; status == Status::Ok && mip < info.mipCount; ++mip)
    {
        status = readSection(file, assets::textureMipTag(mip), mipBytes);
        const slag::CopyFootprint footprint = state.device->copyFootprint(texture, mip);
        const uint64_t rowBytes = packedRowBytes(info.format, footprint.width);
        if (status == Status::Ok && mipBytes.size() != rowBytes * footprint.rowCount)
        {
            status = Status::InvalidData;
        }
        auto* target = static_cast<uint8_t*>(staging->mappedData()) + stagingOffsets[mip];
        for (uint32_t row = 0; status == Status::Ok && row < footprint.rowCount; ++row)
        {
            std::memcpy(target + uint64_t{row} * footprint.rowPitch, mipBytes.data() + row * rowBytes, rowBytes);
        }
    }
    binary::closeFile(opened.value());

    if (status == Status::Ok)
    {
        status = runAndWait([&](slag::CommandBuffer* commands) {
                     const slag::TextureBarrier toCopy = {.syncAfter = slag::BarrierSync::Copy,
                                                          .accessAfter = slag::BarrierAccess::CopyDest,
                                                          .layoutBefore = slag::TextureLayout::Undefined,
                                                          .layoutAfter = slag::TextureLayout::CopyDest,
                                                          .texture = texture};
                     commands->barrier({}, {}, {&toCopy, 1});
                     for (uint32_t mip = 0; mip < info.mipCount; ++mip)
                     {
                         const slag::CopyFootprint footprint = state.device->copyFootprint(texture, mip);
                         commands->copyBufferToTexture({.texture = texture, .mip = mip},
                                                       {staging, stagingOffsets[mip], footprint.rowPitch},
                                                       footprint.width, footprint.height);
                     }
                     // Ready to sample from every later submit.
                     const slag::TextureBarrier toRead = {.syncBefore = slag::BarrierSync::Copy,
                                                          .syncAfter = slag::BarrierSync::None,
                                                          .accessBefore = slag::BarrierAccess::CopyDest,
                                                          .accessAfter = slag::BarrierAccess::NoAccess,
                                                          .layoutBefore = slag::TextureLayout::CopyDest,
                                                          .layoutAfter = slag::TextureLayout::ShaderRead,
                                                          .texture = texture};
                     commands->barrier({}, {}, {&toRead, 1});
                 }).status();
    }
    if (staging != nullptr)
    {
        state.device->destroy(staging);
    }
    uint32_t slot = 0;
    if (status == Status::Ok)
    {
        state.slots->collect();
        slot = state.slots->allocate();
        status = slot == 0 ? Status::OutOfDeviceMemory : Status::Ok;
    }
    if (status != Status::Ok)
    {
        if (texture != nullptr)
        {
            state.device->destroy(texture);
        }
        return status;
    }
    state.device->writeTexture(slot, {.texture = texture});
    auto* loaded = create<LoadedTexture>(MemoryTag::Render);
    loaded->texture = texture;
    loaded->slot = slot;
    return loaded;
}

void destroyTexture(LoadedTexture* texture)
{
    state.slots->release(texture->slot, lastSignaled());
    state.destroyer->destroyAfter(texture->texture, lastSignaled());
    destroy(MemoryTag::Render, texture);
}

// ---------------------------------------------------------------------------
// Acquiring by asset ID
// ---------------------------------------------------------------------------

template <class T, class Create>
Result<uint32_t> acquire(Table<T>& table, const AssetId& id, Create create)
{
    if (state.device == nullptr)
    {
        return Status::Unsupported;
    }
    if (const uint32_t* found = table.byAsset.find(id))
    {
        ++table.slots[*found].uses;
        return *found;
    }
    if (reader == nullptr)
    {
        std::fprintf(stderr, "crucible: an asset was acquired before C# set the asset reader\n");
        return Status::NotFound;
    }
    struct Load
    {
        Create create;
        Status status = Status::NotFound;
        T* value = nullptr;
    } load{create};
    auto deliverTo = [](void* context, const uint8_t* data, uint64_t size) {
        auto& self = *static_cast<Load*>(context);
        Result<T*> created = self.create(std::span<const uint8_t>(data, static_cast<size_t>(size)));
        self.status = created.status();
        self.value = created ? created.value() : nullptr;
    };
    if (reader(&id, &load, deliverTo) != 0)
    {
        return Status::NotFound;
    }
    if (load.value == nullptr)
    {
        return load.status == Status::Ok ? Status::InvalidData : load.status;
    }
    const uint32_t index = table.add(load.value);
    table.slots[index].uses = 1;
    table.slots[index].acquired = true;
    table.slots[index].asset = id;
    table.byAsset.insertOrAssign(id, index);
    return index;
}

template <class T, class Destroy>
void release(Table<T>& table, uint32_t index, uint32_t generation, bool acquired, Destroy destroyValue)
{
    Slot<T>* slot = table.find(index, generation);
    if (slot == nullptr)
    {
        return; // stale or none
    }
    if (slot->acquired != acquired)
    {
        std::fprintf(stderr, "crucible: %s\n",
                     acquired ? "release of something loaded from bytes (use unload)"
                              : "unload of something acquired by asset ID (use release)");
        return;
    }
    if (acquired && --slot->uses > 0)
    {
        return;
    }
    if (acquired)
    {
        table.byAsset.erase(slot->asset);
    }
    destroyValue(slot->value);
    table.remove(index);
}

} // namespace

// ---------------------------------------------------------------------------
// The renderer's hooks (RenderInternal.h)
// ---------------------------------------------------------------------------

Result<void> startAssets(slag::GraphicsDevice& device, slag::CommandQueue& queue)
{
    state.device = &device;
    state.queue = &queue;
    slag::Result<slag::Semaphore*> work = device.createSemaphore({.debugName = "crucible work"});
    if (!work)
    {
        return toStatus(work.status());
    }
    state.work = work.value();
    state.workSignaled = 0;
    state.destroyer = create<slag::DeferredDestroyer>(MemoryTag::Render, &device);
    state.slots = create<slag::DescriptorAllocator>(
        MemoryTag::Render, &device,
        slag::DescriptorAllocatorDesc{.heap = slag::DescriptorHeap::Resource,
                                      .firstSlot = firstTextureSlot,
                                      .slotCount = slag::defaults::resourceHeapSize - firstTextureSlot});
    const slag::SamplerDescriptor samplers[] = {
        {.addressU = slag::AddressMode::Wrap,
         .addressV = slag::AddressMode::Wrap,
         .addressW = slag::AddressMode::Wrap,
         .maxAnisotropy = slag::limits::maxAnisotropy},
        {.addressU = slag::AddressMode::Clamp,
         .addressV = slag::AddressMode::Clamp,
         .addressW = slag::AddressMode::Clamp,
         .maxAnisotropy = slag::limits::maxAnisotropy},
        {.minFilter = slag::Filter::Point,
         .magFilter = slag::Filter::Point,
         .mipFilter = slag::Filter::Point,
         .addressU = slag::AddressMode::Clamp,
         .addressV = slag::AddressMode::Clamp,
         .addressW = slag::AddressMode::Clamp},
    };
    for (uint32_t i = 0; i < 3; ++i)
    {
        device.writeSampler(firstSamplerSlot + i, samplers[i]);
    }
    return {};
}

void collectAssets()
{
    if (state.destroyer != nullptr)
    {
        state.destroyer->collect();
    }
}

slag::Texture* textureOf(TextureHandle texture)
{
    const Slot<LoadedTexture>* slot = state.textures.find(texture.index, texture.generation);
    return slot != nullptr ? slot->value->texture : nullptr;
}

void forceStagingUploads(bool force)
{
    stagingOnly = force;
}

slag::SemaphoreValue workSignal()
{
    return {state.work, ++state.workSignaled};
}

void stopAssets()
{
    if (state.device == nullptr)
    {
        return;
    }
    // Whatever is still loaded goes with the renderer.
    for (uint32_t i = 0; i < state.meshes.slots.size(); ++i)
    {
        if (state.meshes.slots[i].value != nullptr)
        {
            destroyMesh(state.meshes.slots[i].value);
            state.meshes.remove(i);
        }
    }
    for (uint32_t i = 0; i < state.textures.slots.size(); ++i)
    {
        if (state.textures.slots[i].value != nullptr)
        {
            destroyTexture(state.textures.slots[i].value);
            state.textures.remove(i);
        }
    }
    state.meshes.byAsset.clear();
    state.textures.byAsset.clear();
    destroy(MemoryTag::Render, state.destroyer); // flushes
    destroy(MemoryTag::Render, state.slots);
    state.device->destroy(state.work);
    state.destroyer = nullptr;
    state.slots = nullptr;
    state.work = nullptr;
    state.device = nullptr;
    state.queue = nullptr;
}

// ---------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------

Result<MeshHandle> acquireMesh(assets::AssetId model)
{
    Result<uint32_t> index = acquire(state.meshes, model, createMesh);
    if (!index)
    {
        return index.status();
    }
    return MeshHandle{index.value(), state.meshes.slots[index.value()].generation};
}

void releaseMesh(MeshHandle mesh)
{
    release(state.meshes, mesh.index, mesh.generation, true, destroyMesh);
}

Result<TextureHandle> acquireTexture(assets::AssetId texture)
{
    Result<uint32_t> index = acquire(state.textures, texture, createTexture);
    if (!index)
    {
        return index.status();
    }
    return TextureHandle{index.value(), state.textures.slots[index.value()].generation};
}

void releaseTexture(TextureHandle texture)
{
    release(state.textures, texture.index, texture.generation, true, destroyTexture);
}

Result<MeshHandle> loadMesh(std::span<const uint8_t> bytes)
{
    if (state.device == nullptr)
    {
        return Status::Unsupported;
    }
    Result<LoadedMesh*> mesh = createMesh(bytes);
    if (!mesh)
    {
        return mesh.status();
    }
    const uint32_t index = state.meshes.add(mesh.value());
    return MeshHandle{index, state.meshes.slots[index].generation};
}

void unloadMesh(MeshHandle mesh)
{
    release(state.meshes, mesh.index, mesh.generation, false, destroyMesh);
}

Result<TextureHandle> loadTexture(std::span<const uint8_t> bytes)
{
    if (state.device == nullptr)
    {
        return Status::Unsupported;
    }
    Result<LoadedTexture*> texture = createTexture(bytes);
    if (!texture)
    {
        return texture.status();
    }
    const uint32_t index = state.textures.add(texture.value());
    return TextureHandle{index, state.textures.slots[index].generation};
}

void unloadTexture(TextureHandle texture)
{
    release(state.textures, texture.index, texture.generation, false, destroyTexture);
}

uint32_t textureDescriptor(TextureHandle texture)
{
    const Slot<LoadedTexture>* slot = state.textures.find(texture.index, texture.generation);
    return slot != nullptr ? slot->value->slot : 0;
}

uint32_t samplerDescriptor(Sampler sampler)
{
    return firstSamplerSlot + static_cast<uint32_t>(sampler);
}

void setAssetReader(uint64_t readerAddress)
{
    reader = reinterpret_cast<AssetReader>(static_cast<uintptr_t>(readerAddress));
}

const MeshData* meshData(MeshHandle mesh)
{
    const Slot<LoadedMesh>* slot = state.meshes.find(mesh.index, mesh.generation);
    return slot != nullptr ? &slot->value->data : nullptr;
}

} // namespace crucible::render
