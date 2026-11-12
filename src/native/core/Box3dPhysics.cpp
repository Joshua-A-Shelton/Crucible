// Box3D behind the physics contract (ADR 0027, 0085). One Box3D world per
// ECS world, made when first needed. Each tick:
//
//   "Physics.Sync" (PrePhysics)      bodies made, changed, and removed to match
//                                    the components; transforms the game wrote
//                                    pushed in (by kind: a teleport, a target,
//                                    a move)
//   "Physics.Step" (Physics)         one b3World_Step; the tick's contact and
//                                    sensor events gathered
//   "Physics.WriteBack" (PostPhysics) dynamic bodies' poses and velocities
//                                    back into their components, keeping the
//                                    last two ticks' poses
//
// and each frame "Physics.Interpolate" (Update) sets dynamic bodies'
// transforms between those two poses by the frame's fixed alpha. What the
// physics last wrote is remembered per body, so a transform or velocity the
// game wrote is told apart from the physics' own. Box3D's tasks run on the
// job system; its memory comes from Crucible's allocator.

#include "Box3dPhysics.h"

#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Log.h>
#include <crucible/Memory.h>
#include <crucible/MeshFile.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include <box3d/box3d.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>

namespace crucible::physics {

namespace box3d {

namespace {

constexpr MemoryTag tag = MemoryTag::Physics;
constexpr int subSteps = 4;
constexpr uint32_t maxTasks = 64;

// --- Memory -----------------------------------------------------------------

void* allocate(size_t size, int32_t alignment)
{
    return tryAllocate(size, static_cast<size_t>(std::max(alignment, 16)), tag);
}

void release(void* memory, size_t size)
{
    if (memory != nullptr)
    {
        free(memory, size, tag);
    }
}

void setAllocator()
{
    static bool set = false;
    if (!set)
    {
        b3SetAllocator(allocate, release);
        set = true;
    }
}

// --- Conversions ------------------------------------------------------------

b3Vec3 toB3(Vector3 v)
{
    return {v.x, v.y, v.z};
}

Vector3 fromB3(b3Vec3 v)
{
    return {v.x, v.y, v.z};
}

b3Quat toB3(Quaternion q)
{
    return {{q.x, q.y, q.z}, q.w};
}

Quaternion fromB3(b3Quat q)
{
    return {q.v.x, q.v.y, q.v.z, q.s};
}

void* entityData(ecs::Entity entity)
{
    return reinterpret_cast<void*>(static_cast<uintptr_t>(entity.value));
}

ecs::Entity entityOf(b3ShapeId shape)
{
    return {static_cast<uint64_t>(reinterpret_cast<uintptr_t>(b3Shape_GetUserData(shape)))};
}

struct Pose
{
    Vector3 position{};
    Quaternion rotation = Quaternion::identity();
    Vector3 scale{1, 1, 1};
};

// A rotation matrix's quaternion, for row vectors (Matrix4x4::rotation's
// inverse; System.Numerics' CreateFromRotationMatrix).
Quaternion rotationOf(const Vector3 (&r)[3])
{
    const float trace = r[0].x + r[1].y + r[2].z;
    if (trace > 0)
    {
        const float s = std::sqrt(trace + 1);
        const float i = 0.5f / s;
        return {(r[1].z - r[2].y) * i, (r[2].x - r[0].z) * i, (r[0].y - r[1].x) * i, s * 0.5f};
    }
    if (r[0].x >= r[1].y && r[0].x >= r[2].z)
    {
        const float s = std::sqrt(1 + r[0].x - r[1].y - r[2].z);
        const float i = 0.5f / s;
        return {0.5f * s, (r[0].y + r[1].x) * i, (r[0].z + r[2].x) * i, (r[1].z - r[2].y) * i};
    }
    if (r[1].y > r[2].z)
    {
        const float s = std::sqrt(1 + r[1].y - r[0].x - r[2].z);
        const float i = 0.5f / s;
        return {(r[1].x + r[0].y) * i, 0.5f * s, (r[2].y + r[1].z) * i, (r[2].x - r[0].z) * i};
    }
    const float s = std::sqrt(1 + r[2].z - r[0].x - r[1].y);
    const float i = 0.5f / s;
    return {(r[2].x + r[0].z) * i, (r[2].y + r[1].z) * i, 0.5f * s, (r[0].y - r[1].x) * i};
}

Pose decompose(const Matrix4x4& m)
{
    Pose pose;
    pose.position = {m.rows[3].x, m.rows[3].y, m.rows[3].z};
    Vector3 axes[3];
    for (int i = 0; i < 3; ++i)
    {
        const Vector3 row{m.rows[i].x, m.rows[i].y, m.rows[i].z};
        const float scale = length(row);
        (i == 0 ? pose.scale.x : i == 1 ? pose.scale.y : pose.scale.z) = scale;
        axes[i] =
            scale > 1e-12f ? row * (1.0f / scale) : Vector3{i == 0 ? 1.0f : 0, i == 1 ? 1.0f : 0, i == 2 ? 1.0f : 0};
    }
    pose.rotation = normalize(rotationOf(axes));
    return pose;
}

Matrix4x4 compose(Vector3 position, Quaternion rotation, Vector3 scale)
{
    Matrix4x4 m = Matrix4x4::rotation(rotation);
    m.rows[0] = m.rows[0] * scale.x;
    m.rows[1] = m.rows[1] * scale.y;
    m.rows[2] = m.rows[2] * scale.z;
    m.rows[3] = {position.x, position.y, position.z, 1};
    return m;
}

bool same(const scenes::LocalTransform& a, const scenes::LocalTransform& b)
{
    return a.position.x == b.position.x && a.position.y == b.position.y && a.position.z == b.position.z &&
           a.rotation.x == b.rotation.x && a.rotation.y == b.rotation.y && a.rotation.z == b.rotation.z &&
           a.rotation.w == b.rotation.w;
}

bool same(Vector3 a, Vector3 b)
{
    return a.x == b.x && a.y == b.y && a.z == b.z;
}

// Field by field: Collider has padding a byte compare would read.
bool same(const Collider& a, const Collider& b)
{
    return a.shape == b.shape && same(a.size, b.size) && same(a.offset, b.offset) && a.friction == b.friction &&
           a.restitution == b.restitution && a.isTrigger == b.isTrigger && a.mesh == b.mesh &&
           a.meshIndex == b.meshIndex && a.generateIfMissing == b.generateIfMissing;
}

// --- Worlds and bodies ------------------------------------------------------

struct CollisionData;

struct Body
{
    b3BodyId body = b3_nullBodyId;
    b3ShapeId shape = b3_nullShapeId;
    CollisionData* data = nullptr;    // a Mesh or Convex shape's
    BodyKind kind = BodyKind::Static; // a Collider alone is static
    // What the shape was made from.
    Collider collider{};
    Vector3 scale{1, 1, 1};
    uint32_t layers = 1;
    uint32_t mask = allLayers;
    float mass = 0;
    float gravityScale = 1;
    // What the physics last wrote, so the game's own writes can be told
    // apart.
    scenes::LocalTransform written{};
    bool hasWritten = false;
    Vector3 writtenLinear{};
    Vector3 writtenAngular{};
    // The last two ticks' world poses (dynamic), or the last target (kinematic).
    Vector3 previousPosition{};
    Vector3 currentPosition{};
    Quaternion previousRotation = Quaternion::identity();
    Quaternion currentRotation = Quaternion::identity();
    uint32_t seen = 0;
};

// --- Collision data (ADR 0086) ---------------------------------------------
//
// Box3D's mesh and hull data, by model and mesh: read from a model's COLM or
// COLH section (cooked at import), or made at run time. Self-contained blobs,
// kept 16-byte aligned. A mesh shape refers to its data rather than copying
// it, so data is counted by the bodies using it; data a reload or destroy
// retires is freed once none do, and bodies using it rebuild their shapes.

struct CollisionData
{
    assets::AssetId id{};
    int32_t meshIndex = 0;
    bool hull = false;
    void* bytes = nullptr; // null: there's none (so it isn't read again)
    size_t size = 0;
    uint32_t refs = 0;
    bool retired = false;
    bool generated = false; // built at load (Generate If Missing), not imported
};

Vector<CollisionData*> collisionData{tag}; // every live and retired entry
uint32_t createdShapes = 0;

void freeData(CollisionData* data)
{
    if (data->bytes != nullptr)
    {
        free(data->bytes, data->size, tag);
    }
    destroy(tag, data);
}

void retire(CollisionData* data)
{
    data->retired = true;
    if (data->refs == 0)
    {
        for (size_t i = 0; i < collisionData.size(); ++i)
        {
            if (collisionData[i] == data)
            {
                collisionData.removeSwap(i);
                break;
            }
        }
        freeData(data);
    }
}

void releaseData(CollisionData*& data)
{
    if (data != nullptr && --data->refs == 0 && data->retired)
    {
        CollisionData* gone = data;
        for (size_t i = 0; i < collisionData.size(); ++i)
        {
            if (collisionData[i] == gone)
            {
                collisionData.removeSwap(i);
                break;
            }
        }
        freeData(gone);
    }
    data = nullptr;
}

// An aligned copy of a blob, its size from its own byte count.
CollisionData* holding(const void* blob, size_t size, bool hull)
{
    CollisionData* data = create<CollisionData>(tag);
    data->hull = hull;
    data->bytes = tryAllocate(size, 16, tag);
    if (data->bytes == nullptr)
    {
        destroy(tag, data);
        return nullptr;
    }
    std::memcpy(data->bytes, blob, size);
    data->size = size;
    return data;
}

bool validBlob(const void* blob, size_t size, bool hull)
{
    if (hull)
    {
        const auto* data = static_cast<const b3HullData*>(blob);
        return size >= sizeof(b3HullData) && data->version == B3_HULL_VERSION && data->byteCount > 0 &&
               static_cast<size_t>(data->byteCount) <= size;
    }
    const auto* data = static_cast<const b3MeshData*>(blob);
    return size >= sizeof(b3MeshData) && data->version == B3_MESH_VERSION && data->byteCount > 0 &&
           static_cast<size_t>(data->byteCount) <= size;
}

// Box3D's mesh from triangles, owned by the caller (b3DestroyMesh); null if
// it can't be made. Crucible's front faces are clockwise as seen in its
// left-handed space, which is the order whose cross product (b - a) x (c - a)
// points out of the front: Box3D's default winding, whatever the handedness.
b3MeshData* makeMesh(std::span<const Vector3> positions, std::span<const uint32_t> indices)
{
    if (positions.empty() || indices.size() < 3 || positions.size() > INT32_MAX || indices.size() / 3 > INT32_MAX)
    {
        return nullptr;
    }
    for (const uint32_t index : indices)
    {
        if (index >= positions.size())
        {
            return nullptr;
        }
    }
    static_assert(sizeof(Vector3) == sizeof(b3Vec3));
    b3MeshDef def{};
    def.vertices = const_cast<b3Vec3*>(reinterpret_cast<const b3Vec3*>(positions.data()));
    def.stride = sizeof(b3Vec3);
    def.indices = const_cast<int32_t*>(reinterpret_cast<const int32_t*>(indices.data()));
    def.vertexCount = static_cast<int>(positions.size());
    def.triangleCount = static_cast<int>(indices.size() / 3);
    def.weldTolerance = 0.0001f;
    def.weldVertices = true;
    def.identifyEdges = true;
    def.clockWiseWinding = false;
    return b3CreateMesh(&def, nullptr, 0);
}

b3HullData* makeHull(std::span<const Vector3> points, uint32_t maxVertices)
{
    if (points.size() < 4 || points.size() > INT32_MAX)
    {
        return nullptr;
    }
    return b3CreateHull(reinterpret_cast<const b3Vec3*>(points.data()), static_cast<int>(points.size()),
                        static_cast<int>(std::clamp(maxVertices, 4u, 128u)));
}

constexpr uint32_t generatedHullVertices = 32;      // as the importer's MaxHullVertices default
constexpr double slowGenerationMilliseconds = 10.0; // a build at load longer than this says so

template <typename T>
bool readArray(binary::File& file, uint32_t tag, Vector<T>& out)
{
    const int32_t index = binary::findSection(file, tag);
    if (index < 0)
    {
        return false;
    }
    const uint64_t size = binary::sectionInfo(file, static_cast<uint32_t>(index)).size;
    out.resize(static_cast<size_t>(size / sizeof(T)));
    return static_cast<bool>(binary::readSection(
        file, static_cast<uint32_t>(index), std::span(reinterpret_cast<uint8_t*>(out.data()), out.size() * sizeof(T))));
}

// A mesh's data built from its level 0, as the importer builds it; null if
// it can't be made.
CollisionData* generate(binary::File& file, int32_t meshIndex, bool hull)
{
    Vector<assets::MeshEntry> meshes(tag);
    Vector<assets::MeshLod> lods(tag);
    Vector<assets::MeshRange> ranges(tag);
    Vector<Vector3> positions(tag);
    Vector<uint8_t> indexBytes(tag);
    if (!readArray(file, binary::tag("MSHS"), meshes) || meshIndex < 0 ||
        static_cast<size_t>(meshIndex) >= meshes.size() || !readArray(file, binary::tag("POSN"), positions))
    {
        return nullptr;
    }
    const assets::MeshEntry& mesh = meshes[static_cast<size_t>(meshIndex)];
    if (uint64_t{mesh.vertexOffset} + mesh.vertexCount > positions.size())
    {
        return nullptr;
    }
    const std::span<const Vector3> points(positions.data() + mesh.vertexOffset, mesh.vertexCount);
    void* blob = nullptr;
    if (hull)
    {
        blob = makeHull(points, generatedHullVertices);
    }
    else
    {
        if (!readArray(file, binary::tag("LODS"), lods) || !readArray(file, binary::tag("RNGS"), ranges) ||
            !readArray(file, binary::tag("INDX"), indexBytes) || mesh.lodCount == 0 || mesh.lodOffset >= lods.size())
        {
            return nullptr;
        }
        Vector<uint32_t> indices(tag);
        const uint32_t first = lods[mesh.lodOffset].rangeOffset;
        for (uint32_t r = first; r < first + mesh.submeshCount && r < ranges.size(); ++r)
        {
            const assets::MeshRange& range = ranges[r];
            if (uint64_t{range.indexByteOffset} + uint64_t{range.indexCount} * mesh.indexSize > indexBytes.size())
            {
                return nullptr;
            }
            for (uint32_t i = 0; i < range.indexCount; ++i)
            {
                const uint8_t* at = indexBytes.data() + range.indexByteOffset + size_t{i} * mesh.indexSize;
                uint32_t index = 0;
                if (mesh.indexSize == 2)
                {
                    uint16_t small;
                    std::memcpy(&small, at, sizeof(small));
                    index = small;
                }
                else
                {
                    std::memcpy(&index, at, sizeof(index));
                }
                indices.pushBack(index);
            }
        }
        blob = makeMesh(points, indices.span());
    }
    if (blob == nullptr)
    {
        return nullptr;
    }
    const size_t size = hull ? static_cast<size_t>(static_cast<b3HullData*>(blob)->byteCount)
                             : static_cast<size_t>(static_cast<b3MeshData*>(blob)->byteCount);
    CollisionData* made = holding(blob, size, hull);
    if (hull)
    {
        b3DestroyHull(static_cast<b3HullData*>(blob));
    }
    else
    {
        b3DestroyMesh(static_cast<b3MeshData*>(blob));
    }
    if (made != nullptr)
    {
        made->generated = true;
    }
    return made;
}

// A model's mesh's data from its MESH file: imported, or else (if asked)
// built from its triangles; null if there's none.
CollisionData* readModel(const assets::AssetId& id, int32_t meshIndex, bool hull, bool generateIfMissing)
{
    Vector<uint8_t> bytes(tag);
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    if (assets::assetSource().read(&id, &bytes, deliver) != 0)
    {
        return nullptr;
    }
    Result<binary::File*> opened = binary::parseFile(bytes.span(), assets::meshFileKind, assets::meshFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    CollisionData* made = nullptr;
    const int32_t index = binary::findSection(*file, binary::tag(hull ? "COLH" : "COLM"));
    if (index >= 0)
    {
        Vector<uint8_t> section(tag);
        section.resize(static_cast<size_t>(binary::sectionInfo(*file, static_cast<uint32_t>(index)).size));
        if (binary::readSection(*file, static_cast<uint32_t>(index), section.span()) && meshIndex >= 0 &&
            (static_cast<size_t>(meshIndex) + 1) * sizeof(assets::CollisionEntry) <= section.size())
        {
            assets::CollisionEntry entry;
            std::memcpy(&entry, section.data() + static_cast<size_t>(meshIndex) * sizeof(entry), sizeof(entry));
            if (entry.size > 0 && uint64_t{entry.offset} + entry.size <= section.size() &&
                validBlob(section.data() + entry.offset, entry.size, hull))
            {
                made = holding(section.data() + entry.offset, entry.size, hull);
            }
        }
    }
    if (made == nullptr && generateIfMissing)
    {
        const auto started = std::chrono::steady_clock::now();
        made = generate(*file, meshIndex, hull);
        const double elapsed =
            std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - started).count();
        if (made != nullptr && elapsed > slowGenerationMilliseconds)
        {
            // The runtime knows assets by ID, not by file: the editor's Log names the time.
            log::info("generated a model's %s collision for mesh %d at load (%.0f ms); set the model's Generate "
                      "Collision to generate it ahead of time",
                      hull ? "convex" : "mesh", meshIndex, elapsed);
        }
    }
    binary::closeFile(file);
    return made;
}

// The live data for a collider, read if it hasn't been; null if there's
// none (said once). Data built at load (Generate If Missing) serves only
// colliders that ask for it, so one that doesn't still says it's missing.
CollisionData* acquireData(const assets::AssetId& id, int32_t meshIndex, bool hull, bool generateIfMissing = false)
{
    CollisionData* found = nullptr;
    for (CollisionData* data : collisionData)
    {
        if (!data->retired && data->id == id && data->meshIndex == meshIndex && data->hull == hull &&
            (data->bytes == nullptr || !data->generated || generateIfMissing))
        {
            found = data;
        }
    }
    if (found != nullptr && found->bytes == nullptr && generateIfMissing)
    {
        // Missing for a collider that didn't ask; this one does.
        if (CollisionData* made = readModel(id, meshIndex, hull, true))
        {
            made->id = id;
            made->meshIndex = meshIndex;
            collisionData.pushBack(made);
            found = made;
        }
    }
    if (found == nullptr)
    {
        found = readModel(id, meshIndex, hull, generateIfMissing);
        if (found == nullptr)
        {
            found = create<CollisionData>(tag); // none: remembered, so it's said and read once
            found->hull = hull;
            if (!generateIfMissing)
            {
                log::warning("a %s collider's model has no %s collision generated (set the model's Generate "
                             "Collision, or the collider's Generate If Missing)",
                             hull ? "Convex" : "Mesh", hull ? "Convex" : "Mesh");
            }
            else
            {
                log::warning("a %s collider's model has no mesh %d to build collision from", hull ? "Convex" : "Mesh",
                             meshIndex);
            }
        }
        found->id = id;
        found->meshIndex = meshIndex;
        collisionData.pushBack(found);
    }
    if (found->bytes == nullptr)
    {
        return nullptr;
    }
    ++found->refs;
    return found;
}

struct Task
{
    jobs::JobCounter counter;
};

// A character (ADR 0091): what moveCharacter keeps between moves, and the
// kinematic capsule bodies meet it as, following it from tick to tick.
struct Character
{
    b3BodyId body = b3_nullBodyId;
    b3ShapeId shape = b3_nullShapeId;
    CharacterController controller{}; // what the capsule was made from
    Vector3 position{};               // where the body was last sent
    float fallSpeed = 0;
    bool grounded = false;
    Vector3 groundNormal{0, 1, 0};
    uint32_t seen = 0;
};

struct PhysicsWorld
{
    const ecs::World* world = nullptr;
    b3WorldId id = b3_nullWorldId;
    HashMap<uint64_t, Body> bodies{tag};
    HashMap<uint64_t, Character> characters{tag};
    Vector<ContactEvent> events{tag};
    uint64_t eventsFrame = UINT64_MAX;
    uint32_t mark = 0;
    Task tasks[maxTasks];
    uint32_t taskCount = 0;
};

Vector<PhysicsWorld*> worlds{tag};

// Box3D's tasks as jobs: each enqueue runs one, each finish waits for it.
void* enqueueTask(b3TaskCallback* task, void* taskContext, void* userContext, const char*)
{
    PhysicsWorld& world = *static_cast<PhysicsWorld*>(userContext);
    if (world.taskCount == maxTasks)
    {
        task(taskContext); // run here; no finish needed
        return nullptr;
    }
    Task& slot = world.tasks[world.taskCount++];
    const jobs::Job job{task, taskContext};
    jobs::run(std::span(&job, 1), jobs::JobPriority::Frame, slot.counter);
    return &slot;
}

void finishTask(void* userTask, void*)
{
    jobs::wait(static_cast<Task*>(userTask)->counter);
}

// The contract's layers: two shapes collide when either's mask has a layer
// the other is on. A shape's category bits carry its layers (low 32) and its
// mask (high 32), and Box3D's own mask test always passes, so this decides.
bool filter(b3ShapeId a, b3ShapeId b, void*)
{
    const uint64_t categoryA = b3Shape_GetFilter(a).categoryBits;
    const uint64_t categoryB = b3Shape_GetFilter(b).categoryBits;
    const auto layersA = static_cast<uint32_t>(categoryA), maskA = static_cast<uint32_t>(categoryA >> 32);
    const auto layersB = static_cast<uint32_t>(categoryB), maskB = static_cast<uint32_t>(categoryB >> 32);
    return (layersA & maskB) != 0 || (layersB & maskA) != 0;
}

PhysicsWorld* find(const ecs::World& world)
{
    for (PhysicsWorld* candidate : worlds)
    {
        if (candidate->world == &world)
        {
            return candidate;
        }
    }
    return nullptr;
}

PhysicsWorld& worldOf(const ecs::World& world)
{
    if (PhysicsWorld* found = find(world))
    {
        return *found;
    }
    setAllocator();
    PhysicsWorld* made = create<PhysicsWorld>(tag);
    made->world = &world;
    b3WorldDef def = b3DefaultWorldDef();
    def.gravity = {gravity.x, gravity.y, gravity.z};
    def.workerCount = std::max(jobs::workerCount(), 1u);
    def.enqueueTask = enqueueTask;
    def.finishTask = finishTask;
    def.userTaskContext = made;
    made->id = b3CreateWorld(&def);
    b3World_SetCustomFilterCallback(made->id, filter, nullptr);
    worlds.pushBack(made);
    return *made;
}

b3BodyType typeOf(BodyKind kind)
{
    return kind == BodyKind::Dynamic ? b3_dynamicBody : kind == BodyKind::Kinematic ? b3_kinematicBody : b3_staticBody;
}

// The entity's pose in the world, as of its LocalTransforms now (world
// transforms propagate later in the frame).
Pose worldPose(const ecs::World& world, ecs::Entity entity)
{
    return decompose(scenes::computeWorldTransform(world, entity));
}

// A world pose as the entity's LocalTransform: through its parent's world
// transform, if it has one; its own scale kept.
scenes::LocalTransform localOf(const ecs::World& world, ecs::Entity entity, Vector3 position, Quaternion rotation)
{
    scenes::LocalTransform local = *ecs::read<scenes::LocalTransform>(world, entity);
    const ecs::Entity parent = ecs::parentOf(world, entity);
    if (parent.value == 0 || ecs::read<scenes::LocalTransform>(world, parent) == nullptr)
    {
        local.position = position;
        local.rotation = rotation;
        return local;
    }
    Matrix4x4 inverse;
    if (!invert(scenes::computeWorldTransform(world, parent), inverse))
    {
        local.position = position;
        local.rotation = rotation;
        return local;
    }
    const Pose pose = decompose(compose(position, rotation, {1, 1, 1}) * inverse);
    local.position = pose.position;
    local.rotation = pose.rotation;
    return local;
}

// The shape, from the Collider at the entity's world scale (Box3D shapes
// have none): boxes by their extents, spheres and capsules by the largest
// axis. Mesh and Convex shapes come with step 2; until then they have none.
void buildShape(Body& body, ecs::Entity entity, const Collider& collider, Vector3 scale, uint32_t layers, uint32_t mask)
{
    if (!B3_IS_NULL(body.shape))
    {
        b3DestroyShape(body.shape, false);
        body.shape = b3_nullShapeId;
    }
    releaseData(body.data);
    body.collider = collider;
    body.scale = scale;
    body.layers = layers;
    body.mask = mask;
    b3ShapeDef def = b3DefaultShapeDef();
    def.userData = entityData(entity);
    def.baseMaterial.friction = std::max(collider.friction, 0.0f);
    def.baseMaterial.restitution = std::clamp(collider.restitution, 0.0f, 1.0f);
    def.filter = {uint64_t{layers} | (uint64_t{mask} << 32), UINT64_MAX, 0};
    def.enableCustomFiltering = true;
    def.isSensor = collider.isTrigger;
    def.enableSensorEvents = true;
    def.enableContactEvents = true;
    def.updateBodyMass = false;
    const Vector3 offset{collider.offset.x * scale.x, collider.offset.y * scale.y, collider.offset.z * scale.z};
    const float largest = std::max({std::fabs(scale.x), std::fabs(scale.y), std::fabs(scale.z)});
    switch (collider.shape)
    {
    case ShapeKind::Box:
    {
        const b3BoxHull box =
            b3MakeOffsetBoxHull(std::max(collider.size.x * std::fabs(scale.x) * 0.5f, 0.001f),
                                std::max(collider.size.y * std::fabs(scale.y) * 0.5f, 0.001f),
                                std::max(collider.size.z * std::fabs(scale.z) * 0.5f, 0.001f), toB3(offset));
        body.shape = b3CreateHullShape(body.body, &def, &box.base);
        break;
    }
    case ShapeKind::Sphere:
    {
        const b3Sphere sphere{toB3(offset), std::max(collider.size.x * largest, 0.001f)};
        body.shape = b3CreateSphereShape(body.body, &def, &sphere);
        break;
    }
    case ShapeKind::Capsule:
    {
        const float half = std::max(collider.size.y * largest * 0.5f, 0.0f);
        const b3Capsule capsule{toB3(offset - Vector3{0, half, 0}), toB3(offset + Vector3{0, half, 0}),
                                std::max(collider.size.x * largest, 0.001f)};
        body.shape = b3CreateCapsuleShape(body.body, &def, &capsule);
        break;
    }
    case ShapeKind::Mesh:
        // Box3D collides triangle meshes only on static bodies.
        if (body.kind != BodyKind::Static)
        {
            log::warning("a Mesh collider is only for static bodies; use Convex for one that moves");
            break;
        }
        body.data = acquireData(collider.mesh, collider.meshIndex, false, collider.generateIfMissing);
        if (body.data != nullptr)
        {
            body.shape =
                b3CreateMeshShape(body.body, &def, static_cast<const b3MeshData*>(body.data->bytes), toB3(scale));
        }
        break;
    case ShapeKind::Convex:
        body.data = acquireData(collider.mesh, collider.meshIndex, true, collider.generateIfMissing);
        if (body.data != nullptr)
        {
            // Cloned, scaled, and offset; the data could go now, but it's
            // kept for the next body using it.
            body.shape = b3CreateTransformedHullShape(body.body, &def, static_cast<const b3HullData*>(body.data->bytes),
                                                      {toB3(offset), {{0, 0, 0}, 1}}, toB3(scale));
        }
        break;
    }
}

// The body's mass as the RigidBody gives it: Box3D's from its shapes (at
// density 1), scaled so the total is right.
void applyMass(Body& body, float mass)
{
    body.mass = mass;
    // Every body after its shapes change, whatever its kind: Box3D won't
    // step one whose mass is out of date.
    b3Body_ApplyMassFromShapes(body.body);
    if (body.kind != BodyKind::Dynamic || B3_IS_NULL(body.shape))
    {
        return;
    }
    b3MassData data = b3Body_GetMassData(body.body);
    if (data.mass > 0 && mass > 0)
    {
        const float factor = mass / data.mass;
        data.mass = mass;
        for (b3Vec3* column : {&data.inertia.cx, &data.inertia.cy, &data.inertia.cz})
        {
            column->x *= factor;
            column->y *= factor;
            column->z *= factor;
        }
        b3Body_SetMassData(body.body, data);
    }
}

void syncBody(ecs::World& world, PhysicsWorld& physics, ecs::Entity entity, float step)
{
    const Collider& collider = *ecs::read<Collider>(world, entity);
    const RigidBody* rigid = ecs::read<RigidBody>(world, entity);
    const BodyKind kind = rigid != nullptr ? rigid->kind : BodyKind::Static;
    const uint32_t layers = rigid != nullptr ? rigid->layers : 1u;
    const uint32_t mask = rigid != nullptr ? rigid->mask : allLayers;
    const Pose pose = worldPose(world, entity);
    const scenes::LocalTransform local = *ecs::read<scenes::LocalTransform>(world, entity);

    Body* body = physics.bodies.find(entity.value);
    if (body == nullptr)
    {
        physics.bodies.insertOrAssign(entity.value, Body{});
        body = physics.bodies.find(entity.value);
    }
    body->seen = physics.mark;

    if (B3_IS_NULL(body->body))
    {
        b3BodyDef def = b3DefaultBodyDef();
        def.type = typeOf(kind);
        def.position = toB3(pose.position);
        def.rotation = toB3(pose.rotation);
        if (rigid != nullptr)
        {
            def.linearVelocity = toB3(rigid->linearVelocity);
            def.angularVelocity = toB3(rigid->angularVelocity);
            def.gravityScale = rigid->gravityScale;
            body->writtenLinear = rigid->linearVelocity;
            body->writtenAngular = rigid->angularVelocity;
            body->gravityScale = rigid->gravityScale;
        }
        def.userData = entityData(entity);
        body->body = b3CreateBody(physics.id, &def);
        body->kind = kind;
        body->previousPosition = body->currentPosition = pose.position;
        body->previousRotation = body->currentRotation = pose.rotation;
        body->written = local;
        body->hasWritten = true;
        buildShape(*body, entity, collider, pose.scale, layers, mask);
        applyMass(*body, rigid != nullptr ? rigid->mass : 0);
        return;
    }

    const bool kindChanged = kind != body->kind;
    if (kind != body->kind)
    {
        b3Body_SetType(body->body, typeOf(kind));
        body->kind = kind;
        applyMass(*body, rigid != nullptr ? rigid->mass : 0);
    }
    if (!same(collider, body->collider) ||
        std::fabs(pose.scale.x - body->scale.x) + std::fabs(pose.scale.y - body->scale.y) +
                std::fabs(pose.scale.z - body->scale.z) >
            1e-5f ||
        layers != body->layers || mask != body->mask || (body->data != nullptr && body->data->retired) || kindChanged)
    {
        buildShape(*body, entity, collider, pose.scale, layers, mask);
        applyMass(*body, rigid != nullptr ? rigid->mass : 0);
    }
    if (rigid != nullptr)
    {
        if (rigid->mass != body->mass)
        {
            applyMass(*body, rigid->mass);
        }
        if (rigid->gravityScale != body->gravityScale)
        {
            b3Body_SetGravityScale(body->body, rigid->gravityScale);
            body->gravityScale = rigid->gravityScale;
        }
        // Velocities the game set (not the ones written back) are pushed in.
        if (kind == BodyKind::Dynamic && !same(rigid->linearVelocity, body->writtenLinear))
        {
            b3Body_SetLinearVelocity(body->body, toB3(rigid->linearVelocity));
            body->writtenLinear = rigid->linearVelocity;
        }
        if (kind == BodyKind::Dynamic && !same(rigid->angularVelocity, body->writtenAngular))
        {
            b3Body_SetAngularVelocity(body->body, toB3(rigid->angularVelocity));
            body->writtenAngular = rigid->angularVelocity;
        }
    }

    // A transform the game wrote, by kind.
    switch (kind)
    {
    case BodyKind::Dynamic:
        if (!same(local, body->written))
        {
            b3Body_SetTransform(body->body, toB3(pose.position), toB3(pose.rotation));
            b3Body_SetAwake(body->body, true);
            body->previousPosition = body->currentPosition = pose.position;
            body->previousRotation = body->currentRotation = pose.rotation;
            body->written = local;
        }
        break;
    case BodyKind::Kinematic:
        // A new target is reached over the tick, pushing what's in the way;
        // an unchanged one holds it still.
        if (!same(pose.position, body->currentPosition) || !(pose.rotation == body->currentRotation))
        {
            b3Body_SetTargetTransform(body->body, {toB3(pose.position), toB3(pose.rotation)}, step, true);
            body->currentPosition = pose.position;
            body->currentRotation = pose.rotation;
        }
        else
        {
            b3Body_SetLinearVelocity(body->body, {0, 0, 0});
            b3Body_SetAngularVelocity(body->body, {0, 0, 0});
        }
        break;
    case BodyKind::Static:
        if (!same(pose.position, body->currentPosition) || !(pose.rotation == body->currentRotation))
        {
            b3Body_SetTransform(body->body, toB3(pose.position), toB3(pose.rotation));
            body->currentPosition = pose.position;
            body->currentRotation = pose.rotation;
        }
        break;
    }
}

// A character's capsule from its feet: the bottom cap's center a step
// height and a radius up (it rides above the ground, ADR 0091), the top's
// its height above that; or, for its body, reaching down to the feet.
b3Capsule characterCapsule(const CharacterController& controller, bool toFeet)
{
    const float radius = std::max(controller.radius, 0.01f);
    const float bottom = radius + (toFeet ? 0.0f : std::max(controller.stepHeight, 0.0f));
    const float top = radius + std::max(controller.stepHeight, 0.0f) + std::max(controller.height, 0.0f);
    return {{0, bottom, 0}, {0, std::max(top, bottom), 0}, radius};
}

bool sameController(const CharacterController& a, const CharacterController& b)
{
    return a.radius == b.radius && a.height == b.height && a.stepHeight == b.stepHeight && a.layers == b.layers &&
           a.mask == b.mask;
}

// The kinematic capsule a character's body is: made, reshaped, and sent to
// where the character is over the tick, pushing what's in the way.
void syncCharacter(ecs::World& world, PhysicsWorld& physics, ecs::Entity entity, float step)
{
    const CharacterController& controller = *ecs::read<CharacterController>(world, entity);
    const Vector3 at = ecs::read<scenes::LocalTransform>(world, entity)->position;
    Character* character = physics.characters.find(entity.value);
    if (character == nullptr)
    {
        physics.characters.insertOrAssign(entity.value, Character{});
        character = physics.characters.find(entity.value);
    }
    character->seen = physics.mark;
    if (B3_IS_NULL(character->body))
    {
        b3BodyDef def = b3DefaultBodyDef();
        def.type = b3_kinematicBody;
        def.position = toB3(at);
        def.userData = entityData(entity);
        character->body = b3CreateBody(physics.id, &def);
        character->position = at;
    }
    if (B3_IS_NULL(character->shape) || !sameController(controller, character->controller))
    {
        if (!B3_IS_NULL(character->shape))
        {
            b3DestroyShape(character->shape, false);
        }
        b3ShapeDef def = b3DefaultShapeDef();
        def.userData = entityData(entity);
        def.filter = {uint64_t{controller.layers} | (uint64_t{controller.mask} << 32), UINT64_MAX, 0};
        def.enableCustomFiltering = true;
        def.enableContactEvents = true;
        def.enableSensorEvents = true;
        def.updateBodyMass = false;
        const b3Capsule capsule = characterCapsule(controller, true);
        character->shape = b3CreateCapsuleShape(character->body, &def, &capsule);
        b3Body_ApplyMassFromShapes(character->body);
        character->controller = controller;
    }
    if (!same(at, character->position))
    {
        b3Body_SetTargetTransform(character->body, {toB3(at), {{0, 0, 0}, 1}}, step, true);
        character->position = at;
    }
    else
    {
        b3Body_SetLinearVelocity(character->body, {0, 0, 0});
    }
}

float stepOf(const loop::SystemContext& context)
{
    return context.fixedStepSeconds > 0 ? static_cast<float>(context.fixedStepSeconds) : 1.0f / 60;
}

void sync(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    ecs::World& world = *context.world;
    PhysicsWorld& physics = worldOf(world);
    // The first tick of a frame starts its events afresh.
    if (context.frameNumber != physics.eventsFrame)
    {
        physics.events.clear();
        physics.eventsFrame = context.frameNumber;
    }
    ++physics.mark;
    Vector<ecs::Entity> entities(tag);
    const ecs::ComponentId required[] = {ecs::componentIdOf<Collider>, ecs::componentIdOf<scenes::LocalTransform>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* chunk = ecs::entitiesOf(view);
            const size_t count = ecs::readColumn<Collider>(view).size();
            for (size_t i = 0; i < count; ++i)
            {
                static_cast<Vector<ecs::Entity>*>(data)->pushBack(chunk[i]);
            }
        },
        &entities);
    ecs::destroyQuery(query);
    const float step = stepOf(context);
    for (const ecs::Entity entity : entities)
    {
        syncBody(world, physics, entity, step);
    }
    // Bodies whose entity, or Collider, is gone.
    Vector<uint64_t> gone(tag);
    for (const auto& entry : physics.bodies)
    {
        if (entry.value.seen != physics.mark)
        {
            gone.pushBack(entry.key);
        }
    }
    for (const uint64_t key : gone)
    {
        Body* body = physics.bodies.find(key);
        releaseData(body->data);
        if (!B3_IS_NULL(body->body))
        {
            b3DestroyBody(body->body);
        }
        physics.bodies.erase(key);
    }

