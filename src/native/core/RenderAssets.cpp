#include "RenderInternal.h"

#include <crucible/AssetLoads.h>
#include <crucible/BinaryFile.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Log.h>
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
#include <mutex>

namespace crucible::render {

namespace {

using assets::AssetId;

// Resource slots below this stay with render steps (per-frame data, ADR 0049).
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
    // One per slot (MeshFileInfo::materialCount), from the MTLS section;
    // none for a file without one, whose slots draw the fallback.
    Vector<MaterialHandle> materials{MemoryTag::Render};
    Vector<bool> acquiredMaterials{MemoryTag::Render}; // remapped slots' (released, not destroyed)
    Vector<Vector3> boneOrigins{MemoryTag::Render};    // data.boneOrigins
    Vector<float> boneReach{MemoryTag::Render};        // data.boneReach
};

struct LoadedTexture
{
    slag::Texture* texture = nullptr;
    uint32_t slot = 0;
    bool borrowed = false; // a surface's: the surface destroys it
};

template <class T>
struct Slot
{
    T* value = nullptr;
    uint32_t generation = 1; // the handle's; bumped when freed
    uint32_t uses = 0;       // acquires not yet released (acquired ones)
    bool acquired = false;   // by asset ID, rather than loaded from bytes
    // Loaded (ADR 0099): until then value is a placeholder (a texture's
    // keeps its descriptor slot, which materials already hold).
    bool ready = true;
    // A model whose materials' textures are all ready too: only these draw.
    bool drawable = false;
    Status failure = Status::Ok; // why its load failed; it stays a placeholder

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
        slots[index].ready = true;
        slots[index].drawable = false;
        slots[index].failure = Status::Ok;
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

struct Assets
{
    slag::GraphicsDevice* device = nullptr;
    slag::CommandQueue* queue = nullptr;
    slag::Semaphore* work = nullptr; // every frame signals the next value
    uint64_t workSignaled = 0;
    // Uploads signal their own values, under queueMutex (ADR 0099).
    slag::Semaphore* uploads = nullptr;
    uint64_t uploadsSignaled = 0;
    slag::DeferredDestroyer* destroyer = nullptr;
    slag::DescriptorAllocator* slots = nullptr;
    Table<LoadedMesh> meshes;
    Table<LoadedTexture> textures;
};

Assets state;
bool stagingOnly = false; // tests
std::mutex queueMutex;

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

// An upload submitted (ADR 0099): its command buffer, kept until the GPU has
// run it, and the uploads semaphore's value it signals. None for a model
// written straight into GPU memory.
struct Upload
{
    slag::CommandBuffer* commands = nullptr;
    uint64_t value = 0;
};

// Records commands into a one-off command buffer and submits them to the
// graphics queue, signalling the next upload value. Any thread.
template <class Record>
Result<Upload> submitUpload(Record&& record)
{
    slag::Result<slag::CommandBuffer*> commands = state.device->createCommandBuffer({.debugName = "crucible upload"});
    if (!commands)
    {
        return toStatus(commands.status());
    }
    slag::CommandBuffer* commandBuffer = commands.value();
    slag::Result<void> done = commandBuffer->begin();
    if (done)
    {
        record(commandBuffer);
        done = commandBuffer->end();
    }
    Upload upload{commandBuffer, 0};
    if (done)
    {
        // Values are signalled in the order they're taken.
        std::lock_guard lock(queueMutex);
        upload.value = ++state.uploadsSignaled;
        const slag::SemaphoreValue signal{state.uploads, upload.value};
        slag::CommandBuffer* const submitted[] = {commandBuffer};
        done = state.queue->submit({.commandBuffers = submitted, .signals = {&signal, 1}});
    }
    if (!done)
    {
        state.device->destroy(commandBuffer);
        return toStatus(done.status());
    }
    return upload;
}

// Whether the GPU has run the upload; with wait, once it has (or failed).
bool uploadDone(const Upload& upload, bool wait)
{
    if (upload.commands == nullptr)
    {
        return true;
    }
    if (wait)
    {
        (void)state.uploads->wait(upload.value, UINT64_MAX);
        return true;
    }
    return state.uploads->value() >= upload.value;
}

void releaseUpload(Upload& upload)
{
    if (upload.commands != nullptr)
    {
        state.device->destroy(upload.commands);
    }
    upload = {};
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

void destroyMesh(LoadedMesh* mesh);

// The model's materials (ADR 0052), one per slot: each slot's generated one,
// or the authored material its import settings remapped it to.
Status createMaterials(LoadedMesh& mesh, const Vector<uint8_t>& generated, const Vector<uint8_t>& remapped)
{
    if (generated.empty())
    {
        return Status::Ok;
    }
    const uint32_t count = mesh.data.info.materialCount;
    if (!remapped.empty() && remapped.size() != uint64_t{count} * 16)
    {
        return Status::InvalidData;
    }
    for (uint32_t m = 0; m < count; ++m)
    {
        AssetId authored{};
        if (!remapped.empty())
        {
            std::memcpy(authored.bytes, remapped.data() + m * 16, 16);
        }
        bool remap = false;
        for (uint8_t byte : authored.bytes)
        {
            remap = remap || byte != 0;
        }
        if (remap)
        {
            Result<MaterialHandle> material = acquireMaterial(authored);
            if (material)
            {
                mesh.materials.pushBack(material.value());
                mesh.acquiredMaterials.pushBack(true);
                continue;
            }
            // The generated one stands in: a missing material shouldn't
            // keep the model from loading.
            log::warning("a model's slot %u names a material that can't be loaded (status %d)", m,
                         static_cast<int>(material.status()));
        }
        Result<MaterialHandle> material = createMaterialFromRecords(generated.span(), m);
        if (!material)
        {
            return material.status();
        }
        mesh.materials.pushBack(material.value());
        mesh.acquiredMaterials.pushBack(false);
    }
    return Status::Ok;
}

// Each bone's bind-pose origin (its inverse bind, inverted) and how far
// the vertices following it are from there, for culling posed instances
// (ADR 0080).
void measureBones(LoadedMesh& mesh, const Vector<uint8_t>& positions, const Vector<uint8_t>& bones,
                  const Vector<uint8_t>& weights)
{
    const uint32_t influences = mesh.data.skinInfluences;
    const size_t boneCount = mesh.data.bones.size();
    mesh.boneOrigins.resize(boneCount);
    mesh.boneReach.resize(boneCount);
    for (size_t j = 0; j < boneCount; ++j)
    {
        Matrix4x4 inverseBind, bind;
        std::memcpy(&inverseBind, mesh.data.bones[j].inverseBind, sizeof(inverseBind));
        mesh.boneOrigins[j] =
            invert(inverseBind, bind) ? Vector3{bind.rows[3].x, bind.rows[3].y, bind.rows[3].z} : Vector3{};
        mesh.boneReach[j] = -1.0f;
    }
    const size_t vertexCount = positions.size() / 12;
    if (influences == 0 || bones.size() < vertexCount * influences * 2 || weights.size() < vertexCount * influences)
    {
        mesh.data.skinInfluences = 0;
    }
    else
    {
        for (size_t v = 0; v < vertexCount; ++v)
        {
            Vector3 p;
            std::memcpy(&p, positions.data() + v * 12, 12);
            for (uint32_t k = 0; k < influences; ++k)
            {
                uint16_t bone;
                std::memcpy(&bone, bones.data() + (v * influences + k) * 2, 2);
                if (weights[v * influences + k] == 0 || bone >= boneCount)
                {
                    continue;
                }
                mesh.boneReach[bone] = std::max(mesh.boneReach[bone], length(p - mesh.boneOrigins[bone]));
            }
        }
    }
    mesh.data.boneOrigins = mesh.boneOrigins.span();
    mesh.data.boneReach = mesh.boneReach.span();
}

// A model as a job makes it (ADR 0099): its tables, its buffer filled (and
// the upload from staging submitted), its bones measured. Its descriptor
// slot and materials come from the main thread's tables, in finishMesh.
struct PreparedMesh
{
    LoadedMesh* mesh = nullptr;
    slag::Buffer* staging = nullptr;
    Upload upload;
    uint64_t size = 0; // the buffer's
    Vector<uint8_t> generated{MemoryTag::Render};
    Vector<uint8_t> remapped{MemoryTag::Render};
};

// What prepareMesh made, freed (a failed or abandoned load). Waits for the
// upload, which is rare enough not to matter.
void discardMesh(PreparedMesh& prepared)
{
    uploadDone(prepared.upload, true);
    releaseUpload(prepared.upload);
    if (prepared.staging != nullptr)
    {
        state.device->destroy(prepared.staging);
        prepared.staging = nullptr;
    }
    if (prepared.mesh != nullptr)
    {
        if (prepared.mesh->data.buffer != nullptr)
        {
            state.device->destroy(prepared.mesh->data.buffer);
        }
        destroy(MemoryTag::Render, prepared.mesh);
        prepared.mesh = nullptr;
    }
}

// Any thread.
Status prepareMesh(std::span<const uint8_t> bytes, PreparedMesh& prepared)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::meshFileKind, assets::meshFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    const binary::File& file = *opened.value();
    auto* mesh = create<LoadedMesh>(MemoryTag::Render);
    prepared.mesh = mesh;
    Status status = Status::Ok;
    // The tables, kept on the CPU, back to back (each 16-byte aligned).
    const uint32_t tableTags[] = {binary::tag("INFO"), binary::tag("MSHS"), binary::tag("LODS"),
                                  binary::tag("RNGS"), binary::tag("NODE"), binary::tag("MATL"),
                                  binary::tag("BONE"), binary::tag("NAME"), binary::tag("SKIN")};
    constexpr int tableCount = static_cast<int>(std::size(tableTags));
    uint64_t tableOffsets[tableCount] = {}, tableSizes[tableCount] = {};
    for (int t = 0; t < tableCount && status == Status::Ok; ++t)
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
                                   binary::tag("UV1_"), binary::tag("COLR"), binary::tag("ONRM"), binary::tag("BIDX"),
                                   binary::tag("BWGT"), binary::tag("INDX")};
    MeshData::Stream* streams[] = {
        &mesh->data.positions,   &mesh->data.normals, &mesh->data.tangents,       &mesh->data.uv0,
        &mesh->data.uv1,         &mesh->data.colors,  &mesh->data.outlineNormals, &mesh->data.boneIndices,
        &mesh->data.boneWeights, &mesh->data.indices};
    constexpr int streamCount = static_cast<int>(std::size(streamTags));
    int32_t sectionIndex[streamCount] = {};
    uint64_t total = 0;
    for (int s = 0; s < streamCount && status == Status::Ok; ++s)
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
    // Raw-buffer views (vertex pulling) cover whole 4-byte words.
    total = (total + 3) / 4 * 4;

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
            prepared.staging = upload.value();
            slag::Buffer* staging = prepared.staging;
            auto* target = static_cast<uint8_t*>(direct ? mesh->data.buffer->mappedData() : staging->mappedData());
            for (int s = 0; s < streamCount && status == Status::Ok; ++s)
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
    // The skin's vertices, on the CPU too, for each bone's reach.
    Vector<uint8_t> skinPositions(MemoryTag::Render), skinBones(MemoryTag::Render), skinWeights(MemoryTag::Render);
    if (status == Status::Ok && tableSizes[6] > 0)
    {
        status = readSection(file, binary::tag("POSN"), skinPositions);
        status = status == Status::Ok ? readSection(file, binary::tag("BIDX"), skinBones) : status;
        status = status == Status::Ok ? readSection(file, binary::tag("BWGT"), skinWeights) : status;
    }
    if (status == Status::Ok)
    {
        status = readSection(file, assets::meshMaterialsTag, prepared.generated);
    }
    if (status == Status::Ok)
    {
        status = readSection(file, assets::meshRemappedMaterialsTag, prepared.remapped);
    }
    binary::closeFile(opened.value());
    prepared.size = total;
    if (status == Status::Ok && prepared.staging != nullptr)
    {
        slag::Buffer* destination = mesh->data.buffer;
        slag::Buffer* staging = prepared.staging;
        Result<Upload> submitted = submitUpload(
            [&](slag::CommandBuffer* commands) { commands->copyBuffer(destination, 0, staging, 0, total); });
        status = submitted.status();
        if (submitted)
        {
            prepared.upload = submitted.value();
        }
    }
    if (status != Status::Ok)
    {
        discardMesh(prepared);
        return status;
    }

