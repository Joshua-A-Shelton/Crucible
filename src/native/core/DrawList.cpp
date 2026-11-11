// The frame's draw list (ADR 0051): mesh instances resolved to loaded models,
// culled against the view (and those that cast shadows against each shadow
// view, ADR 0054) on the job system, and grouped into instanced batches; and
// the GPU copy of their world matrices, updated only where WorldTransform
// changed.

#include "RenderInternal.h"

#include <crucible/Animation.h>
#include <crucible/Ecs.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Log.h>
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

// A model acquired for mesh instances (its materials come with it).
struct CachedModel
{
    MeshHandle handle{}; // generation 0: it failed to load, retried once expired
    Clock::time_point lastUsed;
};

struct Cache
{
    HashMap<AssetId, CachedModel*, AssetIdHash> models{MemoryTag::Render};
    // By MeshHandle index: the build that last saw an instance of the model
    // (written from culling jobs).
    Vector<uint64_t> lastBuild{MemoryTag::Render};
    uint64_t build = 0;
};

Cache cache;
DrawStats stats{};

void releaseModel(CachedModel* model)
{
    if (model->handle.generation != 0)
    {
        releaseMesh(model->handle);
    }
    destroy(MemoryTag::Render, model);
}

// The model's handle, acquired on first use.
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
        const uint32_t index = model->handle.index;
        if (cache.lastBuild.size() <= index)
        {
            cache.lastBuild.resize(index + 1);
        }
    }
    else
    {
        char text[33];
        printId(id, text);
        log::warning("mesh instances use model %s, which couldn't be loaded (status %d)", text,
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

// One frame slot's GPU data: every instance's world matrix by entity index,
// written each frame (skipping unchanged ones saved nothing measurable on the
// M5 demonstration, ADR 0056); and the visible instances' entity indices.
struct FrameBuffers
{
    slag::Buffer* transforms = nullptr;
    uint32_t transformCapacity = 0;
    slag::Buffer* instances = nullptr;
    uint32_t instanceCapacity = 0;
    slag::Buffer* palettes = nullptr; // skinning's (ADR 0080), three rows per joint
    uint32_t paletteCapacity = 0;     // in joints
};

// What one worker (or the calling thread) found; reused frame to frame.
struct WorkerOutput
{
    Vector<uint64_t> keys{MemoryTag::Render}; // per visible instance: view, model index, mesh, level
    Vector<uint32_t> entities{MemoryTag::Render};
    Vector<float> distances{MemoryTag::Render}; // per visible instance: the camera's view depth (0 for shadows)
    Vector<ecs::Entity> unresolved{MemoryTag::Render};
    Vector<uint64_t> casterHashes{MemoryTag::Render}; // per shadow view

    uint32_t instances = 0;
};

// View 0 is the camera, shadow views follow it. Sorting the keys groups the
// batches by view, then model, mesh, and level.
constexpr uint32_t maxViews = 256;
constexpr uint32_t maxModelIndex = 1u << 23;
// A skinned instance's key (ADR 0080) has this bit, and its skin in place of
// the mesh, so each gets a batch of its own.
constexpr uint64_t skinnedKeyBit = uint64_t{1} << 55;
constexpr uint32_t maxSkins = 1u << 24;

uint64_t batchKey(uint32_t view, uint32_t model, uint32_t mesh, uint32_t lod)
{
    return (uint64_t{view} << 56) | (uint64_t{model} << 32) | (uint64_t{mesh} << 8) | lod;
}

uint64_t skinnedKey(uint32_t view, uint32_t model, uint32_t skin, uint32_t lod)
{
    return (uint64_t{view} << 56) | skinnedKeyBit | (uint64_t{model} << 32) | (uint64_t{skin} << 8) | lod;
}

// What culling needs of a skinned instance: its bounds in its mesh's space
// as posed, and a hash of its palette, so a shadow it casts redraws when it
// moves.
struct SkinRecord
{
    uint32_t modelIndex;
    Vector3 boundsMin;
    Vector3 boundsMax;
    uint64_t poseHash;
};

struct RemapKey
{
    const MeshData* model;
    uint64_t skeleton;
    bool operator==(const RemapKey& other) const
    {
        return model == other.model && skeleton == other.skeleton;
    }
};

struct RemapKeyHash
{
    uint64_t operator()(const RemapKey& key) const
    {
        return Hash<uint64_t>()(static_cast<uint64_t>(reinterpret_cast<uintptr_t>(key.model)) * 0x9e3779b97f4a7c15ull ^
                                key.skeleton);
    }
};

uint64_t mix(uint64_t h)
{
    h ^= h >> 31;
    h *= 0xbf58476d1ce4e5b9ull;
    return h ^ (h >> 29);
}

// A caster's share of its view's hash: added up, so the order culling finds
// casters in doesn't matter. Its world matrix is in it, so a caster that
// moved changes the hash, whatever else its chunk holds.
uint64_t casterHash(uint32_t entity, uint64_t key, const Matrix4x4& world)
{
    uint64_t h = mix((uint64_t{entity} << 32 ^ key) * 0x9e3779b97f4a7c15ull);
    for (int row = 0; row < 4; ++row)
    {
        uint32_t bits[3];
        std::memcpy(bits, &world.rows[row], sizeof(bits));
        h = mix(h ^ (uint64_t{bits[0]} | uint64_t{bits[1]} << 32)) + bits[2];
    }
    return mix(h);
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
    // Each view's batches: the first, and one past the last.
    Vector<uint32_t> viewStarts{MemoryTag::Render};
    Vector<uint64_t> casterHashes{MemoryTag::Render};
    Vector<float> distances{MemoryTag::Render};   // by instance-list position
    Vector<uint32_t> entities{MemoryTag::Render}; // by instance-list position: the GPU list's CPU copy
    uint32_t instanceSlot = 0;
    uint32_t transformSlot = 0;
    // Skinning (ADR 0080): this frame's skinned instances, by skin; which
    // skin an entity index has; the palettes; and each skin's joints in its
    // pose's skeleton, by model and skeleton (rebuilt after a reload).
    Vector<SkinnedInstance> skinned{MemoryTag::Render};
    Vector<SkinRecord> skinRecords{MemoryTag::Render};
    HashMap<uint32_t, uint32_t> skinOf{MemoryTag::Render};
    Vector<InstanceTransform> palette{MemoryTag::Render};
    HashMap<RemapKey, Vector<int32_t>*, RemapKeyHash> remaps{MemoryTag::Render};
    uint64_t remapReloads = 0;
    uint32_t skinnedVertexCount = 0;
    uint32_t paletteSlot = 0;
    uint32_t deformedSlot = 0;
};

namespace {

struct CullJob
{
    DrawState* state;
    ecs::World* world;
    FrameBuffers* frame;
    std::array<Plane, 6> planes;
    Vector3 camera;
    Vector3 forward;
    bool orthographic;
    float pixelsPerMeter; // at distance 1 for a perspective view
    std::span<const ShadowCullView> shadows;
    std::atomic<uint32_t> maxIndex{0};
    std::atomic<bool> overflow{false};
};

bool overlaps(std::span<const Plane> planes, Vector3 center, Vector3 extent)
{
    for (const Plane& plane : planes)
    {
        const float reach = std::fabs(plane.normal.x) * extent.x + std::fabs(plane.normal.y) * extent.y +
                            std::fabs(plane.normal.z) * extent.z;
        if (dot(plane.normal, center) + plane.distance + reach < 0)
        {
            return false;
        }
    }
    return true;
}

// The coarsest level whose error, in pixels (or shadow-map texels), stays
// under the limit.
uint32_t levelOf(const MeshData& model, const assets::MeshEntry& mesh, const Matrix4x4& world, Vector3 center,
                 Vector3 extent, bool orthographic, Vector3 eye, float pixelsPerMeter)
{
    uint32_t lod = 0;
    if (mesh.lodCount <= 1)
    {
        return lod;
    }
    float scale = 0;
    for (int axis = 0; axis < 3; ++axis)
    {
        const Vector4 row = world.rows[axis];
        scale = std::max(scale, row.x * row.x + row.y * row.y + row.z * row.z);
    }
    scale = std::sqrt(scale);
    float pixels = pixelsPerMeter * scale;
    if (!orthographic)
    {
        const float radius = length(extent);
        pixels /= std::max(length(center - eye) - radius, 1e-3f);
    }
    while (lod + 1 < mesh.lodCount && model.lods[mesh.lodOffset + lod + 1].error * pixels <= lodPixelError)
    {
        ++lod;
    }
    return lod;
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
        writeTransform(transforms[index], world);
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
        if (meshIndex < 0 || static_cast<size_t>(meshIndex) >= model->meshes.size() || handle.index >= maxModelIndex)
        {
            continue;
        }
        const assets::MeshEntry& mesh = model->meshes[static_cast<size_t>(meshIndex)];
        // A skinned instance is culled by its posed bounds (ADR 0080).
        const uint32_t* skin = job.state->skinOf.find(index);
        Vector3 boundsMin{mesh.boundsMin[0], mesh.boundsMin[1], mesh.boundsMin[2]};
        Vector3 boundsMax{mesh.boundsMax[0], mesh.boundsMax[1], mesh.boundsMax[2]};
        if (skin != nullptr)
        {
            boundsMin = job.state->skinRecords[*skin].boundsMin;
            boundsMax = job.state->skinRecords[*skin].boundsMax;
        }
        auto keyOf = [&](uint32_t view, uint32_t lod) {
            return skin != nullptr ? skinnedKey(view, handle.index, *skin, lod)
                                   : batchKey(view, handle.index, static_cast<uint32_t>(meshIndex), lod);
        };

        // The bounds in world space (Arvo): the center transformed, the
        // extents through the absolute 3x3.
        const Vector3 localCenter = (boundsMin + boundsMax) * 0.5f;
        const Vector3 localExtent = (boundsMax - boundsMin) * 0.5f;
        const Vector3 center = transformPoint(localCenter, world);
        Vector3 extent{};
        for (int axis = 0; axis < 3; ++axis)
        {
            const Vector4 row = world.rows[axis];
            const float e = localExtent[axis];
            extent = extent + Vector3{std::fabs(row.x) * e, std::fabs(row.y) * e, std::fabs(row.z) * e};
        }
        if (overlaps(job.planes, center, extent))
        {
            const uint32_t lod =
                levelOf(*model, mesh, world, center, extent, job.orthographic, job.camera, job.pixelsPerMeter);
            out.keys.pushBack(keyOf(0, lod));
            out.entities.pushBack(index);
            out.distances.pushBack(dot(center - job.camera, job.forward));
        }
        if (instances[i].noShadows)
        {
            continue;
        }
        for (size_t v = 0; v < job.shadows.size(); ++v)
        {
            const ShadowCullView& shadow = job.shadows[v];
            if (!overlaps(std::span(shadow.planes.data(), shadow.planeCount), center, extent))
            {
                continue;
            }
            const uint32_t lod =
                levelOf(*model, mesh, world, center, extent, shadow.orthographic, shadow.eye, shadow.pixelsPerMeter);
            const uint64_t key = keyOf(static_cast<uint32_t>(v) + 1, lod);
            out.keys.pushBack(key);
            out.entities.pushBack(index);
            out.distances.pushBack(0);
            // A skin's key changes with its index from frame to frame: its
            // mesh and pose stand in.
            const uint64_t casterKey = skin != nullptr
                                           ? (uint64_t{handle.index} << 32 | uint32_t(meshIndex) << 8 | lod) ^
                                                 job.state->skinRecords[*skin].poseHash
                                           : key & ~(uint64_t{0xff} << 56);
            out.casterHashes[v] += casterHash(index, casterKey, world);
        }
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

Result<void> ensurePalettes(DrawState& state, uint32_t frameSlot, uint32_t capacity)
{
    FrameBuffers& frame = state.frames[frameSlot];
    if (frame.palettes != nullptr && frame.paletteCapacity >= capacity)
    {
        return {};
    }
    destroyBuffer(frame.palettes);
    slag::GraphicsDevice& device = *renderDevice();
    slag::Result<slag::Buffer*> buffer =
        device.createBuffer({.size = uint64_t{capacity} * sizeof(InstanceTransform),
                             .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                             .debugName = "crucible skin palettes"});
    if (!buffer)
    {
        frame.paletteCapacity = 0;
        return Status::OutOfDeviceMemory;
    }
    frame.palettes = buffer.value();
    frame.paletteCapacity = capacity;
    device.writeStructuredBuffer(state.descriptorBase + skinPaletteDescriptor + frameSlot,
                                 {.buffer = frame.palettes, .count = capacity * 3, .stride = sizeof(Vector4)});
    return {};
}

// The entity's pose (ADR 0079): a PoseHandle on it or its nearest ancestor
// that has one; 0 for none.
uint32_t poseOf(const ecs::World& world, ecs::Entity entity)
{
    for (int depth = 0; entity != ecs::nullEntity && depth < 256; ++depth)
    {
        if (const animation::PoseHandle* handle = ecs::read<animation::PoseHandle>(world, entity);
            handle != nullptr && handle->pose != 0)
        {
            return handle->pose;
        }
        entity = ecs::parentOf(world, entity);
    }
    return 0;
}

// The model's joints in the pose's skeleton, by name (-1 where it has none).
const Vector<int32_t>& remapOf(DrawState& state, const MeshData& model, uint32_t pose)
{
    const RemapKey key{&model, animation::poseSkeletonKey(pose)};
    if (Vector<int32_t>** found = state.remaps.find(key))
    {
        return **found;
    }
    auto* remap = create<Vector<int32_t>>(MemoryTag::Render, MemoryTag::Render);
    for (const assets::JointEntry& joint : model.joints)
    {
        const bool named = uint64_t{joint.nameOffset} + joint.nameLength <= model.names.size();
        remap->pushBack(
            named ? animation::poseJointIndex(pose, {model.names.data() + joint.nameOffset, joint.nameLength}) : -1);
    }
    state.remaps.insertOrAssign(key, remap);
    return *remap;
}

void clearRemaps(DrawState& state)
{
    for (auto& entry : state.remaps)
    {
        destroy(MemoryTag::Render, entry.value);
    }
    state.remaps.clear();
}

// The world's skinned instances that have a pose (ADR 0080): each skin's
// palette (inverse bind, then the posed joint, then the pose's entity to the
// mesh's, so skinned vertices are in the mesh's own space and draw with its
// transform), its posed bounds, and its place in the skinned vertices.
Result<void> gatherSkins(DrawState& state, ecs::World& world, uint32_t frameSlot)
{
    state.skinned.clear();
    state.skinRecords.clear();
    state.skinOf.clear();
    state.palette.clear();
    state.skinnedVertexCount = 0;
    if (ecs::componentIdOf<animation::PoseHandle> == ecs::invalidComponent)
    {
        return {};
    }
    if (renderAssetReloads() != state.remapReloads)
    {
        clearRemaps(state);
        state.remapReloads = renderAssetReloads();
    }
    struct Gather
    {
        DrawState* state;
        ecs::World* world;
    };
    Gather gather{&state, &world};
    const ecs::ComponentId required[] = {ecs::componentIdOf<MeshInstance>, ecs::componentIdOf<LoadedMesh>,
                                         ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            DrawState& state = *static_cast<Gather*>(data)->state;
            const ecs::World& world = *static_cast<Gather*>(data)->world;
            const std::span<const MeshInstance> instances = ecs::readColumn<MeshInstance>(view);
            const std::span<const LoadedMesh> loaded = ecs::readColumn<LoadedMesh>(view);
            const std::span<const WorldTransform> worlds = ecs::readColumn<WorldTransform>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < instances.size(); ++i)
            {
                const MeshHandle handle = loaded[i].handle;
                const MeshData* model = handle.generation != 0 ? meshData(handle) : nullptr;
                const int32_t meshIndex = instances[i].mesh;
                if (model == nullptr || model->skinInfluences == 0 || meshIndex < 0 ||
                    static_cast<size_t>(meshIndex) >= model->meshes.size() || handle.index >= maxModelIndex ||
                    state.skinned.size() >= maxSkins)
                {
                    continue;
                }
                const assets::MeshEntry& mesh = model->meshes[static_cast<size_t>(meshIndex)];
                if ((mesh.attributes & static_cast<uint32_t>(assets::MeshAttribute::Skinned)) == 0)
                {
                    continue;
                }
                const uint32_t pose = poseOf(world, entities[i]);
                const std::span<const Matrix4x4> joints = animation::poseJoints(pose);
                if (joints.empty())
                {
                    continue; // drawn as it was bound
                }
                // From the pose's entity (whose space the joints are in) to the mesh's.
                Matrix4x4 meshInverse;
                ecs::Entity owner = entities[i];
                while (owner != ecs::nullEntity)
                {
                    const animation::PoseHandle* found = ecs::read<animation::PoseHandle>(world, owner);
                    if (found != nullptr && found->pose == pose)
                    {
                        break;
                    }
                    owner = ecs::parentOf(world, owner);
                }
                const WorldTransform* ownerWorld = ecs::read<WorldTransform>(world, owner);
                if (!invert(worlds[i].matrix, meshInverse))
                {
                    continue;
                }
                const Matrix4x4 toMesh =
                    (ownerWorld != nullptr ? ownerWorld->matrix : Matrix4x4::identity()) * meshInverse;
                const Vector<int32_t>& remap = remapOf(state, *model, pose);
                const uint32_t paletteOffset = static_cast<uint32_t>(state.palette.size());
                Vector3 lo{1e30f, 1e30f, 1e30f}, hi{-1e30f, -1e30f, -1e30f};
                uint64_t hash = 0xcbf29ce484222325ull;
                for (size_t j = 0; j < model->joints.size(); ++j)
                {
                    Matrix4x4 skinning = Matrix4x4::identity(); // a joint the pose lacks stays as bound
                    if (remap[j] >= 0 && static_cast<size_t>(remap[j]) < joints.size())
                    {
                        Matrix4x4 inverseBind;
                        std::memcpy(&inverseBind, model->joints[j].inverseBind, sizeof(inverseBind));
                        skinning = inverseBind * joints[static_cast<size_t>(remap[j])] * toMesh;
                    }
                    InstanceTransform& row = state.palette.emplaceBack();
                    writeTransform(row, skinning);
                    uint32_t bits[12];
                    std::memcpy(bits, &row, sizeof(bits));
                    for (const uint32_t b : bits)
                    {
                        hash = (hash ^ b) * 0x100000001b3ull;
                    }
                    if (model->jointReach[j] < 0)
                    {
                        continue;
                    }
                    // The joint's vertices are within its reach of it, scaled
                    // as it is; blended ones within the union of those spheres.
                    float scale = 0;
                    for (int axis = 0; axis < 3; ++axis)
                    {
                        const Vector4 r = skinning.rows[axis];
                        scale = std::max(scale, r.x * r.x + r.y * r.y + r.z * r.z);
                    }
                    const float reach = model->jointReach[j] * std::sqrt(scale);
                    const Vector3 at = transformPoint(model->jointOrigins[j], skinning);
                    lo = {std::min(lo.x, at.x - reach), std::min(lo.y, at.y - reach), std::min(lo.z, at.z - reach)};
                    hi = {std::max(hi.x, at.x + reach), std::max(hi.y, at.y + reach), std::max(hi.z, at.z + reach)};
                }
                if (lo.x > hi.x)
                {
                    lo = hi = {};
                }
                const uint32_t skin = static_cast<uint32_t>(state.skinned.size());
                state.skinned.pushBack({.model = model,
                                        .mesh = static_cast<uint32_t>(meshIndex),
                                        .paletteOffset = paletteOffset,
                                        .jointCount = static_cast<uint32_t>(model->joints.size()),
                                        .deformedOffset = state.skinnedVertexCount,
                                        .vertexCount = mesh.vertexCount});
                state.skinRecords.pushBack({handle.index, lo, hi, hash});
                state.skinOf.insertOrAssign(ecs::indexOf(entities[i]), skin);
                state.skinnedVertexCount += mesh.vertexCount;
            }
        },
        &gather);
    ecs::destroyQuery(query);
    if (state.palette.empty())
    {
        return {};
    }
    const uint32_t joints = static_cast<uint32_t>(state.palette.size());
    if (Result<void> made = ensurePalettes(state, frameSlot, std::max(joints + joints / 2, 256u)); !made)
    {
        state.skinned.clear();
        state.skinOf.clear();
        state.skinnedVertexCount = 0;
        return made;
    }
    std::memcpy(state.frames[frameSlot].palettes->mappedData(), state.palette.data(),
                state.palette.size() * sizeof(InstanceTransform));
    return {};
}

Result<void> cull(DrawState& state, ecs::World& world, const ViewConstants& view, uint32_t height, uint32_t frameSlot,
                  std::span<const ShadowCullView> shadows)
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
            out->distances.clear();
            out->unresolved.clear();
            out->casterHashes.clear();
            out->casterHashes.resize(shadows.size());
            out->instances = 0;
        }
        CullJob job{.state = &state,
                    .world = &world,
                    .frame = &frame,
                    .planes = frustumPlanes(view.viewProjection),
                    .camera = view.cameraPosition,
                    .forward = {view.inverseView.rows[2].x, view.inverseView.rows[2].y, view.inverseView.rows[2].z},
                    .orthographic = orthographic,
                    .pixelsPerMeter = view.projection.rows[1].y * static_cast<float>(height) * 0.5f,
                    .shadows = shadows};
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
    state.casterHashes.clear();
    state.casterHashes.resize(shadows.size());
    for (const WorkerOutput* out : state.workers)
    {
        for (size_t v = 0; v < shadows.size(); ++v)
        {
            state.casterHashes[v] += out->casterHashes[v];
        }
    }
    return {};
}