    // Characters (ADR 0091), and those whose entity or controller is gone.
    Vector<ecs::Entity> characters(tag);
    const ecs::ComponentId controlled[] = {ecs::componentIdOf<CharacterController>,
                                           ecs::componentIdOf<scenes::LocalTransform>};
    ecs::Query* moving = ecs::createQuery(world, {.required = controlled});
    ecs::forEachChunk(
        *moving,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* chunk = ecs::entitiesOf(view);
            const size_t count = ecs::readColumn<CharacterController>(view).size();
            for (size_t i = 0; i < count; ++i)
            {
                static_cast<Vector<ecs::Entity>*>(data)->pushBack(chunk[i]);
            }
        },
        &characters);
    ecs::destroyQuery(moving);
    for (const ecs::Entity entity : characters)
    {
        syncCharacter(world, physics, entity, step);
    }
    gone.clear();
    for (const auto& entry : physics.characters)
    {
        if (entry.value.seen != physics.mark)
        {
            gone.pushBack(entry.key);
        }
    }
    for (const uint64_t key : gone)
    {
        Character* character = physics.characters.find(key);
        if (!B3_IS_NULL(character->body))
        {
            b3DestroyBody(character->body);
        }
        physics.characters.erase(key);
    }
}

Vector3 contactPoint(b3ContactId contact, Vector3& normal)
{
    if (!b3Contact_IsValid(contact))
    {
        return {};
    }
    const b3ContactData data = b3Contact_GetData(contact);
    if (data.manifoldCount == 0 || data.manifolds[0].pointCount == 0)
    {
        return {};
    }
    normal = fromB3(data.manifolds[0].normal);
    const Vector3 center = fromB3(b3Body_GetWorldCenter(b3Shape_GetBody(data.shapeIdA)));
    return center + fromB3(data.manifolds[0].points[0].anchorA);
}

void stepWorld(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    PhysicsWorld& physics = worldOf(*context.world);
    physics.taskCount = 0;
    b3World_Step(physics.id, stepOf(context), subSteps);

    const b3ContactEvents contacts = b3World_GetContactEvents(physics.id);
    for (int i = 0; i < contacts.beginCount; ++i)
    {
        const b3ContactBeginTouchEvent& event = contacts.beginEvents[i];
        if (!b3Shape_IsValid(event.shapeIdA) || !b3Shape_IsValid(event.shapeIdB))
        {
            continue;
        }
        Vector3 normal{};
        const Vector3 point = contactPoint(event.contactId, normal);
        physics.events.pushBack(
            {ContactKind::Begin, false, entityOf(event.shapeIdA), entityOf(event.shapeIdB), point, normal});
    }
    for (int i = 0; i < contacts.endCount; ++i)
    {
        const b3ContactEndTouchEvent& event = contacts.endEvents[i];
        if (!b3Shape_IsValid(event.shapeIdA) || !b3Shape_IsValid(event.shapeIdB))
        {
            continue;
        }
        physics.events.pushBack({ContactKind::End, false, entityOf(event.shapeIdA), entityOf(event.shapeIdB), {}, {}});
    }
    const b3SensorEvents sensors = b3World_GetSensorEvents(physics.id);
    for (int i = 0; i < sensors.beginCount; ++i)
    {
        const b3SensorBeginTouchEvent& event = sensors.beginEvents[i];
        if (b3Shape_IsValid(event.sensorShapeId) && b3Shape_IsValid(event.visitorShapeId))
        {
            physics.events.pushBack(
                {ContactKind::Begin, true, entityOf(event.sensorShapeId), entityOf(event.visitorShapeId), {}, {}});
        }
    }
    for (int i = 0; i < sensors.endCount; ++i)
    {
        const b3SensorEndTouchEvent& event = sensors.endEvents[i];
        if (b3Shape_IsValid(event.sensorShapeId) && b3Shape_IsValid(event.visitorShapeId))
        {
            physics.events.pushBack(
                {ContactKind::End, true, entityOf(event.sensorShapeId), entityOf(event.visitorShapeId), {}, {}});
        }
    }
}

void writeBack(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    ecs::World& world = *context.world;
    PhysicsWorld& physics = worldOf(world);
    for (auto& entry : physics.bodies)
    {
        Body& body = entry.value;
        if (body.kind != BodyKind::Dynamic || B3_IS_NULL(body.body))
        {
            continue;
        }
        const ecs::Entity entity{entry.key};
        const b3WorldTransform transform = b3Body_GetTransform(body.body);
        body.previousPosition = body.currentPosition;
        body.previousRotation = body.currentRotation;
        body.currentPosition = fromB3(transform.p);
        body.currentRotation = normalize(fromB3(transform.q));
        if (RigidBody* rigid = ecs::get<RigidBody>(world, entity))
        {
            rigid->linearVelocity = body.writtenLinear = fromB3(b3Body_GetLinearVelocity(body.body));
            rigid->angularVelocity = body.writtenAngular = fromB3(b3Body_GetAngularVelocity(body.body));
        }
        const scenes::LocalTransform local = localOf(world, entity, body.currentPosition, body.currentRotation);
        *ecs::get<scenes::LocalTransform>(world, entity) = local;
        body.written = local;
    }
}

Quaternion nlerp(Quaternion a, Quaternion b, float t)
{
    const float side = dot(a, b) < 0 ? -1.0f : 1.0f;
    return normalize(Quaternion{a.x + (b.x * side - a.x) * t, a.y + (b.y * side - a.y) * t,
                                a.z + (b.z * side - a.z) * t, a.w + (b.w * side - a.w) * t});
}

void interpolate(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    ecs::World& world = *context.world;
    PhysicsWorld* physics = find(world);
    if (physics == nullptr)
    {
        return;
    }
    const float alpha = std::clamp(static_cast<float>(context.fixedAlpha), 0.0f, 1.0f);
    for (auto& entry : physics->bodies)
    {
        Body& body = entry.value;
        const ecs::Entity entity{entry.key};
        if (body.kind != BodyKind::Dynamic || ecs::read<scenes::LocalTransform>(world, entity) == nullptr)
        {
            continue;
        }
        const Vector3 position = body.previousPosition + (body.currentPosition - body.previousPosition) * alpha;
        const Quaternion rotation = nlerp(body.previousRotation, body.currentRotation, alpha);
        const scenes::LocalTransform local = localOf(world, entity, position, rotation);
        *ecs::get<scenes::LocalTransform>(world, entity) = local;
        body.written = local;
    }
}

} // namespace