    std::memcpy(&mesh->data.info, mesh->tables.data() + tableOffsets[0], sizeof(assets::MeshFileInfo));
    mesh->data.meshes = tableOf<assets::MeshEntry>(mesh->tables, tableOffsets[1], tableSizes[1]);
    mesh->data.lods = tableOf<assets::MeshLod>(mesh->tables, tableOffsets[2], tableSizes[2]);
    mesh->data.ranges = tableOf<assets::MeshRange>(mesh->tables, tableOffsets[3], tableSizes[3]);
    mesh->data.nodes = tableOf<assets::MeshNode>(mesh->tables, tableOffsets[4], tableSizes[4]);
    mesh->data.materials = tableOf<assets::MaterialEntry>(mesh->tables, tableOffsets[5], tableSizes[5]);
    mesh->data.bones = tableOf<assets::BoneEntry>(mesh->tables, tableOffsets[6], tableSizes[6]);
    mesh->data.names = tableOf<char>(mesh->tables, tableOffsets[7], tableSizes[7]);
    const std::span<const assets::SkinInfo> skin =
        tableOf<assets::SkinInfo>(mesh->tables, tableOffsets[8], tableSizes[8]);
    mesh->data.skinInfluences = skin.empty() ? 0 : skin[0].influences;
    measureBones(*mesh, skinPositions, skinBones, skinWeights);
    return Status::Ok;
}

// Main thread, once the upload is done: the staging freed, the descriptor
// slot written, the materials made.
Result<LoadedMesh*> finishMesh(PreparedMesh& prepared)
{
    releaseUpload(prepared.upload);
    if (prepared.staging != nullptr)
    {
        state.device->destroy(prepared.staging);
        prepared.staging = nullptr;
    }
    LoadedMesh* mesh = prepared.mesh;
    prepared.mesh = nullptr;
    // Shaders read the streams through a raw-buffer slot (vertex pulling, ADR 0051).
    state.slots->collect();
    mesh->data.descriptor = state.slots->allocate();
    if (mesh->data.descriptor == 0)
    {
        state.device->destroy(mesh->data.buffer);
        destroy(MemoryTag::Render, mesh);
        return Status::OutOfDeviceMemory;
    }
    state.device->writeRawBuffer(mesh->data.descriptor, {.buffer = mesh->data.buffer, .size = prepared.size});
    if (Status made = createMaterials(*mesh, prepared.generated, prepared.remapped); made != Status::Ok)
    {
        destroyMesh(mesh);
        return made;
    }
    return mesh;
}

// Synchronously: hot reloads and models from bytes.
Result<LoadedMesh*> createMesh(std::span<const uint8_t> bytes)
{
    PreparedMesh prepared;
    if (Status status = prepareMesh(bytes, prepared); status != Status::Ok)
    {
        return status;
    }
    uploadDone(prepared.upload, true);
    return finishMesh(prepared);
}

void destroyMesh(LoadedMesh* mesh)
{
    for (size_t m = 0; m < mesh->materials.size(); ++m)
    {
        if (mesh->acquiredMaterials[m])
        {
            releaseMaterial(mesh->materials[m]);
        }
        else
        {
            destroyMaterial(mesh->materials[m]);
        }
    }
    if (mesh->data.descriptor != 0)
    {
        state.slots->release(mesh->data.descriptor, lastSignaled());
    }
    if (mesh->data.buffer != nullptr)
    {
        state.destroyer->destroyAfter(mesh->data.buffer, lastSignaled());
    }
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

// A texture as a job makes it (ADR 0099): created, its mips staged, and the
// upload submitted. Its descriptor slot is the main thread's (finishTexture).
struct PreparedTexture
{
    slag::Texture* texture = nullptr;
    slag::Buffer* staging = nullptr;
    Upload upload;
};

void discardTexture(PreparedTexture& prepared)
{
    uploadDone(prepared.upload, true);
    releaseUpload(prepared.upload);
    if (prepared.staging != nullptr)
    {
        state.device->destroy(prepared.staging);
    }
    if (prepared.texture != nullptr)
    {
        state.device->destroy(prepared.texture);
    }
    prepared = {};
}

// Any thread.
Status prepareTexture(std::span<const uint8_t> bytes, PreparedTexture& prepared)
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

    slag::Texture*& texture = prepared.texture;
    slag::Buffer*& staging = prepared.staging;
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
        Result<Upload> submitted = submitUpload([&](slag::CommandBuffer* commands) {
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
                                              {staging, stagingOffsets[mip], footprint.rowPitch}, footprint.width,
                                              footprint.height);
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
        });
        status = submitted.status();
        if (submitted)
        {
            prepared.upload = submitted.value();
        }
    }
    if (status != Status::Ok)
    {
        discardTexture(prepared);
    }
    return status;
}