Result<void> batch(DrawState& state, uint32_t frameSlot, uint32_t viewCount)
{
    // The distinct keys, in order: batches by view, model, mesh, then level.
    state.batchOf.clear();
    state.keys.clear();
    uint32_t total = 0;
    uint32_t visible = 0; // the camera's
    for (const WorkerOutput* out : state.workers)
    {
        for (uint64_t key : out->keys)
        {
            if (state.batchOf.tryEmplace(key, 0u).second)
            {
                state.keys.pushBack(key);
            }
            visible += (key >> 56) == 0 ? 1 : 0;
        }
        total += static_cast<uint32_t>(out->keys.size());
    }
    std::sort(state.keys.data(), state.keys.data() + state.keys.size());
    state.batches.clear();
    state.cursors.clear();
    state.viewStarts.clear();
    state.viewStarts.resize(viewCount + 1);
    for (uint64_t key : state.keys)
    {
        *state.batchOf.find(key) = static_cast<uint32_t>(state.batches.size());
        ++state.viewStarts[(key >> 56) + 1];
        const uint32_t modelIndex = static_cast<uint32_t>((key >> 32) & (maxModelIndex - 1));
        const uint32_t meshOrSkin = static_cast<uint32_t>((key >> 8) & 0xffffff);
        const bool skinned = (key & skinnedKeyBit) != 0;
        state.batches.pushBack({.model = meshDataAt(modelIndex),
                                .mesh = skinned ? state.skinned[meshOrSkin].mesh : meshOrSkin,
                                .lod = static_cast<uint32_t>(key & 0xff),
                                .materials = modelMaterials(modelIndex),
                                .deformedOffset = skinned ? state.skinned[meshOrSkin].deformedOffset : UINT32_MAX});
        state.cursors.pushBack(0);
    }
    for (const WorkerOutput* out : state.workers)
    {
        for (uint64_t key : out->keys)
        {
            ++state.batches[*state.batchOf.find(key)].instanceCount;
        }
    }
    for (uint32_t v = 0; v < viewCount; ++v)
    {
        state.viewStarts[v + 1] += state.viewStarts[v];
    }
    uint32_t first = 0;
    for (size_t b = 0; b < state.batches.size(); ++b)
    {
        state.batches[b].firstInstance = first;
        state.cursors[b] = first;
        first += state.batches[b].instanceCount;
    }

    if (Result<void> made = ensureInstances(state, frameSlot, std::max(total, initialInstanceCapacity)); !made)
    {
        return made;
    }
    auto* list = static_cast<uint32_t*>(state.frames[frameSlot].instances->mappedData());
    state.distances.resize(total);
    state.entities.resize(total);
    for (const WorkerOutput* out : state.workers)
    {
        for (size_t i = 0; i < out->keys.size(); ++i)
        {
            const uint32_t at = state.cursors[*state.batchOf.find(out->keys[i])]++;
            list[at] = out->entities[i];
            state.distances[at] = out->distances[i];
            state.entities[at] = out->entities[i];
        }
    }
    stats.visible = visible;
    return {};
}

} // namespace