void registerSystems()
{
    loop::registerDefaultSystem({.name = syncSystemName, .phase = "PrePhysics", .function = sync, .gameplay = true});
    loop::registerDefaultSystem({.name = stepSystemName, .phase = "Physics", .function = stepWorld, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = writeBackSystemName, .phase = "PostPhysics", .function = writeBack, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = interpolateSystemName, .phase = "Update", .function = interpolate, .gameplay = true});
}

// --- Queries (ADR 0090) -----------------------------------------------------
//
// Box3D's world queries on the last tick's world. A query's layers are its
// Box3D mask, against each shape's category bits (its layers, low 32; see
// Shapes); a shape's own mask doesn't count. Triggers and the ignored
// entity are filtered in the callbacks.

// The queries of the frame, while the editor draws them (View > Queries).
struct QueryRecord
{
    bool cast = false;     // a ray or a shape cast; else an overlap
    bool hasShape = false; // a shape cast or an overlap
    QueryShape shape{};
    Vector3 from{};
    Quaternion rotation = Quaternion::identity();
    Vector3 direction{};
    float distance = 0;
    RayHit hit{};
    uint32_t found = 0;
};

bool recordingQueries = false;
Vector<QueryRecord> recordedQueries{tag};

void record(const QueryRecord& query)
{
    if (recordingQueries && recordedQueries.size() < 4096)
    {
        recordedQueries.pushBack(query);
    }
}