// Main thread, once the upload is done: the staging freed, the texture
// written into `slot` (allocated here if 0).
Result<LoadedTexture*> finishTexture(PreparedTexture& prepared, uint32_t slot)
{
    releaseUpload(prepared.upload);
    if (prepared.staging != nullptr)
    {
        state.device->destroy(prepared.staging);
        prepared.staging = nullptr;
    }
    if (slot == 0)
    {
        state.slots->collect();
        slot = state.slots->allocate();
        if (slot == 0)
        {
            state.device->destroy(prepared.texture);
            prepared.texture = nullptr;
            return Status::OutOfDeviceMemory;
        }
    }
    state.device->writeTexture(slot, {.texture = prepared.texture});
    auto* loaded = create<LoadedTexture>(MemoryTag::Render);
    loaded->texture = prepared.texture;
    loaded->slot = slot;
    prepared.texture = nullptr;
    return loaded;
}

// Synchronously: hot reloads and textures from bytes.
Result<LoadedTexture*> createTexture(std::span<const uint8_t> bytes)
{
    PreparedTexture prepared;
    if (Status status = prepareTexture(bytes, prepared); status != Status::Ok)
    {
        return status;
    }
    uploadDone(prepared.upload, true);
    return finishTexture(prepared, 0);
}

void destroyTexture(LoadedTexture* texture)
{
    state.slots->release(texture->slot, lastSignaled());
    if (!texture->borrowed && texture->texture != nullptr)
    {
        state.destroyer->destroyAfter(texture->texture, lastSignaled());
    }
    destroy(MemoryTag::Render, texture);
}