namespace {

// The frame's skinning, which every view's list carries (ADR 0080).
DrawList withSkins(const DrawState& state, DrawList list)
{
    list.skinned = state.skinned.span();
    list.palettes = state.paletteSlot;
    list.deformed = state.deformedSlot;
    list.skinnedVertexCount = state.skinnedVertexCount;
    return list;
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
        destroyBuffer(frame.palettes);
    }
    clearRemaps(*state);
    for (WorkerOutput* out : state->workers)
    {
        destroy(MemoryTag::Render, out);
    }
    destroy(MemoryTag::Render, state);
}

Result<DrawList> buildDrawList(DrawState& state, ecs::World* world, const ViewConstants& view, uint32_t height,
                               uint32_t frameSlot, uint32_t lastRunVersion, std::span<const ShadowCullView> shadowViews)
{
    const Clock::time_point started = Clock::now();
    stats = {};
    state.batches.clear();
    state.viewStarts.clear();
    state.casterHashes.clear();
    state.distances.clear();
    state.entities.clear();
    state.skinned.clear();
    state.skinOf.clear();
    state.skinnedVertexCount = 0;
    state.paletteSlot = state.descriptorBase + skinPaletteDescriptor + frameSlot;
    state.deformedSlot = state.descriptorBase + skinnedVerticesDescriptor + frameSlot;
    const uint32_t slots[2] = {state.descriptorBase + instancesDescriptor + frameSlot,
                               state.descriptorBase + transformsDescriptor + frameSlot};
    state.instanceSlot = slots[0];
    state.transformSlot = slots[1];
    if (shadowViews.size() >= maxViews)
    {
        shadowViews = shadowViews.first(maxViews - 1);
    }
    const bool registered = ecs::componentIdOf<MeshInstance> != ecs::invalidComponent &&
                            ecs::componentIdOf<WorldTransform> != ecs::invalidComponent;
    if (world == nullptr || !registered)
    {
        return DrawList{{}, slots[0], slots[1]};
    }
    // Workers may have been added since (the job system restarted).
    while (state.workers.size() <= jobs::workerCount())
    {
        state.workers.pushBack(create<WorkerOutput>(MemoryTag::Render));
    }

    resolveInstances(*world, lastRunVersion);
    ++cache.build;
    if (Result<void> skins = gatherSkins(state, *world, frameSlot); !skins)
    {
        return skins.status();
    }
    if (Result<void> culled = cull(state, *world, view, height, frameSlot, shadowViews); !culled)
    {
        return culled.status();
    }
    if (Result<void> batched = batch(state, frameSlot, static_cast<uint32_t>(shadowViews.size()) + 1); !batched)
    {
        return batched.status();
    }
    for (WorkerOutput* out : state.workers)
    {
        stats.instances += out->instances;
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
    return withSkins(
        state, DrawList{state.batches.span().first(state.viewStarts[1]), slots[0], slots[1], state.entities.span()});
}

DrawList shadowDrawList(const DrawState& state, uint32_t index)
{
    if (size_t{index} + 2 >= state.viewStarts.size())
    {
        return DrawList{{}, state.instanceSlot, state.transformSlot};
    }
    const uint32_t begin = state.viewStarts[index + 1];
    const uint32_t end = state.viewStarts[index + 2];
    return withSkins(state, DrawList{state.batches.span().subspan(begin, end - begin), state.instanceSlot,
                                     state.transformSlot, state.entities.span()});
}

std::span<const float> instanceDistances(const DrawState& state)
{
    return state.distances.span();
}

uint64_t shadowCasterHash(const DrawState& state, uint32_t index)
{
    // A reloaded model or material changes what casts without moving
    // anything (ADR 0072): cached tiles are drawn again.
    const uint64_t hash = index < state.casterHashes.size() ? state.casterHashes[index] : 0;
    return hash ^ mix(renderAssetReloads() * 0x9e3779b97f4a7c15ull);
}

void stopDrawing()
{
    for (auto& entry : cache.models)
    {
        releaseModel(entry.value);
    }
    cache.models.clear();
    cache.lastBuild.clear();
}

DrawStats drawStats()
{
    return stats;
}

} // namespace crucible::render