b3QueryFilter queryFilter(const QueryFilter& filter)
{
    b3QueryFilter b3 = b3DefaultQueryFilter();
    b3.categoryBits = UINT64_MAX;
    b3.maskBits = filter.layers;
    return b3;
}

bool admits(b3ShapeId shape, const QueryFilter& filter)
{
    return (filter.triggers || !b3Shape_IsSensor(shape)) &&
           (filter.ignore.value == 0 || entityOf(shape).value != filter.ignore.value);
}

// A query shape as Box3D's point cloud, around the origin, rotated: a box's
// corners, a sphere's center, a capsule's two cap centers (along its Y).
struct Proxy
{
    b3Vec3 points[8];
    b3ShapeProxy proxy{};
};

bool makeProxy(const QueryShape& shape, Quaternion rotation, Proxy& out)
{
    out.proxy.points = out.points;
    switch (shape.kind)
    {
    case ShapeKind::Box:
    {
        const Vector3 half = shape.size * 0.5f;
        for (int i = 0; i < 8; ++i)
        {
            const Vector3 corner{(i & 1) ? half.x : -half.x, (i & 2) ? half.y : -half.y, (i & 4) ? half.z : -half.z};
            out.points[i] = toB3(rotate(corner, rotation));
        }
        out.proxy.count = 8;
        out.proxy.radius = 0;
        return true;
    }
    case ShapeKind::Sphere:
        out.points[0] = {0, 0, 0};
        out.proxy.count = 1;
        out.proxy.radius = std::max(shape.size.x, 0.0f);
        return true;
    case ShapeKind::Capsule:
    {
        const float half = std::max(shape.size.y, 0.0f) * 0.5f;
        out.points[0] = toB3(rotate(Vector3{0, -half, 0}, rotation));
        out.points[1] = toB3(rotate(Vector3{0, half, 0}, rotation));
        out.proxy.count = 2;
        out.proxy.radius = std::max(shape.size.x, 0.0f);
        return true;
    }
    default:
        return false; // Mesh and Convex aren't query shapes
    }
}

