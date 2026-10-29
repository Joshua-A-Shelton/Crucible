// The frame's draw list (ADR 0051): mesh instances resolved to loaded models,
// culled against the view on the job system, and grouped into instanced
// batches; and the GPU copy of their world matrices, updated only where
// WorldTransform changed.

#include "RenderInternal.h"

#include <crucible/Ecs.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/MeshInstance.h>
#include <crucible/Transform.h>

#include <slag/Slag.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace crucible::scenes {

void registerMeshInstance()
{
    ecs::registerComponent<MeshInstance>("Crucible.Scenes.MeshInstance");
    ecs::registerComponent<LoadedMesh>("Crucible.Scenes.LoadedMesh");
}

} // namespace crucible::scenes

namespace crucible::render {

namespace {

using assets::AssetId;
using scenes::LoadedMesh;
using scenes::MeshInstance;
using scenes::WorldTransform;
using Clock = std::chrono::steady_clock;

// A model no instance has used for this long is released.
constexpr auto unusedModelLifetime = std::chrono::seconds(2);
// The coarsest level whose error stays under this many pixels is drawn.
constexpr float lodPixelError = 1.0f;
constexpr uint32_t initialTransformCapacity = 1024;
constexpr uint32_t initialInstanceCapacity = 1024;

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

bool isZero(const uint8_t (&bytes)[16])
{
    for (uint8_t byte : bytes)
    {
        if (byte != 0)
        {
            return false;
        }
    }
    return true;
}

void printId(const AssetId& id, char (&text)[33])
{
    for (int i = 0; i < 16; ++i)
    {
        std::snprintf(text + i * 2, 3, "%02x", id.bytes[i]);
    }
}

// ---------------------------------------------------------------------------
// The model cache
// ---------------------------------------------------------------------------

// A model acquired for mesh instances, with its base-color textures.
struct CachedModel
{
    MeshHandle handle{};                                   // generation 0: it failed to load, retried once expired
    Vector<uint32_t> baseColorTextures{MemoryTag::Render}; // per material: slot or 0
    Vector<TextureHandle> textures{MemoryTag::Render};
    Clock::time_point lastUsed;
};

struct Cache
{
    HashMap<AssetId, CachedModel*, AssetIdHash> models{MemoryTag::Render};
    // By MeshHandle index: the build that last saw an instance of the model
    // (written from culling jobs), and the cache entry.
    Vector<uint64_t> lastBuild{MemoryTag::Render};
    Vector<CachedModel*> byIndex{MemoryTag::Render};
    uint64_t build = 0;
};

Cache cache;
bool fullUploads = false;
DrawStats stats{};

void releaseModel(CachedModel* model)
{
    if (model->handle.generation != 0)
    {
        if (model->handle.index < cache.byIndex.size())
        {
            cache.byIndex[model->handle.index] = nullptr;
        }
        releaseMesh(model->handle);
    }
    for (TextureHandle texture : model->textures)
    {
        releaseTexture(texture);
    }
    destroy(MemoryTag::Render, model);
}

// The model's handle, acquired (with its base-color textures) on first use.
MeshHandle resolve(const AssetId& id)
{
    if (isZero(id.bytes))
    {
        return {};
    }
    if (CachedModel** found = cache.models.find(id))
    {
        return (*found)->handle;
    }
    auto* model = create<CachedModel>(MemoryTag::Render);
    model->lastUsed = Clock::now();
    if (Result<MeshHandle> acquired = acquireMesh(id))
    {
        model->handle = acquired.value();
        const MeshData& data = *meshData(model->handle);
        for (const assets::MaterialEntry& material : data.materials)
        {
            uint32_t slot = 0;
            if (!isZero(material.baseColorTexture))
            {
                AssetId textureId;
                std::memcpy(textureId.bytes, material.baseColorTexture, 16);
                if (Result<TextureHandle> texture = acquireTexture(textureId))
                {
                    model->textures.pushBack(texture.value());
                    slot = textureDescriptor(texture.value());
                }
            }
            model->baseColorTextures.pushBack(slot);
        }
        const uint32_t index = model->handle.index;
        if (cache.lastBuild.size() <= index)
        {
            cache.lastBuild.resize(index + 1);
            cache.byIndex.resize(index + 1);
        }
        cache.byIndex[index] = model;
    }
    else
    {
        char text[33];
        printId(id, text);
        std::fprintf(stderr, "crucible: mesh instances use model %s, which couldn't be loaded (status %d)\n", text,
                     static_cast<int>(acquired.status()));
    }
    cache.models.insertOrAssign(id, model);
    return model->handle;
}

void resolveEntity(ecs::World& world, ecs::Entity entity)
{
    const MeshInstance* instance = ecs::read<MeshInstance>(world, entity);
    if (LoadedMesh* loaded = ecs::get<LoadedMesh>(world, entity); instance != nullptr && loaded != nullptr)
    {
        loaded->handle = resolve(instance->model);
    }
}

// New instances get a LoadedMesh; changed ones are resolved again.
void resolveInstances(ecs::World& world, uint32_t lastRunVersion)
{
    const ecs::ComponentId instanceOnly[] = {ecs::componentIdOf<MeshInstance>};
    const ecs::ComponentId loadedOnly[] = {ecs::componentIdOf<LoadedMesh>};
    ecs::Query* fresh = ecs::createQuery(world, {.required = instanceOnly, .excluded = loadedOnly});
    Vector<ecs::Entity> pending(MemoryTag::Render);
    ecs::forEachChunk(
        *fresh,
        [](const ecs::ChunkView& view, void* data) {
            auto& list = *static_cast<Vector<ecs::Entity>*>(data);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (uint32_t i = 0; i < ecs::entityCountOf(view); ++i)
            {
                list.pushBack(entities[i]);
            }
        },
        &pending);
    ecs::destroyQuery(fresh);
    for (ecs::Entity entity : pending)
    {
        ecs::addComponent(world, entity, ecs::componentIdOf<LoadedMesh>);
        resolveEntity(world, entity);
    }

    const ecs::ComponentId both[] = {ecs::componentIdOf<MeshInstance>, ecs::componentIdOf<LoadedMesh>};
    ecs::Query* existing = ecs::createQuery(world, {.required = both});
    struct Changed
    {
        uint32_t since;
    } changed{lastRunVersion};
    ecs::forEachChunk(
        *existing,
        [](const ecs::ChunkView& view, void* data) {
            if (!ecs::changedSince(view, ecs::componentIdOf<MeshInstance>, static_cast<Changed*>(data)->since))
            {
                return;
            }
            const std::span<const MeshInstance> instances = ecs::readColumn<MeshInstance>(view);
            const std::span<LoadedMesh> loaded = ecs::writeColumn<LoadedMesh>(view);
            for (size_t i = 0; i < instances.size(); ++i)
            {
                loaded[i].handle = resolve(instances[i].model);
            }
        },
        &changed);
    ecs::destroyQuery(existing);
}

// Models no instance used for a while are released, and so are failures,
// which are then tried again.
void expireModels()
{
    const Clock::time_point now = Clock::now();
    Vector<AssetId> expired(MemoryTag::Render);
    for (auto& entry : cache.models)
    {
        CachedModel& model = *entry.value;
        if (model.handle.generation != 0 && cache.lastBuild[model.handle.index] == cache.build)
        {
            model.lastUsed = now;
        }
        else if (now - model.lastUsed > unusedModelLifetime)
        {
            expired.pushBack(entry.key);
        }
    }
    for (const AssetId& id : expired)
    {
        releaseModel(*cache.models.find(id));
        cache.models.erase(id);
    }
}

// ---------------------------------------------------------------------------
// Culling and batching
// ---------------------------------------------------------------------------

struct Plane
{
    Vector3 normal;
    float distance;
};

// One frame slot's GPU data: world matrices by entity index, kept across
// frames and brought up to date from the chunks that changed since this
// slot last was; and the visible instances' entity indices.
struct FrameBuffers
{
    slag::Buffer* transforms = nullptr;
    uint32_t transformCapacity = 0;
    uint32_t synced = 0; // the world's version when this copy was last brought up to date
    bool valid = false;  // false: send everything
    slag::Buffer* instances = nullptr;
    uint32_t instanceCapacity = 0;
};

// What one worker (or the calling thread) found; reused frame to frame.
struct WorkerOutput
{
    Vector<uint64_t> keys{MemoryTag::Render}; // per visible instance: model index, mesh, level
    Vector<uint32_t> entities{MemoryTag::Render};
    Vector<ecs::Entity> unresolved{MemoryTag::Render};
    uint32_t instances = 0;
    uint32_t sent = 0;
};

uint64_t batchKey(uint32_t model, uint32_t mesh, uint32_t lod)
{
    return (uint64_t{model} << 32) | (uint64_t{mesh} << 8) | lod;
}

} // namespace

struct DrawState
{
    uint32_t descriptorBase = 0;
    std::array<FrameBuffers, framesInFlight> frames{};
    Vector<WorkerOutput*> workers{MemoryTag::Render};
    HashMap<uint64_t, uint32_t> batchOf{MemoryTag::Render};
    Vector<uint64_t> keys{MemoryTag::Render};
    Vector<DrawBatch> batches{MemoryTag::Render};
    Vector<uint32_t> cursors{MemoryTag::Render};
};

namespace {

struct CullJob
{
    DrawState* state;
    ecs::World* world;
    FrameBuffers* frame;
    bool sendAll;
    std::array<Plane, 6> planes;
    Vector3 camera;
    bool orthographic;
    float pixelsPerMeter; // at distance 1 for a perspective view
    std::atomic<uint32_t> maxIndex{0};
    std::atomic<bool> overflow{false};
};

// Gribb and Hartmann's planes for row vectors: clip = p * M, so each plane is
// a sum of M's columns. Reverse-Z: inside is 0 <= z <= w. An infinite
// perspective's far plane comes out as 0 = near, always inside.
std::array<Plane, 6> frustumPlanes(const Matrix4x4& m)
{
    auto column = [&](int j) {
        return Vector4{m.rows[0][j], m.rows[1][j], m.rows[2][j], m.rows[3][j]};
    };
    const Vector4 x = column(0), y = column(1), z = column(2), w = column(3);
    const Vector4 sums[6] = {w + x, w - x, w + y, w - y, w - z, z};
    std::array<Plane, 6> planes{};
    for (int i = 0; i < 6; ++i)
    {
        planes[i] = {{sums[i].x, sums[i].y, sums[i].z}, sums[i].w};
    }
    return planes;
}

void writeTransform(InstanceTransform& out, const Matrix4x4& m)
{
    out.x = {m.rows[0].x, m.rows[1].x, m.rows[2].x, m.rows[3].x};
    out.y = {m.rows[0].y, m.rows[1].y, m.rows[2].y, m.rows[3].y};
    out.z = {m.rows[0].z, m.rows[1].z, m.rows[2].z, m.rows[3].z};
}

void cullChunk(const ecs::ChunkView& view, void* data)
{
    CullJob& job = *static_cast<CullJob*>(data);
    const int32_t worker = jobs::currentWorker();
    WorkerOutput& out = *job.state->workers[worker < 0 ? job.state->workers.size() - 1 : static_cast<size_t>(worker)];
    const std::span<const MeshInstance> instances = ecs::readColumn<MeshInstance>(view);
    const std::span<const LoadedMesh> loaded = ecs::readColumn<LoadedMesh>(view);
    const std::span<const WorldTransform> worlds = ecs::readColumn<WorldTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    // Only chunks whose world matrices changed since this slot's copy was
    // last brought up to date are sent again.
    const bool send = job.sendAll || ecs::changedSince(view, ecs::componentIdOf<WorldTransform>, job.frame->synced);
    auto* transforms = static_cast<InstanceTransform*>(job.frame->transforms->mappedData());

    for (size_t i = 0; i < instances.size(); ++i)
    {
        const uint32_t index = ecs::indexOf(entities[i]);
        if (index >= job.frame->transformCapacity)
        {
            uint32_t seen = job.maxIndex.load(std::memory_order_relaxed);
            while (index > seen && !job.maxIndex.compare_exchange_weak(seen, index, std::memory_order_relaxed))
            {
            }
            job.overflow.store(true, std::memory_order_relaxed);
            continue;
        }
        const Matrix4x4& world = worlds[i].matrix;
        if (send)
        {
            writeTransform(transforms[index], world);
            ++out.sent;
        }
        ++out.instances;

        const MeshHandle handle = loaded[i].handle;
        const MeshData* model = handle.generation != 0 ? meshData(handle) : nullptr;
        if (model == nullptr)
        {
            out.unresolved.pushBack(entities[i]);
            continue;
        }
        if (handle.index < cache.lastBuild.size())
        {
            std::atomic_ref<uint64_t>(cache.lastBuild[handle.index]).store(cache.build, std::memory_order_relaxed);
        }
        const int32_t meshIndex = instances[i].mesh;
        if (meshIndex < 0 || static_cast<size_t>(meshIndex) >= model->meshes.size())
        {
            continue;
        }
        const assets::MeshEntry& mesh = model->meshes[static_cast<size_t>(meshIndex)];

        // The bounds in world space (Arvo): the center transformed, the
        // extents through the absolute 3x3.
        const Vector3 localCenter{(mesh.boundsMin[0] + mesh.boundsMax[0]) * 0.5f,
                                  (mesh.boundsMin[1] + mesh.boundsMax[1]) * 0.5f,
                                  (mesh.boundsMin[2] + mesh.boundsMax[2]) * 0.5f};
        const Vector3 localExtent{(mesh.boundsMax[0] - mesh.boundsMin[0]) * 0.5f,
                                  (mesh.boundsMax[1] - mesh.boundsMin[1]) * 0.5f,
                                  (mesh.boundsMax[2] - mesh.boundsMin[2]) * 0.5f};
        const Vector3 center = transformPoint(localCenter, world);
        Vector3 extent{};
        for (int axis = 0; axis < 3; ++axis)
        {
            const Vector4 row = world.rows[axis];
            const float e = localExtent[axis];
            extent = extent + Vector3{std::fabs(row.x) * e, std::fabs(row.y) * e, std::fabs(row.z) * e};
        }
        bool inside = true;
        for (const Plane& plane : job.planes)
        {
            const float reach = std::fabs(plane.normal.x) * extent.x + std::fabs(plane.normal.y) * extent.y +
                                std::fabs(plane.normal.z) * extent.z;
            if (dot(plane.normal, center) + plane.distance + reach < 0)
            {
                inside = false;
                break;
            }
        }
        if (!inside)
        {
            continue;
        }

        // The coarsest level whose error, in pixels, stays under the limit.
        uint32_t lod = 0;
        if (mesh.lodCount > 1)
        {
            float scale = 0;
            for (int axis = 0; axis < 3; ++axis)
            {
                const Vector4 row = world.rows[axis];
                scale = std::max(scale, row.x * row.x + row.y * row.y + row.z * row.z);
            }
            scale = std::sqrt(scale);
            float pixels = job.pixelsPerMeter * scale;
            if (!job.orthographic)
            {
                const float radius = length(extent);
                pixels /= std::max(length(center - job.camera) - radius, 1e-3f);
            }
            while (lod + 1 < mesh.lodCount && model->lods[mesh.lodOffset + lod + 1].error * pixels <= lodPixelError)
            {
                ++lod;
            }
        }
        out.keys.pushBack(batchKey(handle.index, static_cast<uint32_t>(meshIndex), lod));
        out.entities.pushBack(index);
    }
}

void destroyBuffer(slag::Buffer*& buffer)
{
    if (buffer != nullptr)
    {
        renderDevice()->destroy(buffer);
        buffer = nullptr;
    }
}

// The slot's GPU work is done (beginFrame waited for it), so its buffers can
// be replaced at once.
Result<void> ensureTransforms(DrawState& state, uint32_t frameSlot, uint32_t capacity)
{
    FrameBuffers& frame = state.frames[frameSlot];
    if (frame.transforms != nullptr && frame.transformCapacity >= capacity)
    {
        return {};
    }
    destroyBuffer(frame.transforms);
    slag::GraphicsDevice& device = *renderDevice();
    // Read by every vertex: in GPU memory when the CPU can write it there.
    const slag::MemoryKind kind =
        device.capabilities().gpuUploadMemory ? slag::MemoryKind::GpuUpload : slag::MemoryKind::Upload;
    slag::Result<slag::Buffer*> buffer = device.createBuffer({.size = uint64_t{capacity} * sizeof(InstanceTransform),
                                                              .placement = slag::Placement::automatic(kind),
                                                              .debugName = "crucible instance transforms"});
    if (!buffer)
    {
        frame.transformCapacity = 0;
        return Status::OutOfDeviceMemory;
    }
    frame.transforms = buffer.value();
    frame.transformCapacity = capacity;
    frame.valid = false;
    device.writeStructuredBuffer(state.descriptorBase + transformsDescriptor + frameSlot,
                                 {.buffer = frame.transforms, .count = capacity, .stride = sizeof(InstanceTransform)});
    return {};
}

Result<void> ensureInstances(DrawState& state, uint32_t frameSlot, uint32_t capacity)
{
    FrameBuffers& frame = state.frames[frameSlot];
    if (frame.instances != nullptr && frame.instanceCapacity >= capacity)
    {
        return {};
    }
    destroyBuffer(frame.instances);
    slag::GraphicsDevice& device = *renderDevice();
    slag::Result<slag::Buffer*> buffer =
        device.createBuffer({.size = uint64_t{capacity} * sizeof(uint32_t),
                             .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                             .debugName = "crucible instance list"});
    if (!buffer)
    {
        frame.instanceCapacity = 0;
        return Status::OutOfDeviceMemory;
    }
    frame.instances = buffer.value();
    frame.instanceCapacity = capacity;
    device.writeStructuredBuffer(state.descriptorBase + instancesDescriptor + frameSlot,
                                 {.buffer = frame.instances, .count = capacity, .stride = sizeof(uint32_t)});
    return {};
}

Result<void> cull(DrawState& state, ecs::World& world, const ViewConstants& view, uint32_t height, uint32_t frameSlot)
{
    FrameBuffers& frame = state.frames[frameSlot];
    if (Result<void> made = ensureTransforms(state, frameSlot, initialTransformCapacity); !made)
    {
        return made;
    }
    const ecs::ComponentId required[] = {ecs::componentIdOf<MeshInstance>, ecs::componentIdOf<LoadedMesh>,
                                         ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    const bool orthographic = view.projection.rows[3].w == 1.0f;
    for (;;)
    {
        for (WorkerOutput* out : state.workers)
        {
            out->keys.clear();
            out->entities.clear();
            out->unresolved.clear();
            out->instances = 0;
            out->sent = 0;
        }
        CullJob job{.state = &state,
                    .world = &world,
                    .frame = &frame,
                    .sendAll = fullUploads || !frame.valid,
                    .planes = frustumPlanes(view.viewProjection),
                    .camera = view.cameraPosition,
                    .orthographic = orthographic,
                    .pixelsPerMeter = view.projection.rows[1].y * static_cast<float>(height) * 0.5f};
        ecs::parallelForEachChunk(*query, cullChunk, &job);
        if (!job.overflow.load())
        {
            break;
        }
        // An entity index past the buffer: grow it and do the frame again.
        const uint32_t needed = job.maxIndex.load() + 1;
        if (Result<void> grown = ensureTransforms(state, frameSlot, needed + needed / 2); !grown)
        {
            ecs::destroyQuery(query);
            return grown;
        }
    }
    ecs::destroyQuery(query);
    frame.synced = ecs::currentVersion(world);
    frame.valid = true;
    return {};
}

Result<void> batch(DrawState& state, uint32_t frameSlot)
{
    // The distinct keys, in order: batches by model, mesh, then level.
    state.batchOf.clear();
    state.keys.clear();
    uint32_t visible = 0;
    for (const WorkerOutput* out : state.workers)
    {
        for (uint64_t key : out->keys)
        {
            if (state.batchOf.tryEmplace(key, 0u).second)
            {
                state.keys.pushBack(key);
            }
        }
        visible += static_cast<uint32_t>(out->keys.size());
    }
    std::sort(state.keys.data(), state.keys.data() + state.keys.size());
    state.batches.clear();
    state.cursors.clear();
    for (uint64_t key : state.keys)
    {
        *state.batchOf.find(key) = static_cast<uint32_t>(state.batches.size());
        const uint32_t modelIndex = static_cast<uint32_t>(key >> 32);
        const CachedModel* cached = modelIndex < cache.byIndex.size() ? cache.byIndex[modelIndex] : nullptr;
        state.batches.pushBack(
            {.model = meshDataAt(modelIndex),
             .mesh = static_cast<uint32_t>((key >> 8) & 0xffffff),
             .lod = static_cast<uint32_t>(key & 0xff),
             .baseColorTextures = cached != nullptr ? cached->baseColorTextures.span() : std::span<const uint32_t>()});
        state.cursors.pushBack(0);
    }
    for (const WorkerOutput* out : state.workers)
    {
        for (uint64_t key : out->keys)
        {
            ++state.batches[*state.batchOf.find(key)].instanceCount;
        }
    }
    uint32_t first = 0;
    for (size_t b = 0; b < state.batches.size(); ++b)
    {
        state.batches[b].firstInstance = first;
        state.cursors[b] = first;
        first += state.batches[b].instanceCount;
    }

    if (Result<void> made = ensureInstances(state, frameSlot, std::max(visible, initialInstanceCapacity)); !made)
    {
        return made;
    }
    auto* list = static_cast<uint32_t*>(state.frames[frameSlot].instances->mappedData());
    for (const WorkerOutput* out : state.workers)
    {
        for (size_t i = 0; i < out->keys.size(); ++i)
        {
            list[state.cursors[*state.batchOf.find(out->keys[i])]++] = out->entities[i];
        }
    }
    stats.visible = visible;
    return {};
}

} // namespace

DrawState* createDrawState(uint32_t descriptorBase)
{
    auto* state = create<DrawState>(MemoryTag::Render);
    state->descriptorBase = descriptorBase;
    for (uint32_t i = 0; i <= jobs::workerCount(); ++i)
    {
        state->workers.pushBack(create<WorkerOutput>(MemoryTag::Render));
    }
    return state;
}

uint32_t drawDescriptorBase(const DrawState& state)
{
    return state.descriptorBase;
}

void destroyDrawState(DrawState* state)
{
    if (state == nullptr)
    {
        return;
    }
    for (FrameBuffers& frame : state->frames)
    {
        destroyBuffer(frame.transforms);
        destroyBuffer(frame.instances);
    }
    for (WorkerOutput* out : state->workers)
    {
        destroy(MemoryTag::Render, out);
    }
    destroy(MemoryTag::Render, state);
}

Result<DrawList> buildDrawList(DrawState& state, ecs::World* world, const ViewConstants& view, uint32_t height,
                               uint32_t frameSlot, uint32_t lastRunVersion)
{
    const Clock::time_point started = Clock::now();
    stats = {};
    state.batches.clear();
    const uint32_t slots[2] = {state.descriptorBase + instancesDescriptor + frameSlot,
                               state.descriptorBase + transformsDescriptor + frameSlot};
    const bool registered = ecs::componentIdOf<MeshInstance> != ecs::invalidComponent &&
                            ecs::componentIdOf<WorldTransform> != ecs::invalidComponent;
    if (world == nullptr || !registered)
    {
        return DrawList{{}, slots[0], slots[1]};
    }
    if (lastRunVersion == 0)
    {
        // A world not seen before: every copy starts over.
        for (FrameBuffers& frame : state.frames)
        {
            frame.valid = false;
        }
    }
    // Workers may have been added since (the job system restarted).
    while (state.workers.size() <= jobs::workerCount())
    {
        state.workers.pushBack(create<WorkerOutput>(MemoryTag::Render));
    }

    resolveInstances(*world, lastRunVersion);
    ++cache.build;
    if (Result<void> culled = cull(state, *world, view, height, frameSlot); !culled)
    {
        return culled.status();
    }
    if (Result<void> batched = batch(state, frameSlot); !batched)
    {
        return batched.status();
    }
    for (WorkerOutput* out : state.workers)
    {
        stats.instances += out->instances;
        stats.transformsSent += out->sent;
        // Stale or new handles: resolved now, drawn next frame.
        for (ecs::Entity entity : out->unresolved)
        {
            resolveEntity(*world, entity);
        }
    }
    expireModels();
    stats.cachedModels = static_cast<uint32_t>(cache.models.size());
    stats.cpuNs =
        static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(Clock::now() - started).count());
    return DrawList{state.batches.span(), slots[0], slots[1]};
}

void stopDrawing()
{
    for (auto& entry : cache.models)
    {
        releaseModel(entry.value);
    }
    cache.models.clear();
    cache.lastBuild.clear();
    cache.byIndex.clear();
}

void forceFullTransformUploads(bool force)
{
    fullUploads = force;
}

DrawStats drawStats()
{
    return stats;
}

} // namespace crucible::render