// ---------------------------------------------------------------------------
// Acquiring by asset ID
// ---------------------------------------------------------------------------

// Models whose materials' textures have all become ready start drawing.
void updateDrawable()
{
    for (Slot<LoadedMesh>& slot : state.meshes.slots)
    {
        if (slot.value == nullptr || !slot.ready || slot.drawable)
        {
            continue;
        }
        bool ready = true;
        for (MaterialHandle material : slot.value->materials)
        {
            ready = ready && isMaterialReady(material);
        }
        slot.drawable = ready;
    }
}

// A load by asset ID (ADR 0099): prepared on a job into `prepared`,
// published into the slot it was started for, unless that was released
// meanwhile (the generation moved on).
template <class Prepared>
struct SlotLoad
{
    uint32_t index = 0;
    uint32_t generation = 0;
    Status status = Status::Ok;
    Prepared prepared;
};

using MeshLoad = SlotLoad<PreparedMesh>;
using TextureLoad = SlotLoad<PreparedTexture>;

template <class T>
Slot<T>* loadingSlot(Table<T>& table, uint32_t index, uint32_t generation)
{
    Slot<T>* slot = table.find(index, generation);
    return slot != nullptr && !slot->ready ? slot : nullptr;
}

// Asynchronously, a failure is only seen here; without workers, acquire
// returns it.
void warnFailed(const char* what, const AssetId& id, Status status)
{
    if (jobs::workerCount() == 0)
    {
        return;
    }
    char text[33];
    for (int i = 0; i < 16; ++i)
    {
        std::snprintf(text + i * 2, 3, "%02x", id.bytes[i]);
    }
    log::warning("%s %s couldn't be loaded (status %d)", what, text, static_cast<int>(status));
}