struct Closest
{
    QueryFilter filter;
    float distance;
    bool hitsAtStart = true; // a shape cast's; a ray's doesn't hit the collider it starts in (Box3D reports it at 0)
    RayHit hit{};
};

float keepClosest(b3ShapeId shape, b3Pos point, b3Vec3 normal, float fraction, uint64_t, int, int, void* context)
{
    Closest& closest = *static_cast<Closest*>(context);
    if (!admits(shape, closest.filter) || (!closest.hitsAtStart && fraction <= 0))
    {
        return -1; // not this one: go on
    }
    closest.hit = {true, entityOf(shape), fromB3(point), fromB3(normal), fraction * closest.distance};
    return fraction; // nothing past it
}

RayHit raycast(ecs::World& world, Vector3 from, Vector3 direction, float distance, QueryFilter filter)
{
    const PhysicsWorld* physics = find(world);
    Closest closest{filter, distance, false};
    if (physics != nullptr && distance > 0)
    {
        b3World_CastRay(physics->id, toB3(from), toB3(direction * distance), queryFilter(filter), keepClosest,
                        &closest);
    }
    record({true, false, {}, from, Quaternion::identity(), direction, distance, closest.hit, 0});
    return closest.hit;
}

RayHit shapeCast(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
                 float distance, QueryFilter filter)
{
    const PhysicsWorld* physics = find(world);
    Closest closest{filter, distance};
    Proxy proxy;
    if (physics != nullptr && distance >= 0 && makeProxy(shape, rotation, proxy))
    {
        b3World_CastShape(physics->id, toB3(from), &proxy.proxy, toB3(direction * distance), queryFilter(filter),
                          keepClosest, &closest);
    }
    record({true, true, shape, from, rotation, direction, distance, closest.hit, 0});
    return closest.hit;
}