void startMeshLoad(uint32_t index, uint32_t generation, const AssetId& id)
{
    auto* load = create<MeshLoad>(MemoryTag::Render);
    load->index = index;
    load->generation = generation;
    assets::startLoad({
        .id = id,
        .context = load,
        .prepare =
            [](void* context, std::span<const uint8_t> bytes, bool found) {
                auto& self = *static_cast<MeshLoad*>(context);
                self.status = found ? prepareMesh(bytes, self.prepared) : Status::NotFound;
            },
        .finished = [](void* context,
                       bool wait) { return uploadDone(static_cast<MeshLoad*>(context)->prepared.upload, wait); },
        .publish =
            [](void* context) {
                auto* self = static_cast<MeshLoad*>(context);
                Slot<LoadedMesh>* slot = loadingSlot(state.meshes, self->index, self->generation);
                if (self->status == Status::Ok && slot != nullptr)
                {
                    Result<LoadedMesh*> made = finishMesh(self->prepared);
                    self->status = made.status();
                    if (made)
                    {
                        destroyMesh(slot->value); // the placeholder
                        slot->value = made.value();
                        slot->ready = true;
                    }
                }
                discardMesh(self->prepared); // what's left: all of it, if abandoned or failed
                if (self->status != Status::Ok && slot != nullptr)
                {
                    slot->failure = self->status;
                    warnFailed("a model", slot->asset, self->status);
                }
                destroy(MemoryTag::Render, self);
                updateDrawable();
            },
    });
}