struct Overlaps
{
    QueryFilter filter;
    std::span<ecs::Entity> into;
    Vector<uint64_t> found{tag};
};

bool gather(b3ShapeId shape, void* context)
{
    Overlaps& overlaps = *static_cast<Overlaps*>(context);
    if (!admits(shape, overlaps.filter))
    {
        return true;
    }
    const ecs::Entity entity = entityOf(shape);
    for (const uint64_t seen : overlaps.found)
    {
        if (seen == entity.value)
        {
            return true; // each entity once
        }
    }
    if (overlaps.found.size() < overlaps.into.size())
    {
        overlaps.into[overlaps.found.size()] = entity;
    }
    overlaps.found.pushBack(entity.value);
    return true;
}

uint32_t overlap(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation, QueryFilter filter,
                 std::span<ecs::Entity> into)
{
    const PhysicsWorld* physics = find(world);
    Overlaps overlaps{filter, into};
    Proxy proxy;
    if (physics != nullptr && makeProxy(shape, rotation, proxy))
    {
        b3World_OverlapShape(physics->id, toB3(at), &proxy.proxy, queryFilter(filter), gather, &overlaps);
    }
    const uint32_t count = static_cast<uint32_t>(overlaps.found.size());
    record({false, true, shape, at, rotation, {}, 0, {}, count});
    return count;
}

// --- Characters (ADR 0091) --------------------------------------------------
//
// Box3D's mover: the capsule (riding a step height above the feet) gathers
// the planes it touches, solves the move against them, and casts what's
// left, a few times over; then a short sphere cast down puts the feet on
// walkable ground. Its own kinematic capsule and triggers are left out.

constexpr int maxCharacterPlanes = 16;
constexpr int moveIterations = 5;
constexpr float groundSkin = 0.02f; // how far below the feet still counts as standing

struct Mover
{
    ecs::Entity self;
    float walkable; // the cosine of the slope limit
    int count = 0;
    b3CollisionPlane planes[maxCharacterPlanes];
    b3ShapeId shapes[maxCharacterPlanes];
    Vector3 points[maxCharacterPlanes]; // relative to the origin
};

bool moverAccepts(b3ShapeId shape, void* context)
{
    const Mover& mover = *static_cast<const Mover*>(context);
    return !b3Shape_IsSensor(shape) && entityOf(shape).value != mover.self.value;
}

bool gatherPlanes(b3ShapeId shape, const b3PlaneResult* results, int count, void* context)
{
    Mover& mover = *static_cast<Mover*>(context);
    if (!moverAccepts(shape, context))
    {
        return true;
    }
    for (int i = 0; i < count && mover.count < maxCharacterPlanes; ++i)
    {
        b3Plane plane = results[i].plane;
        // Too steep to walk up: a wall, pushing only sideways, so the solver
        // doesn't slide the character up it.
        if (plane.normal.y > 0 && plane.normal.y < mover.walkable)
        {
            const float side = std::sqrt(plane.normal.x * plane.normal.x + plane.normal.z * plane.normal.z);
            if (side > 1e-4f)
            {
                plane.normal = {plane.normal.x / side, 0, plane.normal.z / side};
            }
        }
        mover.planes[mover.count] = {plane, std::numeric_limits<float>::max(), 0, true}; // rigid
        mover.shapes[mover.count] = shape;
        mover.points[mover.count] = fromB3(results[i].point);
        ++mover.count;
    }
    return true;
}

// The feet's height on ground found at `ground`: down at once (the cast
// keeps them on it), and up onto a step eased over the step smoothing, so a
// character rises rather than snaps (an exponential approach, nine-tenths
// of the way in that time, the same at any frame rate). Landing from a
// fall isn't eased.
float settle(float feet, float ground, const CharacterController& controller, bool wasGrounded, float seconds)
{
    if (ground <= feet || !wasGrounded || controller.stepSmoothing <= 0 || seconds <= 0)
    {
        return ground;
    }
    const float closed = 1 - std::exp(-seconds * 2.302585f / controller.stepSmoothing);
    return feet + (ground - feet) * closed;
}

Character* characterOf(PhysicsWorld& physics, ecs::Entity entity)
{
    Character* character = physics.characters.find(entity.value);
    if (character == nullptr)
    {
        physics.characters.insertOrAssign(entity.value, Character{});
        character = physics.characters.find(entity.value);
    }
    return character;
}