void startTextureLoad(uint32_t index, uint32_t generation, const AssetId& id)
{
    auto* load = create<TextureLoad>(MemoryTag::Render);
    load->index = index;
    load->generation = generation;
    assets::startLoad({
        .id = id,
        .context = load,
        .prepare =
            [](void* context, std::span<const uint8_t> bytes, bool found) {
                auto& self = *static_cast<TextureLoad*>(context);
                self.status = found ? prepareTexture(bytes, self.prepared) : Status::NotFound;
            },
        .finished = [](void* context,
                       bool wait) { return uploadDone(static_cast<TextureLoad*>(context)->prepared.upload, wait); },
        .publish =
            [](void* context) {
                auto* self = static_cast<TextureLoad*>(context);
                Slot<LoadedTexture>* slot = loadingSlot(state.textures, self->index, self->generation);
                if (self->status == Status::Ok && slot != nullptr)
                {
                    // Into the slot the placeholder kept, which materials hold.
                    Result<LoadedTexture*> made = finishTexture(self->prepared, slot->value->slot);
                    self->status = made.status();
                    if (made)
                    {
                        destroy(MemoryTag::Render, slot->value);
                        slot->value = made.value();
                        slot->ready = true;
                    }
                }
                discardTexture(self->prepared);
                if (self->status != Status::Ok && slot != nullptr)
                {
                    slot->failure = self->status;
                    warnFailed("a texture", slot->asset, self->status);
                }
                destroy(MemoryTag::Render, self);
                updateDrawable();
            },
    });
}

// By asset ID: a placeholder in a new slot at once, and a load to fill it
// (ADR 0099). Without job workers the load is done before this returns, so
// a failure is returned, as before loads were asynchronous.
template <class T, class Placeholder, class Start, class Destroy>
Result<uint32_t> acquire(Table<T>& table, const AssetId& id, Placeholder placeholder, Start start, Destroy destroyValue)
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
    if (!assets::assetSourceInstalled())
    {
        log::warning("an asset was acquired before an asset source was installed");
        return Status::NotFound;
    }
    T* value = placeholder();
    if (value == nullptr)
    {
        return Status::OutOfDeviceMemory;
    }
    const uint32_t index = table.add(value);
    Slot<T>& slot = table.slots[index];
    slot.uses = 1;
    slot.acquired = true;
    slot.ready = false;
    slot.asset = id;
    table.byAsset.insertOrAssign(id, index);
    const uint32_t generation = slot.generation;
    start(index, generation, id);
    Slot<T>& after = table.slots[index];
    if (after.generation == generation && after.failure != Status::Ok && jobs::workerCount() == 0)
    {
        const Status failure = after.failure;
        table.byAsset.erase(id);
        destroyValue(after.value);
        table.remove(index);
        return failure;
    }
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
        log::warning("%s", acquired ? "release of something loaded from bytes (use unload)"
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
    slag::Result<slag::Semaphore*> uploads = device.createSemaphore({.debugName = "crucible uploads"});
    if (!uploads)
    {
        device.destroy(state.work);
        state.work = nullptr;
        return toStatus(uploads.status());
    }
    state.uploads = uploads.value();
    state.uploadsSignaled = 0;
    assets::registerLoadStarter(startMeshLoads);
    state.destroyer = create<slag::DeferredDestroyer>(MemoryTag::Render, &device);
    state.slots = create<slag::DescriptorAllocator>(
        MemoryTag::Render, &device,
        slag::DescriptorAllocatorDesc{.heap = slag::DescriptorHeap::Resource,
                                      .firstSlot = firstAssetDescriptor,
                                      .slotCount = slag::defaults::resourceHeapSize - firstAssetDescriptor});
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
        // Shadow maps (ADR 0054, shadows.slang's shadowComparisonSampler):
        // bilinear comparisons, lit where the receiver is no farther than
        // what the light saw.
        {.mipFilter = slag::Filter::Point,
         .addressU = slag::AddressMode::Clamp,
         .addressV = slag::AddressMode::Clamp,
         .addressW = slag::AddressMode::Clamp,
         .compareEnable = true,
         .compareOp = slag::CompareOp::LessEqual},
    };
    for (uint32_t i = 0; i < 4; ++i)
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
    return slot != nullptr && slot->ready ? slot->value->texture : nullptr;
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
    // Loads in flight finish first: they hold uploads and slots.
    assets::waitForLoads();
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
    state.device->destroy(state.uploads);
    state.uploads = nullptr;
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
    Result<uint32_t> index =
        acquire(state.meshes, model, [] { return create<LoadedMesh>(MemoryTag::Render); }, startMeshLoad, destroyMesh);
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
    // The placeholder takes its descriptor slot now: materials made before
    // it's loaded write that slot into their parameters.
    auto placeholder = []() -> LoadedTexture* {
        state.slots->collect();
        const uint32_t slot = state.slots->allocate();
        if (slot == 0)
        {
            return nullptr;
        }
        auto* loaded = create<LoadedTexture>(MemoryTag::Render);
        loaded->slot = slot;
        return loaded;
    };
    Result<uint32_t> index = acquire(state.textures, texture, placeholder, startTextureLoad, destroyTexture);
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
    updateDrawable();
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

TextureHandle addSurfaceTexture(slag::Texture* texture)
{
    if (state.device == nullptr)
    {
        return {};
    }
    state.slots->collect();
    const uint32_t slot = state.slots->allocate();
    if (slot == 0)
    {
        return {};
    }
    state.device->writeTexture(slot, {.texture = texture});
    auto* loaded = create<LoadedTexture>(MemoryTag::Render);
    loaded->texture = texture;
    loaded->slot = slot;
    loaded->borrowed = true;
    const uint32_t index = state.textures.add(loaded);
    return TextureHandle{index, state.textures.slots[index].generation};
}

void replaceSurfaceTexture(TextureHandle handle, slag::Texture* texture)
{
    if (Slot<LoadedTexture>* slot = state.textures.find(handle.index, handle.generation))
    {
        slot->value->texture = texture;
        state.device->writeTexture(slot->value->slot, {.texture = texture});
    }
}

void removeSurfaceTexture(TextureHandle handle)
{
    release(state.textures, handle.index, handle.generation, false, destroyTexture);
}

uint32_t textureDescriptor(TextureHandle texture)
{
    const Slot<LoadedTexture>* slot = state.textures.find(texture.index, texture.generation);
    return slot != nullptr && slot->ready ? slot->value->slot : 0;
}

uint32_t textureSlot(TextureHandle texture)
{
    const Slot<LoadedTexture>* slot = state.textures.find(texture.index, texture.generation);
    return slot != nullptr ? slot->value->slot : 0;
}

bool isMeshReady(MeshHandle mesh)
{
    const Slot<LoadedMesh>* slot = state.meshes.find(mesh.index, mesh.generation);
    return slot != nullptr && slot->drawable;
}

bool isMeshLoading(MeshHandle mesh)
{
    const Slot<LoadedMesh>* slot = state.meshes.find(mesh.index, mesh.generation);
    return slot != nullptr && !slot->drawable && slot->failure == Status::Ok;
}

bool isTextureReady(TextureHandle texture)
{
    const Slot<LoadedTexture>* slot = state.textures.find(texture.index, texture.generation);
    return slot != nullptr && slot->ready;
}

TextureSize textureSize(TextureHandle texture)
{
    const slag::Texture* loaded = textureOf(texture);
    return loaded != nullptr ? TextureSize{loaded->width(), loaded->height()} : TextureSize{};
}

uint32_t samplerDescriptor(Sampler sampler)
{
    return firstSamplerSlot + static_cast<uint32_t>(sampler);
}

// Only models that can draw: loaded, with their materials' textures (ADR 0099).
const MeshData* meshData(MeshHandle mesh)
{
    const Slot<LoadedMesh>* slot = state.meshes.find(mesh.index, mesh.generation);
    return slot != nullptr && slot->drawable ? &slot->value->data : nullptr;
}

const MeshData* meshDataAt(uint32_t index)
{
    return index < state.meshes.slots.size() && state.meshes.slots[index].value != nullptr &&
                   state.meshes.slots[index].drawable
               ? &state.meshes.slots[index].value->data
               : nullptr;
}

std::span<const MaterialHandle> modelMaterials(uint32_t meshIndex)
{
    return meshIndex < state.meshes.slots.size() && state.meshes.slots[meshIndex].value != nullptr
               ? state.meshes.slots[meshIndex].value->materials.span()
               : std::span<const MaterialHandle>();
}

slag::SemaphoreValue lastWorkSignaled()
{
    return lastSignaled();
}

void destroyAfterWork(slag::Pipeline* pipeline)
{
    state.destroyer->destroyAfter(pipeline, lastSignaled());
}

void destroyAfterFrame(slag::Buffer* buffer)
{
    // The frame's submit signals the next value.
    state.destroyer->destroyAfter(buffer, {state.work, state.workSignaled + 1});
}

// ---------------------------------------------------------------------------
// Reloading (ADR 0072)
// ---------------------------------------------------------------------------