// Dynamic bodies the character walked into are pushed (as Box3D's sample
// does): an impulse along each plane to stop closing on it.
void pushBodies(const Mover& mover, Vector3 origin, Vector3 velocity)
{
    for (int i = 0; i < mover.count; ++i)
    {
        const b3BodyId body = b3Shape_GetBody(mover.shapes[i]);
        if (b3Body_GetType(body) != b3_dynamicBody)
        {
            continue;
        }
        const b3Vec3 point = toB3(origin + mover.points[i]);
        const b3Vec3 normal = toB3(-fromB3(mover.planes[i].plane.normal));
        const b3Vec3 arm = toB3(fromB3(point) - fromB3(b3Body_GetWorldCenter(body)));
        const b3Vec3 armNormal = b3Cross(arm, normal);
        const b3Matrix3 inverseInertia = b3Body_GetWorldInverseRotationalInertia(body);
        const float k = b3Body_GetInverseMass(body) + b3Dot(armNormal, b3MulMV(inverseInertia, armNormal));
        if (k <= 0)
        {
            continue;
        }
        const b3Vec3 bodyVelocity =
            b3Add(b3Body_GetLinearVelocity(body), b3Cross(b3Body_GetAngularVelocity(body), arm));
        const float closing = b3Dot(b3Sub(bodyVelocity, toB3(velocity)), normal);
        const float impulse = std::max(-closing / k, 0.0f);
        if (impulse > 0)
        {
            b3Body_ApplyLinearImpulse(body, b3MulSV(impulse, normal), point, true);
        }
    }
}

CharacterMove moveCharacter(ecs::World& world, ecs::Entity entity, Vector3 displacement, float seconds)
{
    scenes::LocalTransform* transform = ecs::get<scenes::LocalTransform>(world, entity);
    if (transform == nullptr)
    {
        return {};
    }
    const CharacterController* controller = ecs::read<CharacterController>(world, entity);
    PhysicsWorld* physics = find(world);
    if (controller == nullptr || physics == nullptr)
    {
        // No character, or nothing to collide with yet: it just moves.
        transform->position = transform->position + displacement;
        return {transform->position, false, {0, 1, 0}, 0};
    }
    Character& character = *characterOf(*physics, entity);
    const float time = std::max(seconds, 0.0f);
    // Falling: off the ground, or going up; on it, the fall's over.
    if (!character.grounded || character.fallSpeed > 0)
    {
        character.fallSpeed += gravity.y * controller->gravityScale * time;
    }
    else
    {
        character.fallSpeed = 0;
    }
    const float limit = std::clamp(controller->slopeLimit, 0.0f, 1.5707963f);
    Mover mover{entity, std::cos(limit)};
    const b3Capsule capsule = characterCapsule(*controller, false);
    b3QueryFilter filter = b3DefaultQueryFilter();
    filter.categoryBits = UINT64_MAX;
    filter.maskBits = controller->mask;

    const Vector3 start = transform->position;
    const Vector3 target = start + displacement + Vector3{0, character.fallSpeed * time, 0};
    Vector3 at = start;
    for (int iteration = 0; iteration < moveIterations; ++iteration)
    {
        mover.count = 0;
        b3World_CollideMover(physics->id, toB3(at), &capsule, filter, gatherPlanes, &mover);
        const b3PlaneSolverResult solved = b3SolvePlanes(toB3(target - at), mover.planes, mover.count);
        const float fraction =
            b3World_CastMover(physics->id, toB3(at), &capsule, solved.delta, filter, moverAccepts, &mover);
        const Vector3 moved = fromB3(solved.delta) * fraction;
        at = at + moved;
        if (length(moved) < 0.001f)
        {
            break;
        }
    }
    // A head against a ceiling stops going up.
    if (character.fallSpeed > 0 && at.y - start.y < character.fallSpeed * time * 0.5f)
    {
        character.fallSpeed = 0;
    }

    // The ground: a sphere a little smaller than the capsule cast down from
    // its bottom cap, as far as the feet ride (and, standing, a step more,
    // to follow it down stairs and slopes).
    const bool wasGrounded = character.grounded;
    character.grounded = false;
    character.groundNormal = {0, 1, 0};
    if (character.fallSpeed <= 0)
    {
        const float radius = capsule.radius * 0.95f;
        const float ride = capsule.center1.y - radius;
        const float reach = ride + groundSkin + (wasGrounded ? controller->stepHeight : 0.0f);
        Proxy probe;
        probe.points[0] = {0, 0, 0};
        probe.proxy = {probe.points, 1, radius};
        Closest closest{{controller->mask, entity, false}, reach};
        const Vector3 from = at + Vector3{0, capsule.center1.y, 0};
        b3World_CastShape(physics->id, toB3(from), &probe.proxy, toB3(Vector3{0, -reach, 0}), filter, keepClosest,
                          &closest);
        if (closest.hit.hit && closest.hit.normal.y >= mover.walkable)
        {
            at.y = settle(at.y, from.y - closest.hit.distance - radius, *controller, wasGrounded, time);
            character.grounded = true;
            character.groundNormal = closest.hit.normal;
            character.fallSpeed = 0;
        }
        else
        {
            // Over an edge the sphere meets its corner, too steep to stand
            // on: a ray straight down from the middle finds the step below.
            Closest below{{controller->mask, entity, false}, ride + reach, false};
            const Vector3 middle = at + Vector3{0, ride, 0};
            b3World_CastRay(physics->id, toB3(middle), toB3(Vector3{0, -(ride + reach), 0}), filter, keepClosest,
                            &below);
            if (below.hit.hit && below.hit.normal.y >= mover.walkable && below.hit.distance <= ride + reach)
            {
                at.y = settle(at.y, middle.y - below.hit.distance, *controller, wasGrounded, time);
                character.grounded = true;
                character.groundNormal = below.hit.normal;
                character.fallSpeed = 0;
            }
        }
    }
    if (time > 0)
    {
        pushBodies(mover, at, (at - start) * (1 / time));
    }
    transform->position = at;
    return {at, character.grounded, character.groundNormal, character.fallSpeed};
}

void launchCharacter(ecs::World& world, ecs::Entity entity, float upwardSpeed)
{
    if (PhysicsWorld* physics = find(world))
    {
        Character& character = *characterOf(*physics, entity);
        character.fallSpeed = upwardSpeed;
        character.grounded = false;
    }
}

uint32_t contacts(ecs::World& world, std::span<ContactEvent> into)
{
    const PhysicsWorld* physics = find(world);
    if (physics == nullptr)
    {
        return 0;
    }
    const size_t count = std::min(into.size(), physics->events.size());
    std::copy_n(physics->events.begin(), count, into.begin());
    return static_cast<uint32_t>(physics->events.size());
}

} // namespace box3d

BodyPose bodyPose(const ecs::World& world, ecs::Entity entity)
{
    using namespace box3d;
    const PhysicsWorld* physics = find(world);
    const Body* body = physics != nullptr ? physics->bodies.find(entity.value) : nullptr;
    if (body == nullptr || B3_IS_NULL(body->body))
    {
        return {false, {}, Quaternion::identity()};
    }
    const b3WorldTransform transform = b3Body_GetTransform(body->body);
    return {true, fromB3(transform.p), normalize(fromB3(transform.q))};
}

using namespace box3d;

namespace {

using box3d::makeHull;
using box3d::makeMesh;

// A made shape under a new ID: "crcoll" and a count.
assets::AssetId adopt(const void* blob, size_t size, bool hull)
{
    CollisionData* data = holding(blob, size, hull);
    if (data == nullptr)
    {
        return {};
    }
    assets::AssetId id{};
    std::memcpy(id.bytes, "crcoll", 6);
    const uint32_t count = ++createdShapes;
    std::memcpy(id.bytes + 8, &count, sizeof(count));
    data->id = id;
    collisionData.pushBack(data);
    return id;
}

} // namespace

bool cookCollisionMesh(std::span<const Vector3> positions, std::span<const uint32_t> indices, Vector<uint8_t>& out)
{
    setAllocator();
    b3MeshData* mesh = makeMesh(positions, indices);
    if (mesh == nullptr)
    {
        return false;
    }
    out.assign(std::span(reinterpret_cast<const uint8_t*>(mesh), static_cast<size_t>(mesh->byteCount)));
    b3DestroyMesh(mesh);
    return true;
}

bool cookCollisionHull(std::span<const Vector3> points, uint32_t maxVertices, Vector<uint8_t>& out)
{
    setAllocator();
    b3HullData* hull = makeHull(points, maxVertices);
    if (hull == nullptr)
    {
        return false;
    }
    out.assign(std::span(reinterpret_cast<const uint8_t*>(hull), static_cast<size_t>(hull->byteCount)));
    b3DestroyHull(hull);
    return true;
}