namespace {

uint64_t reloads = 0;

// Only in the editor, after a reimport: a frame or two of waiting is
// simpler than finding and rewriting everything that points at the old
// version.
void waitForGpu()
{
    {
        std::lock_guard lock(queueMutex);
        (void)state.queue->waitIdle();
    }
    state.destroyer->collect();
}

// The new texture into the old one's descriptor slot, so material records
// holding that slot read the new one without being rewritten.
Status reloadTexture(Slot<LoadedTexture>& slot)
{
    Vector<uint8_t> bytes(MemoryTag::Render);
    if (Status read = readAsset(slot.asset, bytes); read != Status::Ok)
    {
        return read;
    }
    Result<LoadedTexture*> made = createTexture(bytes.span());
    if (!made)
    {
        return made.status();
    }
    LoadedTexture* fresh = made.value();
    LoadedTexture* old = slot.value;
    waitForGpu();
    state.device->writeTexture(old->slot, {.texture = fresh->texture});
    state.slots->release(fresh->slot, lastSignaled());
    state.device->destroy(old->texture);
    old->texture = fresh->texture;
    destroy(MemoryTag::Render, fresh);
    return Status::Ok;
}

// A new model swapped into the old one's slot. Instances find the new
// buffers, tables, and materials through the same handle when the next
// frame's draw list is built.
Status reloadMesh(Slot<LoadedMesh>& slot)
{
    Vector<uint8_t> bytes(MemoryTag::Render);
    if (Status read = readAsset(slot.asset, bytes); read != Status::Ok)
    {
        return read;
    }
    Result<LoadedMesh*> made = createMesh(bytes.span());
    if (!made)
    {
        return made.status();
    }
    LoadedMesh* old = slot.value;
    if (made.value()->data.meshes.size() < old->data.meshes.size())
    {
        log::warning("a reloaded model has %zu meshes, not %zu: instances of the others draw nothing",
                     made.value()->data.meshes.size(), old->data.meshes.size());
    }
    waitForGpu();
    slot.value = made.value();
    slot.drawable = false;
    destroyMesh(old);
    updateDrawable();
    return Status::Ok;
}

} // namespace

Result<bool> reloadRenderAsset(assets::AssetId asset)
{
    if (state.device == nullptr)
    {
        return false; // nothing can be loaded before the renderer starts
    }
    // Still loading (ADR 0099): a load in flight reads it as it is now; one
    // that failed is started again.
    if (const uint32_t* texture = state.textures.byAsset.find(asset);
        texture != nullptr && !state.textures.slots[*texture].ready)
    {
        Slot<LoadedTexture>& slot = state.textures.slots[*texture];
        if (slot.failure == Status::Ok)
        {
            return false;
        }
        slot.failure = Status::Ok;
        startTextureLoad(*texture, slot.generation, asset);
        return true;
    }
    if (const uint32_t* mesh = state.meshes.byAsset.find(asset); mesh != nullptr && !state.meshes.slots[*mesh].ready)
    {
        Slot<LoadedMesh>& slot = state.meshes.slots[*mesh];
        if (slot.failure == Status::Ok)
        {
            return false;
        }
        slot.failure = Status::Ok;
        startMeshLoad(*mesh, slot.generation, asset);
        return true;
    }
    Status status;
    if (const uint32_t* texture = state.textures.byAsset.find(asset))
    {
        status = reloadTexture(state.textures.slots[*texture]);
    }
    else if (const uint32_t* mesh = state.meshes.byAsset.find(asset))
    {
        status = reloadMesh(state.meshes.slots[*mesh]);
    }
    else
    {
        // A material or a game shader: Materials.cpp makes the new one, then
        // waits for the GPU before swapping.
        Result<bool> reloaded = reloadMaterialAsset(asset);
        if (reloaded && !reloaded.value())
        {
            reloaded = reloadGameShader(asset);
        }
        if (reloaded && reloaded.value())
        {
            ++reloads;
        }
        return reloaded;
    }
    if (status != Status::Ok)
    {
        return status;
    }
    ++reloads;
    return true;
}

void waitForReload()
{
    waitForGpu();
}

uint64_t renderAssetReloads()
{
    return reloads;
}

Status readAsset(const AssetId& id, Vector<uint8_t>& out)
{
    if (!assets::assetSourceInstalled())
    {
        return Status::Unsupported;
    }
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    out.clear();
    return assets::assetSource().read(&id, &out, deliver) == 0 ? Status::Ok : Status::NotFound;
}

std::mutex& queueLock()
{
    return queueMutex;
}

} // namespace crucible::render