assets::AssetId createCollisionMesh(std::span<const Vector3> positions, std::span<const uint32_t> indices)
{
    setAllocator();
    b3MeshData* mesh = makeMesh(positions, indices);
    if (mesh == nullptr)
    {
        return {};
    }
    const assets::AssetId id = adopt(mesh, static_cast<size_t>(mesh->byteCount), false);
    b3DestroyMesh(mesh);
    return id;
}

assets::AssetId createCollisionHull(std::span<const Vector3> points, uint32_t maxVertices)
{
    setAllocator();
    b3HullData* hull = makeHull(points, maxVertices);
    if (hull == nullptr)
    {
        return {};
    }
    const assets::AssetId id = adopt(hull, static_cast<size_t>(hull->byteCount), true);
    b3DestroyHull(hull);
    return id;
}

void destroyCollisionShape(assets::AssetId shape)
{
    (void)reloadCollisionAsset(shape);
}

bool reloadCollisionAsset(assets::AssetId model)
{
    bool found = false;
    Vector<CollisionData*> matching(tag);
    for (CollisionData* data : collisionData)
    {
        if (!data->retired && data->id == model)
        {
            matching.pushBack(data);
        }
    }
    for (CollisionData* data : matching)
    {
        found = found || data->bytes != nullptr;
        retire(data);
    }
    return found;
}

void releasePhysics(const ecs::World& world)
{
    using namespace box3d;
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        if (worlds[i]->world == &world)
        {
            for (auto& entry : worlds[i]->bodies)
            {
                releaseData(entry.value.data);
            }
            b3DestroyWorld(worlds[i]->id);
            destroy(tag, worlds[i]);
            worlds.removeSwap(i);
            return;
        }
    }
}

namespace {

// A circle of lines in a plane, as the outline of a round shape.
void circle(Vector3 center, Vector3 u, Vector3 v, float radius, const Matrix4x4& toWorld, Vector4 color)
{
    constexpr int segments = 24;
    Vector3 last = transformPoint(center + u * radius, toWorld);
    for (int i = 1; i <= segments; ++i)
    {
        const float angle = 6.2831853f * static_cast<float>(i) / segments;
        const Vector3 next = transformPoint(center + (u * std::cos(angle) + v * std::sin(angle)) * radius, toWorld);
        debug::drawLine(last, next, color);
        last = next;
    }
}

void outline(const Collider& collider, const Matrix4x4& toWorld, Vector4 color)
{
    const Vector3 x{1, 0, 0}, y{0, 1, 0}, z{0, 0, 1};
    const Vector3 o = collider.offset;
    switch (collider.shape)
    {
    case ShapeKind::Box:
    {
        const Vector3 h = collider.size * 0.5f;
        Vector3 corners[8];
        for (int i = 0; i < 8; ++i)
        {
            corners[i] =
                transformPoint(o + Vector3{(i & 1) ? h.x : -h.x, (i & 2) ? h.y : -h.y, (i & 4) ? h.z : -h.z}, toWorld);
        }
        for (int i = 0; i < 8; ++i)
        {
            for (const int bit : {1, 2, 4})
            {
                if ((i & bit) == 0)
                {
                    debug::drawLine(corners[i], corners[i | bit], color);
                }
            }
        }
        break;
    }
    case ShapeKind::Sphere:
        circle(o, x, y, collider.size.x, toWorld, color);
        circle(o, y, z, collider.size.x, toWorld, color);
        circle(o, z, x, collider.size.x, toWorld, color);
        break;
    case ShapeKind::Capsule:
    {
        const float r = collider.size.x, half = collider.size.y * 0.5f;
        const Vector3 top = o + y * half, bottom = o - y * half;
        circle(top, z, x, r, toWorld, color);
        circle(bottom, z, x, r, toWorld, color);
        circle(top, x, y, r, toWorld, color);
        circle(bottom, x, y, r, toWorld, color);
        circle(top, y, z, r, toWorld, color);
        circle(bottom, y, z, r, toWorld, color);
        for (const Vector3 side : {x * r, x * -r, z * r, z * -r})
        {
            debug::drawLine(transformPoint(top + side, toWorld), transformPoint(bottom + side, toWorld), color);
        }
        break;
    }
    case ShapeKind::Mesh:
    {
        // The triangles' edges, from the collision data itself (what Box3D sees).
        CollisionData* data = acquireData(collider.mesh, collider.meshIndex, false, collider.generateIfMissing);
        if (data == nullptr)
        {
            break;
        }
        const auto* mesh = static_cast<const b3MeshData*>(data->bytes);
        const b3Vec3* vertices = b3GetMeshVertices(mesh);
        const b3MeshTriangle* triangles = b3GetMeshTriangles(mesh);
        for (int32_t t = 0; t < mesh->triangleCount; ++t)
        {
            const Vector3 a = transformPoint(fromB3(vertices[triangles[t].index1]), toWorld);
            const Vector3 b = transformPoint(fromB3(vertices[triangles[t].index2]), toWorld);
            const Vector3 c = transformPoint(fromB3(vertices[triangles[t].index3]), toWorld);
            debug::drawLine(a, b, color);
            debug::drawLine(b, c, color);
            debug::drawLine(c, a, color);
        }
        releaseData(data);
        break;
    }
    case ShapeKind::Convex:
    {
        CollisionData* data = acquireData(collider.mesh, collider.meshIndex, true, collider.generateIfMissing);
        if (data == nullptr)
        {
            break;
        }
        const auto* hull = static_cast<const b3HullData*>(data->bytes);
        const b3Vec3* points = b3GetHullPoints(hull);
        const b3HullHalfEdge* edges = b3GetHullEdges(hull);
        for (int32_t e = 0; e < hull->edgeCount; ++e)
        {
            // Each edge once: from the half of it with the lower index.
            if (e < edges[e].twin)
            {
                debug::drawLine(transformPoint(o + fromB3(points[edges[e].origin]), toWorld),
                                transformPoint(o + fromB3(points[edges[edges[e].twin].origin]), toWorld), color);
            }
        }
        releaseData(data);
        break;
    }
    }
}

} // namespace

void recordQueries(bool on)
{
    recordingQueries = on;
    if (!on)
    {
        recordedQueries.clear();
    }
}

void drawQueries(Vector4 missed, Vector4 hit)
{
    for (const QueryRecord& query : recordedQueries)
    {
        const bool struck = query.cast ? query.hit.hit : query.found > 0;
        const Vector4 color = struck ? hit : missed;
        const Vector3 end = query.from + query.direction * (struck && query.cast ? query.hit.distance : query.distance);
        if (query.cast)
        {
            debug::drawLine(query.from, end, color);
            if (struck)
            {
                // Where it hit, and the surface's normal there.
                debug::drawLine(query.hit.point, query.hit.point + query.hit.normal * 0.25f, color);
                const Vector3 x{0.05f, 0, 0}, y{0, 0.05f, 0}, z{0, 0, 0.05f};
                debug::drawLine(query.hit.point - x, query.hit.point + x, color);
                debug::drawLine(query.hit.point - y, query.hit.point + y, color);
                debug::drawLine(query.hit.point - z, query.hit.point + z, color);
            }
        }
        if (query.hasShape)
        {
            // The shape where it started (an overlap's) and where it stopped (a cast's).
            Collider shape{};
            shape.shape = query.shape.kind;
            shape.size = query.shape.size;
            const Matrix4x4 turn = Matrix4x4::rotation(query.rotation);
            outline(shape, turn * Matrix4x4::translation(query.from), color);
            if (query.cast)
            {
                outline(shape, turn * Matrix4x4::translation(end), color);
            }
        }
    }
    recordedQueries.clear();
}

void drawColliders(const ecs::World& world, Vector4 color)
{
    struct Gather
    {
        const ecs::World* world;
        Vector4 color;
    } gather{&world, color};
    const ecs::ComponentId required[] = {ecs::componentIdOf<Collider>, ecs::componentIdOf<scenes::LocalTransform>};
    ecs::Query* query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const Collider> colliders = ecs::readColumn<Collider>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < colliders.size(); ++i)
            {
                outline(colliders[i], scenes::computeWorldTransform(*gather.world, entities[i]), gather.color);
            }
        },
        &gather);
    ecs::destroyQuery(query);
    // Characters' capsules (ADR 0091), from their feet up.
    const ecs::ComponentId controlled[] = {ecs::componentIdOf<CharacterController>,
                                           ecs::componentIdOf<scenes::LocalTransform>};
    query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = controlled});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const CharacterController> controllers = ecs::readColumn<CharacterController>(view);
            const std::span<const scenes::LocalTransform> transforms = ecs::readColumn<scenes::LocalTransform>(view);
            for (size_t i = 0; i < controllers.size(); ++i)
            {
                const b3Capsule capsule = characterCapsule(controllers[i], true);
                Collider shape{};
                shape.shape = ShapeKind::Capsule;
                shape.size = {capsule.radius, capsule.center2.y - capsule.center1.y, 0};
                shape.offset = {0, (capsule.center1.y + capsule.center2.y) * 0.5f, 0};
                outline(shape, Matrix4x4::translation(transforms[i].position), gather.color);
            }
        },
        &gather);
    ecs::destroyQuery(query);
}

} // namespace crucible::physics
