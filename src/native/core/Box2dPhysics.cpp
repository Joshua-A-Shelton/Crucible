// The engine's 2D physics (ADR 0093): Box2D behind the 2D physics contract,
// built as Box3D is behind the 3D one (ADR 0085, Box3dPhysics.cpp):
//   "Physics2D.Sync" (PrePhysics)        bodies made, changed, and removed to match
//                                        RigidBody2D, Collider2D, and Transform2D
//   "Physics2D.Step" (Physics)           one Box2D step; its contact events kept
//   "Physics2D.WriteBack" (PostPhysics)  dynamic bodies' poses and velocities back
//   "Physics2D.Interpolate" (Update)     dynamic Transform2Ds between the last two ticks
// One Box2D world per ECS world, its tasks on the job system, its memory
// through Crucible's allocator. Bodies are in the XY plane: a Transform2D's
// position and angle (its depth and scale are kept; scale shapes the
// colliders).

#include "Hitboxes.h"

#include <crucible/AssetLoads.h>
#include <crucible/BinaryFile.h>
#include <crucible/ChainFile.h>
#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Log.h>
#include <crucible/Memory.h>
#include <crucible/Physics2D.h>
#include <crucible/Systems.h>
#include <crucible/Tilemap.h>
#include <crucible/TilemapFile.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include <box2d/box2d.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace crucible::physics2d {

namespace {

using physics::BodyKind;
using physics::ContactKind;
using physics::QueryFilter;

constexpr MemoryTag tag = MemoryTag::Physics;
// Box2D's mover cast has no shape filter, so what characters move through
// (triggers, characters' own capsules) leaves this bit out of its shapes'
// Box2D masks, and character moves query with only it (ADR 0094).
constexpr uint64_t moverBit = uint64_t{1} << 63;
constexpr int subSteps = 4;
constexpr uint32_t maxTasks = 64;

// --- Memory and tasks --------------------------------------------------------

void* allocate(size_t size, int alignment)
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
        b2SetAllocator(allocate, release);
        set = true;
    }
}

struct Task
{
    jobs::JobCounter counter;
};

// --- Conversions -------------------------------------------------------------

b2Vec2 toB2(Vector2 v)
{
    return {v.x, v.y};
}

Vector2 fromB2(b2Vec2 v)
{
    return {v.x, v.y};
}

void* entityData(ecs::Entity entity)
{
    return reinterpret_cast<void*>(static_cast<uintptr_t>(entity.value));
}

ecs::Entity entityOf(b2ShapeId shape)
{
    return {static_cast<uint64_t>(reinterpret_cast<uintptr_t>(b2Shape_GetUserData(shape)))};
}

bool same(Vector2 a, Vector2 b)
{
    return a.x == b.x && a.y == b.y;
}

// A 2D place in the world: from a world matrix (its XY plane), or for one.
struct Pose2D
{
    Vector2 position{};
    float angle = 0;
    Vector2 scale{1, 1};
};

// A mirrored matrix (one negative scale) comes out as a negative X scale,
// as a Transform2D scales before it turns: otherwise a mirror would read as
// half a turn, and shapes and write-backs would come out turned.
Pose2D poseOf(const Matrix4x4& m)
{
    Pose2D pose;
    pose.position = {m.rows[3].x, m.rows[3].y};
    const bool mirrored = m.rows[0].x * m.rows[1].y - m.rows[0].y * m.rows[1].x < 0;
    const float sign = mirrored ? -1.0f : 1.0f;
    pose.angle = std::atan2(sign * m.rows[0].y, sign * m.rows[0].x);
    pose.scale = {sign * std::sqrt(m.rows[0].x * m.rows[0].x + m.rows[0].y * m.rows[0].y),
                  std::sqrt(m.rows[1].x * m.rows[1].x + m.rows[1].y * m.rows[1].y)};
    return pose;
}

Pose2D worldPose(const ecs::World& world, ecs::Entity entity)
{
    return poseOf(scenes::computeWorldTransform(world, entity));
}

// A world position and angle as the entity's Transform2D: through its
// parent's world matrix, if it has one; its own scale and depth kept.
scenes::Transform2D localOf(const ecs::World& world, ecs::Entity entity, Vector2 position, float angle)
{
    scenes::Transform2D local = *ecs::read<scenes::Transform2D>(world, entity);
    const ecs::Entity parent = ecs::parentOf(world, entity);
    Matrix4x4 inverse;
    if (parent.value == 0 ||
        (ecs::read<scenes::Transform2D>(world, parent) == nullptr &&
         ecs::read<scenes::LocalTransform>(world, parent) == nullptr) ||
        !invert(scenes::computeWorldTransform(world, parent), inverse))
    {
        local.position = position;
        local.angle = angle;
        return local;
    }
    Matrix4x4 placed = Matrix4x4::rotationZ(angle);
    placed.rows[3] = {position.x, position.y, 0, 1};
    const Pose2D pose = poseOf(placed * inverse);
    local.position = pose.position;
    local.angle = pose.angle;
    return local;
}

bool same(const scenes::Transform2D& a, const scenes::Transform2D& b)
{
    return same(a.position, b.position) && a.angle == b.angle;
}

bool same(const Collider2D& a, const Collider2D& b)
{
    if (a.shape != b.shape || !same(a.size, b.size) || !same(a.offset, b.offset) || a.friction != b.friction ||
        a.restitution != b.restitution || a.isTrigger != b.isTrigger || a.pointCount != b.pointCount ||
        !(a.chain == b.chain))
    {
        return false;
    }
    for (int32_t i = 0; i < std::clamp(a.pointCount, 0, maxPolygonPoints); ++i)
    {
        if (!same(a.points[i], b.points[i]))
        {
            return false;
        }
    }
    return true;
}

// --- Worlds and bodies -------------------------------------------------------

struct Body
{
    b2BodyId body = b2_nullBodyId;
    b2ShapeId shape = b2_nullShapeId;
    Vector<b2ChainId> chains{tag};    // a Chain or Tilemap collider's, instead of a shape
    uint32_t chainVersion = 0;        // of the chain's points it was made from
    BodyKind kind = BodyKind::Static; // a Collider2D alone is static
    Collider2D collider{};
    Vector2 scale{1, 1};
    uint32_t layers = 1;
    uint32_t mask = physics::allLayers;
    float mass = 0;
    float gravityScale = 1;
    // What the physics last wrote, so the game's own writes can be told apart.
    scenes::Transform2D written{};
    Vector2 writtenLinear{};
    float writtenAngular = 0;
    // The last two ticks' world poses (dynamic), or the last target (kinematic).
    Vector2 previousPosition{};
    Vector2 currentPosition{};
    float previousAngle = 0;
    float currentAngle = 0;
    uint32_t seen = 0;
};

// A joint (ADR 0094): the Box2D joint made from a Joint2D, between the
// bodies it was made for, and what it was made from.
struct JointState
{
    b2JointId id = b2_nullJointId;
    Joint2D joint{};
    b2BodyId a = b2_nullBodyId;
    b2BodyId b = b2_nullBodyId;
    Vector2 worldAnchor{}; // where a joint to the world holds on
    bool toWorld = false;
    uint32_t seen = 0;
};

// A character (ADR 0094): what moveCharacter2D keeps between moves, and
// its kinematic capsule.
struct Character
{
    b2BodyId body = b2_nullBodyId;
    b2ShapeId shape = b2_nullShapeId;
    CharacterController2D controller{};
    Vector2 position{};
    float fallSpeed = 0;
    bool grounded = false;
    Vector2 groundNormal{0, 1};
    uint32_t seen = 0;
};

struct PhysicsWorld
{
    const ecs::World* world = nullptr;
    b2WorldId id = b2_nullWorldId;
    HashMap<uint64_t, Body> bodies{tag};
    HashMap<uint64_t, JointState> joints{tag};
    HashMap<uint64_t, Character> characters{tag};
    b2BodyId ground = b2_nullBodyId; // the static body joints to the world hold on to
    Vector<ContactEvent2D> events{tag};
    uint64_t eventsFrame = UINT64_MAX;
    uint32_t mark = 0;
    Task tasks[maxTasks];
    uint32_t taskCount = 0;
};

Vector<PhysicsWorld*> worlds{tag};

// Box2D's tasks as jobs: each enqueue runs one, each finish waits for it.
void* enqueueTask(b2TaskCallbackFcn* task, void* taskContext, void* userContext)
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

// The contract's layers (as 3D's, ADR 0085): a shape's category bits carry
// its layers (low 32) and mask (high 32); two collide when either's mask has
// a layer the other is on.
bool filter(b2ShapeId a, b2ShapeId b, void*)
{
    const uint64_t categoryA = b2Shape_GetFilter(a).categoryBits;
    const uint64_t categoryB = b2Shape_GetFilter(b).categoryBits;
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
    b2WorldDef def = b2DefaultWorldDef();
    def.gravity = toB2(gravity);
    def.workerCount = static_cast<int>(std::max(jobs::workerCount(), 1u));
    def.enqueueTask = enqueueTask;
    def.finishTask = finishTask;
    def.userTaskContext = made;
    made->id = b2CreateWorld(&def);
    b2World_SetCustomFilterCallback(made->id, filter, nullptr);
    worlds.pushBack(made);
    return *made;
}

// --- Chains (ADR 0095) ---------------------------------------------------------
//
// Chain assets' points, read from their CHAN files when a collider first
// uses them and kept for every world. A reload reads them again under a new
// version, so the bodies made from the old points rebuild at their next sync.

struct ChainData
{
    assets::AssetId id{};
    Vector<Vector2> points{tag}; // empty: unreadable (said once)
    bool loop = false;
    // A tilemap's outlines (ADR 0108): each loop's point count, in turn;
    // empty for a chain, whose points are one chain.
    Vector<uint32_t> loops{tag};
    uint32_t version = 0;
    bool loading = false; // on a job (ADR 0099): bodies needing it wait
};

Vector<ChainData*> chainData{tag};
uint32_t chainVersions = 0;

bool isNone(const assets::AssetId& id)
{
    return id == assets::AssetId{};
}

// A tilemap's outlines from its map file's bytes (ADR 0108), every loop
// closed; false if they're wrong.
bool readTilemapOutlines(std::span<const uint8_t> bytes, Vector<Vector2>& points, Vector<uint32_t>& loops)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::tilemapFileKind, assets::tilemapFileVersion);
    if (!opened)
    {
        return false;
    }
    binary::File* file = opened.value();
    auto read = [&](const char (&name)[5], auto& out) {
        using T = std::remove_reference_t<decltype(out[0])>;
        const int32_t index = binary::findSection(*file, binary::tag(name));
        if (index < 0)
        {
            out.clear();
            return true; // a map without collision
        }
        out.resize(binary::sectionInfo(*file, static_cast<uint32_t>(index)).size / sizeof(T));
        return static_cast<bool>(
            binary::readSection(*file, static_cast<uint32_t>(index),
                                std::span(reinterpret_cast<uint8_t*>(out.data()), out.size() * sizeof(T))));
    };
    Vector<assets::TilemapLoop> ranges(tag);
    bool good = read("LOOP", ranges) && read("PNTS", points);
    binary::closeFile(file);
    for (const assets::TilemapLoop& range : ranges)
    {
        good = good && range.count >= 3 && uint64_t{range.first} + range.count <= points.size();
        loops.pushBack(range.count);
    }
    return good;
}

// Its points from the chain file's bytes, or a tilemap's outlines from its
// map file's; false if they're wrong. Any thread (a load's job, ADR 0099).
bool readChainFrom(std::span<const uint8_t> bytes, Vector<Vector2>& points, bool& loop, Vector<uint32_t>& loops)
{
    points.clear();
    loops.clear();
    Result<binary::File*> opened = binary::parseFile(bytes, assets::chainFileKind, assets::chainFileVersion);
    if (!opened)
    {
        loop = true;
        if (!readTilemapOutlines(bytes, points, loops))
        {
            points.clear();
            loops.clear();
            return false;
        }
        return true;
    }
    binary::File* file = opened.value();
    const int32_t info = binary::findSection(*file, binary::tag("INFO"));
    const int32_t pointSection = binary::findSection(*file, binary::tag("PNTS"));
    assets::ChainInfo header{};
    bool good = info >= 0 && pointSection >= 0 &&
                binary::sectionInfo(*file, static_cast<uint32_t>(info)).size == sizeof(header) &&
                binary::readSection(*file, static_cast<uint32_t>(info),
                                    std::span(reinterpret_cast<uint8_t*>(&header), sizeof(header)));
    if (good && header.pointCount >= (header.loop != 0 ? 3u : 2u) &&
        binary::sectionInfo(*file, static_cast<uint32_t>(pointSection)).size == header.pointCount * sizeof(Vector2))
    {
        points.resize(header.pointCount);
        loop = header.loop != 0;
        good = static_cast<bool>(
            binary::readSection(*file, static_cast<uint32_t>(pointSection),
                                std::span(reinterpret_cast<uint8_t*>(points.data()), points.size() * sizeof(Vector2))));
    }
    else
    {
        good = false;
    }
    binary::closeFile(file);
    if (!good)
    {
        points.clear();
    }
    return good;
}

// Its points from the asset source; false if it has none or they're wrong.
bool readChain(ChainData& data)
{
    data.points.clear();
    data.version = ++chainVersions;
    Vector<uint8_t> bytes(tag);
    auto deliver = [](void* context, const uint8_t* source, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(source, static_cast<size_t>(size)));
    };
    if (assets::assetSource().read(&data.id, &bytes, deliver) != 0)
    {
        return false;
    }
    return readChainFrom(bytes.span(), data.points, data.loop, data.loops);
}

// A chain read on a job into the entry waiting for it (ADR 0099).
struct ChainLoad
{
    ChainData* data = nullptr;
    Vector<Vector2> points{tag};
    Vector<uint32_t> loops{tag};
    bool loop = false;
    bool good = false;
};

// A chain asset's points, read if they haven't been; null for none.
const ChainData* chainOf(const assets::AssetId& id)
{
    if (isNone(id))
    {
        return nullptr;
    }
    for (const ChainData* data : chainData)
    {
        if (data->id == id)
        {
            return data;
        }
    }
    ChainData* data = create<ChainData>(tag);
    data->id = id;
    data->loading = true;
    chainData.pushBack(data);
    auto* load = create<ChainLoad>(tag);
    load->data = data;
    assets::startLoad({
        .id = id,
        .context = load,
        .prepare =
            [](void* context, std::span<const uint8_t> bytes, bool found) {
                auto& self = *static_cast<ChainLoad*>(context);
                self.good = found && readChainFrom(bytes, self.points, self.loop, self.loops);
            },
        .publish =
            [](void* context) {
                auto* self = static_cast<ChainLoad*>(context);
                for (ChainData* entry : chainData)
                {
                    if (entry == self->data) // not freed meanwhile
                    {
                        entry->loading = false;
                        entry->points = static_cast<Vector<Vector2>&&>(self->points);
                        entry->loops = static_cast<Vector<uint32_t>&&>(self->loops);
                        entry->loop = self->loop;
                        entry->version = ++chainVersions;
                        if (!self->good)
                        {
                            entry->points.clear();
                            entry->loops.clear();
                            log::warning("a Chain or Tilemap collider's points can't be read (reimport it)");
                        }
                    }
                }
                destroy(tag, self);
            },
    });
    return data;
}

// The collider as the physics builds it: a Tilemap one's chain is its
// entity's map (ADR 0108), whose outlines are read as a chain's points.
Collider2D resolvedCollider(const ecs::World& world, ecs::Entity entity, const Collider2D& collider)
{
    Collider2D resolved = collider;
    if (collider.shape == ShapeKind2D::Tilemap)
    {
        const render2d::Tilemap* tilemap = ecs::componentIdOf<render2d::Tilemap> != ecs::invalidComponent
                                               ? ecs::read<render2d::Tilemap>(world, entity)
                                               : nullptr;
        resolved.chain = tilemap != nullptr ? tilemap->map : assets::AssetId{};
    }
    return resolved;
}

bool chainShaped(const Collider2D& collider)
{
    return collider.shape == ShapeKind2D::Chain || collider.shape == ShapeKind2D::Tilemap;
}

// Whether a Chain or Tilemap collider's points are still to come (a load
// started now if need be). Bodies wait for them.
bool chainLoading(const Collider2D& collider)
{
    if (!chainShaped(collider))
    {
        return false;
    }
    const ChainData* data = chainOf(collider.chain);
    return data != nullptr && data->loading;
}

void freeChains()
{
    for (ChainData* data : chainData)
    {
        destroy(tag, data);
    }
    chainData.clear();
}

b2BodyType typeOf(BodyKind kind)
{
    return kind == BodyKind::Dynamic ? b2_dynamicBody : kind == BodyKind::Kinematic ? b2_kinematicBody : b2_staticBody;
}

Vector2 scaled(Vector2 v, Vector2 scale)
{
    return {v.x * scale.x, v.y * scale.y};
}

// One chain of points onto the body, at the entity's scale. Mirrored (one
// negative scale), the points run the other way, so the solid side stays
// the one drawn; a loop gets a fourth point if it has three (Box2D's least).
void makeChain(Body& body, const b2ShapeDef& def, std::span<const Vector2> source, bool loop, Vector2 scale,
               Vector2 offset)
{
    const bool mirrored = (scale.x < 0) != (scale.y < 0);
    Vector<b2Vec2> points(tag);
    for (size_t i = 0; i < source.size(); ++i)
    {
        points.pushBack(toB2(scaled(source[mirrored ? source.size() - 1 - i : i], scale) + offset));
    }
    if (loop && points.size() == 3)
    {
        points.pushBack(b2Lerp(points[2], points[0], 0.5f));
    }
    if (points.size() < (loop ? 4u : 2u))
    {
        return;
    }
    const size_t count = points.size();
    b2ChainDef chain = b2DefaultChainDef();
    chain.userData = def.userData;
    chain.points = points.data();
    chain.pointCount = static_cast<int>(count);
    chain.materials = &def.material;
    chain.materialCount = 1;
    chain.filter = def.filter;
    chain.isLoop = loop;
    chain.enableSensorEvents = true;
    // An open chain's ends, extended straight on, so nothing catches there.
    chain.ghost1 = b2Add(points[0], b2Sub(points[0], points[1]));
    chain.ghost2 = b2Add(points[count - 1], b2Sub(points[count - 1], points[count - 2]));
    body.chains.pushBack(b2CreateChain(body.body, &chain));
}

// The shape, from the Collider2D at the entity's world scale (Box2D shapes
// have none): boxes and polygons by each axis, circles and capsules by the
// larger one.
void buildShape(Body& body, ecs::Entity entity, const Collider2D& collider, Vector2 scale, uint32_t layers,
                uint32_t mask)
{
    if (B2_IS_NON_NULL(body.shape))
    {
        b2DestroyShape(body.shape, true);
        body.shape = b2_nullShapeId;
    }
    for (const b2ChainId chain : body.chains)
    {
        b2DestroyChain(chain);
    }
    body.chains.clear();
    body.collider = collider;
    body.scale = scale;
    body.layers = layers;
    body.mask = mask;
    b2ShapeDef def = b2DefaultShapeDef();
    def.userData = entityData(entity);
    def.material.friction = std::max(collider.friction, 0.0f);
    def.material.restitution = std::clamp(collider.restitution, 0.0f, 1.0f);
    def.filter = {uint64_t{layers} | (uint64_t{mask} << 32), collider.isTrigger ? ~moverBit : UINT64_MAX, 0};
    def.enableCustomFiltering = true;
    def.isSensor = collider.isTrigger;
    def.enableSensorEvents = true;
    def.enableContactEvents = true;
    const Vector2 size{std::fabs(scale.x), std::fabs(scale.y)};
    const Vector2 offset = scaled(collider.offset, scale);
    const float largest = std::max(size.x, size.y);
    switch (collider.shape)
    {
    case ShapeKind2D::Box:
    {
        const b2Polygon box =
            b2MakeOffsetBox(std::max(collider.size.x * size.x * 0.5f, 0.001f),
                            std::max(collider.size.y * size.y * 0.5f, 0.001f), toB2(offset), b2Rot_identity);
        body.shape = b2CreatePolygonShape(body.body, &def, &box);
        break;
    }
    case ShapeKind2D::Circle:
    {
        const b2Circle circle{toB2(offset), std::max(collider.size.x * largest, 0.001f)};
        body.shape = b2CreateCircleShape(body.body, &def, &circle);
        break;
    }
    case ShapeKind2D::Capsule:
    {
        const float half = std::max(collider.size.y * largest * 0.5f, 0.0f);
        const b2Capsule capsule{toB2(offset - Vector2{0, half}), toB2(offset + Vector2{0, half}),
                                std::max(collider.size.x * largest, 0.001f)};
        body.shape = b2CreateCapsuleShape(body.body, &def, &capsule);
        break;
    }
    case ShapeKind2D::Polygon:
    {
        b2Vec2 points[maxPolygonPoints];
        const int32_t count = std::clamp(collider.pointCount, 0, maxPolygonPoints);
        for (int32_t i = 0; i < count; ++i)
        {
            points[i] = toB2(scaled(collider.points[i], scale) + offset);
        }
        const b2Hull hull = b2ComputeHull(points, count);
        if (hull.count >= 3)
        {
            const b2Polygon polygon = b2MakePolygon(&hull, 0);
            body.shape = b2CreatePolygonShape(body.body, &def, &polygon);
        }
        break;
    }
    case ShapeKind2D::Chain:
    case ShapeKind2D::Tilemap:
    {
        const ChainData* data = chainOf(collider.chain);
        body.chainVersion = data != nullptr ? data->version : 0;
        if (data == nullptr || data->loading || data->points.empty())
        {
            break;
        }
        // Box2D's chains have no mass, and can't be sensors.
        if (body.kind == BodyKind::Dynamic || collider.isTrigger)
        {
            log::warning("a Chain or Tilemap collider is only for static and kinematic bodies, and can't be a trigger");
            break;
        }
        // A chain is one run of points; a tilemap's outlines are several loops.
        const uint32_t whole = static_cast<uint32_t>(data->points.size());
        const std::span<const uint32_t> counts =
            data->loops.empty() ? std::span<const uint32_t>(&whole, 1) : data->loops.span();
        uint32_t first = 0;
        for (const uint32_t count : counts)
        {
            makeChain(body, def, std::span(data->points.data() + first, count), data->loop, scale, offset);
            first += count;
        }
        break;
    }
    }
}

// The body's mass as the RigidBody2D gives it: Box2D's from its shape (at
// density 1), scaled so the total is right.
void applyMass(Body& body, float mass)
{
    body.mass = mass;
    if (body.kind != BodyKind::Dynamic || B2_IS_NULL(body.shape))
    {
        return;
    }
    b2MassData data = b2Body_GetMassData(body.body);
    if (data.mass > 0 && mass > 0)
    {
        data.rotationalInertia *= mass / data.mass;
        data.mass = mass;
        b2Body_SetMassData(body.body, data);
    }
}

void syncBody(ecs::World& world, PhysicsWorld& physics, ecs::Entity entity, float step)
{
    const Collider2D collider = resolvedCollider(world, entity, *ecs::read<Collider2D>(world, entity));
    const RigidBody2D* rigid = ecs::read<RigidBody2D>(world, entity);
    const BodyKind kind = rigid != nullptr ? rigid->kind : BodyKind::Static;
    const uint32_t layers = rigid != nullptr ? rigid->layers : 1u;
    const uint32_t mask = rigid != nullptr ? rigid->mask : physics::allLayers;
    const Pose2D pose = worldPose(world, entity);
    const scenes::Transform2D local = *ecs::read<scenes::Transform2D>(world, entity);

    // A body is made once its chain is loaded (ADR 0099); one that has a
    // shape keeps it until the new chain is.
    const bool waiting = chainLoading(collider);
    Body* body = physics.bodies.find(entity.value);
    if (body == nullptr && waiting)
    {
        return;
    }
    if (body == nullptr)
    {
        physics.bodies.insertOrAssign(entity.value, Body{});
        body = physics.bodies.find(entity.value);
    }
    body->seen = physics.mark;

    if (B2_IS_NULL(body->body))
    {
        b2BodyDef def = b2DefaultBodyDef();
        def.type = typeOf(kind);
        def.position = toB2(pose.position);
        def.rotation = b2MakeRot(pose.angle);
        if (rigid != nullptr)
        {
            def.linearVelocity = toB2(rigid->linearVelocity);
            def.angularVelocity = rigid->angularVelocity;
            def.gravityScale = rigid->gravityScale;
            body->writtenLinear = rigid->linearVelocity;
            body->writtenAngular = rigid->angularVelocity;
            body->gravityScale = rigid->gravityScale;
        }
        def.userData = entityData(entity);
        body->body = b2CreateBody(physics.id, &def);
        body->kind = kind;
        body->previousPosition = body->currentPosition = pose.position;
        body->previousAngle = body->currentAngle = pose.angle;
        body->written = local;
        buildShape(*body, entity, collider, pose.scale, layers, mask);
        applyMass(*body, rigid != nullptr ? rigid->mass : 0);
        return;
    }

    const bool kindChanged = kind != body->kind;
    if (kindChanged)
    {
        b2Body_SetType(body->body, typeOf(kind));
        body->kind = kind;
    }
    if (!waiting && (!same(collider, body->collider) ||
                     std::fabs(pose.scale.x - body->scale.x) + std::fabs(pose.scale.y - body->scale.y) > 1e-5f ||
                     layers != body->layers || mask != body->mask || kindChanged ||
                     (chainShaped(collider) && chainOf(collider.chain) != nullptr &&
                      chainOf(collider.chain)->version != body->chainVersion)))
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
            b2Body_SetGravityScale(body->body, rigid->gravityScale);
            body->gravityScale = rigid->gravityScale;
        }
        // Velocities the game set (not the ones written back) are pushed in.
        if (kind == BodyKind::Dynamic && !same(rigid->linearVelocity, body->writtenLinear))
        {
            b2Body_SetLinearVelocity(body->body, toB2(rigid->linearVelocity));
            body->writtenLinear = rigid->linearVelocity;
        }
        if (kind == BodyKind::Dynamic && rigid->angularVelocity != body->writtenAngular)
        {
            b2Body_SetAngularVelocity(body->body, rigid->angularVelocity);
            body->writtenAngular = rigid->angularVelocity;
        }
    }

    // A transform the game wrote, by kind.
    switch (kind)
    {
    case BodyKind::Dynamic:
        if (!same(local, body->written))
        {
            b2Body_SetTransform(body->body, toB2(pose.position), b2MakeRot(pose.angle));
            b2Body_SetAwake(body->body, true);
            body->previousPosition = body->currentPosition = pose.position;
            body->previousAngle = body->currentAngle = pose.angle;
            body->written = local;
        }
        break;
    case BodyKind::Kinematic:
        // A new target is reached over the tick, pushing what's in the way.
        if (!same(pose.position, body->currentPosition) || pose.angle != body->currentAngle)
        {
            b2Body_SetTargetTransform(body->body, {toB2(pose.position), b2MakeRot(pose.angle)}, step, true);
            body->currentPosition = pose.position;
            body->currentAngle = pose.angle;
        }
        else
        {
            b2Body_SetLinearVelocity(body->body, {0, 0});
            b2Body_SetAngularVelocity(body->body, 0);
        }
        break;
    case BodyKind::Static:
        if (!same(pose.position, body->currentPosition) || pose.angle != body->currentAngle)
        {
            b2Body_SetTransform(body->body, toB2(pose.position), b2MakeRot(pose.angle));
            body->currentPosition = pose.position;
            body->currentAngle = pose.angle;
        }
        break;
    }
}

// --- Joints (ADR 0094) ---------------------------------------------------------

constexpr float minimumLength = 0.01f; // Box2D's distance joints need a length

bool sameJoint(const Joint2D& a, const Joint2D& b)
{
    return a.kind == b.kind && a.other.value == b.other.value && same(a.anchor, b.anchor) &&
           same(a.otherAnchor, b.otherAnchor) && same(a.axis, b.axis) && a.lowerLimit == b.lowerLimit &&
           a.upperLimit == b.upperLimit;
}

b2BodyId groundOf(PhysicsWorld& physics)
{
    if (B2_IS_NULL(physics.ground))
    {
        b2BodyDef def = b2DefaultBodyDef();
        def.type = b2_staticBody;
        physics.ground = b2CreateBody(physics.id, &def);
    }
    return physics.ground;
}

void destroyJoint(JointState& state)
{
    if (B2_IS_NON_NULL(state.id) && b2Joint_IsValid(state.id))
    {
        b2DestroyJoint(state.id);
    }
    state.id = b2_nullJointId;
}

// Frame A at the anchor, turned along the axis (a slider moves along its
// x); frame B where frame A is now, so angles and positions count from how
// the bodies sit when it's made.
void makeJoint(PhysicsWorld& physics, ecs::World& world, ecs::Entity entity, const Joint2D& joint, b2BodyId a,
               b2BodyId b, JointState& state)
{
    const Pose2D poseA = worldPose(world, entity);
    const Vector2 anchorA = scaled(joint.anchor, poseA.scale);
    const float turnA = length(joint.axis) > 1e-6f ? std::atan2(joint.axis.y, joint.axis.x) : 0;
    const b2Rot rotA = b2MakeRot(poseA.angle);
    const Vector2 worldAnchorA = poseA.position + fromB2(b2RotateVector(rotA, toB2(anchorA)));
    const float worldTurn = poseA.angle + turnA;
    Vector2 anchorB{}, worldAnchorB{};
    float turnB = 0;
    if (state.toWorld)
    {
        anchorB = worldAnchorB = state.worldAnchor = worldAnchorA;
        turnB = worldTurn;
    }
    else
    {
        const Pose2D poseB = worldPose(world, joint.other);
        anchorB = scaled(joint.otherAnchor, poseB.scale);
        turnB = worldTurn - poseB.angle;
        worldAnchorB = poseB.position + fromB2(b2RotateVector(b2MakeRot(poseB.angle), toB2(anchorB)));
    }
    const auto fill = [&](b2JointDef& base) {
        base.userData = entityData(entity);
        base.bodyIdA = a;
        base.bodyIdB = b;
        base.localFrameA = {toB2(anchorA), b2MakeRot(turnA)};
        base.localFrameB = {toB2(anchorB), b2MakeRot(turnB)};
        base.collideConnected = false;
    };
    const bool limited = joint.lowerLimit < joint.upperLimit; // off unless set (ADR 0094)
    switch (joint.kind)
    {
    case JointKind2D::Fixed:
    {
        b2WeldJointDef def = b2DefaultWeldJointDef();
        fill(def.base);
        state.id = b2CreateWeldJoint(physics.id, &def);
        break;
    }
    case JointKind2D::Hinge:
    {
        b2RevoluteJointDef def = b2DefaultRevoluteJointDef();
        fill(def.base);
        // Box2D measures body B (the other) against body A (this one): the
        // entity's own turn is the opposite, so its limits are swapped and negated.
        def.enableLimit = limited;
        def.lowerAngle = std::clamp(-joint.upperLimit, -3.11f, 3.11f);
        def.upperAngle = std::clamp(-joint.lowerLimit, -3.11f, 3.11f);
        state.id = b2CreateRevoluteJoint(physics.id, &def);
        break;
    }
    case JointKind2D::Distance:
    {
        b2DistanceJointDef def = b2DefaultDistanceJointDef();
        fill(def.base);
        const float now = length(worldAnchorB - worldAnchorA);
        if (limited)
        {
            def.enableSpring = true; // free between the limits: a rope or rod
            def.hertz = 0;
            def.enableLimit = true;
            def.minLength = std::max(joint.lowerLimit, minimumLength);
            def.maxLength = std::max(joint.upperLimit, def.minLength);
            def.length = std::clamp(now, def.minLength, def.maxLength);
        }
        else
        {
            def.length = std::max(now, minimumLength);
        }
        state.id = b2CreateDistanceJoint(physics.id, &def);
        break;
    }
    case JointKind2D::Slider:
    {
        b2PrismaticJointDef def = b2DefaultPrismaticJointDef();
        fill(def.base);
        def.enableLimit = limited; // the entity's own movement, as a hinge's
        def.lowerTranslation = -joint.upperLimit;
        def.upperTranslation = -joint.lowerLimit;
        state.id = b2CreatePrismaticJoint(physics.id, &def);
        break;
    }
    }
    state.joint = joint;
    state.a = a;
    state.b = b;
}

void syncJoint(PhysicsWorld& physics, ecs::World& world, ecs::Entity entity)
{
    const Joint2D& joint = *ecs::read<Joint2D>(world, entity);
    JointState* state = physics.joints.find(entity.value);
    if (state == nullptr)
    {
        physics.joints.insertOrAssign(entity.value, JointState{});
        state = physics.joints.find(entity.value);
    }
    state->seen = physics.mark;
    const Body* own = physics.bodies.find(entity.value);
    const bool toWorld = joint.other.value == 0;
    const Body* other = toWorld ? nullptr : physics.bodies.find(joint.other.value);
    if (own == nullptr || B2_IS_NULL(own->body) || (!toWorld && (other == nullptr || B2_IS_NULL(other->body))) ||
        joint.other.value == entity.value)
    {
        destroyJoint(*state);
        return;
    }
    const b2BodyId a = own->body;
    const b2BodyId b = toWorld ? groundOf(physics) : other->body;
    const bool valid = B2_IS_NON_NULL(state->id) && b2Joint_IsValid(state->id);
    if (valid && sameJoint(joint, state->joint) && B2_ID_EQUALS(a, state->a) && B2_ID_EQUALS(b, state->b) &&
        state->toWorld == toWorld)
    {
        return;
    }
    destroyJoint(*state);
    state->toWorld = toWorld;
    makeJoint(physics, world, entity, joint, a, b, *state);
}

// --- Characters (ADR 0094) -----------------------------------------------------

// A character's capsule from its feet: riding a step height above them, or
// (its body) reaching down to them.
b2Capsule characterCapsule(const CharacterController2D& controller, bool toFeet)
{
    const float radius = std::max(controller.radius, 0.01f);
    const float bottom = radius + (toFeet ? 0.0f : std::max(controller.stepHeight, 0.0f));
    const float top = radius + std::max(controller.stepHeight, 0.0f) + std::max(controller.height, 0.0f);
    return {{0, bottom}, {0, std::max(top, bottom)}, radius};
}

bool sameController(const CharacterController2D& a, const CharacterController2D& b)
{
    return a.radius == b.radius && a.height == b.height && a.stepHeight == b.stepHeight && a.layers == b.layers &&
           a.mask == b.mask;
}

void syncCharacter(ecs::World& world, PhysicsWorld& physics, ecs::Entity entity, float step)
{
    const CharacterController2D& controller = *ecs::read<CharacterController2D>(world, entity);
    const Vector2 at = ecs::read<scenes::Transform2D>(world, entity)->position;
    Character* character = physics.characters.find(entity.value);
    if (character == nullptr)
    {
        physics.characters.insertOrAssign(entity.value, Character{});
        character = physics.characters.find(entity.value);
    }
    character->seen = physics.mark;
    if (B2_IS_NULL(character->body))
    {
        b2BodyDef def = b2DefaultBodyDef();
        def.type = b2_kinematicBody;
        def.position = toB2(at);
        def.userData = entityData(entity);
        character->body = b2CreateBody(physics.id, &def);
        character->position = at;
    }
    if (B2_IS_NULL(character->shape) || !sameController(controller, character->controller))
    {
        if (B2_IS_NON_NULL(character->shape))
        {
            b2DestroyShape(character->shape, false);
        }
        b2ShapeDef def = b2DefaultShapeDef();
        def.userData = entityData(entity);
        // Not for movers: its own moves (and others') pass it by.
        def.filter = {uint64_t{controller.layers} | (uint64_t{controller.mask} << 32), ~moverBit, 0};
        def.enableCustomFiltering = true;
        def.enableContactEvents = true;
        def.enableSensorEvents = true;
        const b2Capsule capsule = characterCapsule(controller, true);
        character->shape = b2CreateCapsuleShape(character->body, &def, &capsule);
        character->controller = controller;
    }
    if (!same(at, character->position))
    {
        b2Body_SetTargetTransform(character->body, {toB2(at), b2Rot_identity}, step, true);
        character->position = at;
    }
    else
    {
        b2Body_SetLinearVelocity(character->body, {0, 0});
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
    const ecs::ComponentId required[] = {ecs::componentIdOf<Collider2D>, ecs::componentIdOf<scenes::Transform2D>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* chunk = ecs::entitiesOf(view);
            const size_t count = ecs::readColumn<Collider2D>(view).size();
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
    // Bodies whose entity, or Collider2D, is gone.
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
        if (B2_IS_NON_NULL(body->body))
        {
            b2DestroyBody(body->body);
        }
        physics.bodies.erase(key);
    }

    // Joints, after the bodies they join; characters; and those gone.
    const auto gatherEntities = [&](std::span<const ecs::ComponentId> components, Vector<ecs::Entity>& into) {
        ecs::Query* found = ecs::createQuery(world, {.required = components});
        ecs::forEachChunk(
            *found,
            [](const ecs::ChunkView& view, void* data) {
                const ecs::Entity* chunk = ecs::entitiesOf(view);
                for (uint32_t i = 0; i < ecs::entityCountOf(view); ++i)
                {
                    static_cast<Vector<ecs::Entity>*>(data)->pushBack(chunk[i]);
                }
            },
            &into);
        ecs::destroyQuery(found);
    };
    Vector<ecs::Entity> jointed(tag);
    const ecs::ComponentId joints[] = {ecs::componentIdOf<Joint2D>, ecs::componentIdOf<scenes::Transform2D>};
    gatherEntities(joints, jointed);
    for (const ecs::Entity entity : jointed)
    {
        syncJoint(physics, world, entity);
    }
    gone.clear();
    for (const auto& entry : physics.joints)
    {
        if (entry.value.seen != physics.mark)
        {
            gone.pushBack(entry.key);
        }
    }
    for (const uint64_t key : gone)
    {
        destroyJoint(*physics.joints.find(key));
        physics.joints.erase(key);
    }
    Vector<ecs::Entity> characters(tag);
    const ecs::ComponentId controlled[] = {ecs::componentIdOf<CharacterController2D>,
                                           ecs::componentIdOf<scenes::Transform2D>};
    gatherEntities(controlled, characters);
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
        if (B2_IS_NON_NULL(character->body))
        {
            b2DestroyBody(character->body);
        }
        physics.characters.erase(key);
    }
}

Vector2 contactPoint(b2ContactId contact, Vector2& normal)
{
    if (!b2Contact_IsValid(contact))
    {
        return {};
    }
    const b2ContactData data = b2Contact_GetData(contact);
    if (data.manifold.pointCount == 0)
    {
        return {};
    }
    normal = fromB2(data.manifold.normal);
    return fromB2(b2Body_GetWorldCenter(b2Shape_GetBody(data.shapeIdA))) + fromB2(data.manifold.points[0].anchorA);
}

void stepWorld(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    PhysicsWorld& physics = worldOf(*context.world);
    physics.taskCount = 0;
    b2World_Step(physics.id, stepOf(context), subSteps);

    const b2ContactEvents contacts = b2World_GetContactEvents(physics.id);
    for (int i = 0; i < contacts.beginCount; ++i)
    {
        const b2ContactBeginTouchEvent& event = contacts.beginEvents[i];
        if (!b2Shape_IsValid(event.shapeIdA) || !b2Shape_IsValid(event.shapeIdB))
        {
            continue;
        }
        Vector2 normal{};
        const Vector2 point = contactPoint(event.contactId, normal);
        physics.events.pushBack(
            {ContactKind::Begin, false, entityOf(event.shapeIdA), entityOf(event.shapeIdB), point, normal});
    }
    for (int i = 0; i < contacts.endCount; ++i)
    {
        const b2ContactEndTouchEvent& event = contacts.endEvents[i];
        if (!b2Shape_IsValid(event.shapeIdA) || !b2Shape_IsValid(event.shapeIdB))
        {
            continue;
        }
        physics.events.pushBack({ContactKind::End, false, entityOf(event.shapeIdA), entityOf(event.shapeIdB), {}, {}});
    }
    const b2SensorEvents sensors = b2World_GetSensorEvents(physics.id);
    for (int i = 0; i < sensors.beginCount; ++i)
    {
        const b2SensorBeginTouchEvent& event = sensors.beginEvents[i];
        if (b2Shape_IsValid(event.sensorShapeId) && b2Shape_IsValid(event.visitorShapeId))
        {
            physics.events.pushBack(
                {ContactKind::Begin, true, entityOf(event.sensorShapeId), entityOf(event.visitorShapeId), {}, {}});
        }
    }
    for (int i = 0; i < sensors.endCount; ++i)
    {
        const b2SensorEndTouchEvent& event = sensors.endEvents[i];
        if (b2Shape_IsValid(event.sensorShapeId) && b2Shape_IsValid(event.visitorShapeId))
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
        if (body.kind != BodyKind::Dynamic || B2_IS_NULL(body.body))
        {
            continue;
        }
        const ecs::Entity entity{entry.key};
        body.previousPosition = body.currentPosition;
        body.previousAngle = body.currentAngle;
        body.currentPosition = fromB2(b2Body_GetPosition(body.body));
        body.currentAngle = b2Rot_GetAngle(b2Body_GetRotation(body.body));
        if (RigidBody2D* rigid = ecs::get<RigidBody2D>(world, entity))
        {
            rigid->linearVelocity = body.writtenLinear = fromB2(b2Body_GetLinearVelocity(body.body));
            rigid->angularVelocity = body.writtenAngular = b2Body_GetAngularVelocity(body.body);
        }
        const scenes::Transform2D local = localOf(world, entity, body.currentPosition, body.currentAngle);
        *ecs::get<scenes::Transform2D>(world, entity) = local;
        body.written = local;
    }
}

// The angle between two others, the short way round.
float lerpAngle(float a, float b, float t)
{
    float d = std::remainder(b - a, 6.2831853f);
    return a + d * t;
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
        if (body.kind != BodyKind::Dynamic || ecs::read<scenes::Transform2D>(world, entity) == nullptr)
        {
            continue;
        }
        const Vector2 position = body.previousPosition + (body.currentPosition - body.previousPosition) * alpha;
        const float angle = lerpAngle(body.previousAngle, body.currentAngle, alpha);
        const scenes::Transform2D local = localOf(world, entity, position, angle);
        *ecs::get<scenes::Transform2D>(world, entity) = local;
        body.written = local;
    }
}

// --- Queries (as 3D's, ADR 0090) ----------------------------------------------

struct QueryRecord
{
    bool cast = false;     // a ray or a shape cast; else an overlap
    bool hasShape = false; // a shape cast or an overlap
    QueryShape2D shape{};
    Vector2 from{};
    float angle = 0;
    Vector2 direction{};
    float distance = 0;
    RayHit2D hit{};
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

b2QueryFilter queryFilter(const QueryFilter& filter)
{
    b2QueryFilter b2 = b2DefaultQueryFilter();
    b2.categoryBits = UINT64_MAX;
    b2.maskBits = filter.layers;
    return b2;
}

bool admits(b2ShapeId shape, const QueryFilter& filter)
{
    return (filter.triggers || !b2Shape_IsSensor(shape)) &&
           (filter.ignore.value == 0 || entityOf(shape).value != filter.ignore.value);
}

// A query shape as Box2D's point cloud round the origin, turned: a box's
// corners, a circle's center, a capsule's two cap centers (along its Y).
bool makeProxy(const QueryShape2D& shape, float angle, b2ShapeProxy& proxy)
{
    const b2Rot turn = b2MakeRot(angle);
    switch (shape.kind)
    {
    case ShapeKind2D::Box:
    {
        const Vector2 half = shape.size * 0.5f;
        const b2Vec2 corners[4] = {b2RotateVector(turn, {-half.x, -half.y}), b2RotateVector(turn, {half.x, -half.y}),
                                   b2RotateVector(turn, {half.x, half.y}), b2RotateVector(turn, {-half.x, half.y})};
        proxy = b2MakeProxy(corners, 4, 0);
        return true;
    }
    case ShapeKind2D::Circle:
    {
        const b2Vec2 center{0, 0};
        proxy = b2MakeProxy(&center, 1, std::max(shape.size.x, 0.0f));
        return true;
    }
    case ShapeKind2D::Capsule:
    {
        const float half = std::max(shape.size.y, 0.0f) * 0.5f;
        const b2Vec2 ends[2] = {b2RotateVector(turn, {0, -half}), b2RotateVector(turn, {0, half})};
        proxy = b2MakeProxy(ends, 2, std::max(shape.size.x, 0.0f));
        return true;
    }
    default:
        return false; // polygons aren't query shapes
    }
}

struct Closest
{
    QueryFilter filter;
    float distance;
    bool hitsAtStart = true; // a shape cast's; a ray's doesn't hit the collider it starts in
    RayHit2D hit{};
};

float keepClosest(b2ShapeId shape, b2Pos point, b2Vec2 normal, float fraction, void* context)
{
    Closest& closest = *static_cast<Closest*>(context);
    if (!admits(shape, closest.filter) || (!closest.hitsAtStart && fraction <= 0))
    {
        return -1; // not this one: go on
    }
    closest.hit = {true, entityOf(shape), fromB2(point), fromB2(normal), fraction * closest.distance};
    return fraction;
}

struct Overlaps
{
    QueryFilter filter;
    std::span<ecs::Entity> into;
    Vector<uint64_t> found{tag};
};

bool gather(b2ShapeId shape, void* context)
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

RayHit2D raycastBox2D(ecs::World& world, Vector2 from, Vector2 direction, float distance, QueryFilter filter)
{
    const PhysicsWorld* physics = find(world);
    Closest closest{filter, distance, false};
    if (physics != nullptr && distance > 0)
    {
        b2World_CastRay(physics->id, toB2(from), toB2(direction * distance), queryFilter(filter), keepClosest,
                        &closest);
    }
    record({true, false, {}, from, 0, direction, distance, closest.hit, 0});
    return closest.hit;
}

RayHit2D shapeCastBox2D(ecs::World& world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
                        float distance, QueryFilter filter)
{
    const PhysicsWorld* physics = find(world);
    Closest closest{filter, distance};
    b2ShapeProxy proxy;
    if (physics != nullptr && distance >= 0 && makeProxy(shape, angle, proxy))
    {
        b2World_CastShape(physics->id, toB2(from), &proxy, toB2(direction * distance), queryFilter(filter), keepClosest,
                          &closest);
    }
    record({true, true, shape, from, angle, direction, distance, closest.hit, 0});
    return closest.hit;
}

// Every shape the cast touches, each entity once at its nearest; returning 1
// lets the cast go its whole length.
struct AllHits
{
    QueryFilter filter;
    float distance;
    Vector<RayHit2D> hits{tag};
};

float keepAll(b2ShapeId shape, b2Pos point, b2Vec2 normal, float fraction, void* context)
{
    AllHits& all = *static_cast<AllHits*>(context);
    if (!admits(shape, all.filter))
    {
        return -1;
    }
    const RayHit2D hit{true, entityOf(shape), fromB2(point), fromB2(normal), fraction * all.distance};
    for (RayHit2D& seen : all.hits)
    {
        if (seen.entity.value == hit.entity.value)
        {
            if (hit.distance < seen.distance)
            {
                seen = hit;
            }
            return 1;
        }
    }
    all.hits.pushBack(hit);
    return 1;
}

uint32_t shapeCastAllBox2D(ecs::World& world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
                           float distance, QueryFilter filter, std::span<RayHit2D> into)
{
    const PhysicsWorld* physics = find(world);
    AllHits all{filter, distance};
    b2ShapeProxy proxy;
    if (physics != nullptr && distance >= 0 && makeProxy(shape, angle, proxy))
    {
        b2World_CastShape(physics->id, toB2(from), &proxy, toB2(direction * distance), queryFilter(filter), keepAll,
                          &all);
    }
    std::stable_sort(all.hits.begin(), all.hits.end(),
                     [](const RayHit2D& a, const RayHit2D& b) { return a.distance < b.distance; });
    std::copy_n(all.hits.begin(), std::min(all.hits.size(), into.size()), into.begin());
    const auto count = static_cast<uint32_t>(all.hits.size());
    record({true, true, shape, from, angle, direction, distance, count > 0 ? all.hits[0] : RayHit2D{}, count});
    return count;
}

uint32_t overlapBox2D(ecs::World& world, QueryShape2D shape, Vector2 at, float angle, QueryFilter filter,
                      std::span<ecs::Entity> into)
{
    const PhysicsWorld* physics = find(world);
    Overlaps overlaps{filter, into};
    b2ShapeProxy proxy;
    if (physics != nullptr && makeProxy(shape, angle, proxy))
    {
        b2World_OverlapShape(physics->id, toB2(at), &proxy, queryFilter(filter), gather, &overlaps);
    }
    const auto count = static_cast<uint32_t>(overlaps.found.size());
    record({false, true, shape, at, angle, {}, 0, {}, count});
    return count;
}

uint32_t contactsBox2D(ecs::World& world, std::span<ContactEvent2D> into)
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

// --- Character moves (ADR 0094, as 3D's, ADR 0091) -------------------------------

constexpr int maxCharacterPlanes = 16;
constexpr int moveIterations = 5;
constexpr float groundSkin = 0.02f;

struct Mover
{
    ecs::Entity self;
    float walkable; // the cosine of the slope limit
    int count = 0;
    b2CollisionPlane planes[maxCharacterPlanes];
    b2ShapeId shapes[maxCharacterPlanes];
    Vector2 points[maxCharacterPlanes]; // relative to the origin
};

bool gatherPlanes(b2ShapeId shape, const b2PlaneResult* result, void* context)
{
    Mover& mover = *static_cast<Mover*>(context);
    if (b2Shape_IsSensor(shape) || entityOf(shape).value == mover.self.value || mover.count == maxCharacterPlanes)
    {
        return true;
    }
    b2Plane plane = result->plane;
    // Too steep to walk up: a wall, pushing only sideways.
    if (plane.normal.y > 0 && plane.normal.y < mover.walkable && std::fabs(plane.normal.x) > 1e-4f)
    {
        plane.normal = {plane.normal.x > 0 ? 1.0f : -1.0f, 0};
    }
    mover.planes[mover.count] = {plane, 3.4e38f, 0, true};
    mover.shapes[mover.count] = shape;
    mover.points[mover.count] = fromB2(result->point);
    ++mover.count;
    return true;
}

Character& characterOf(PhysicsWorld& physics, ecs::Entity entity)
{
    Character* character = physics.characters.find(entity.value);
    if (character == nullptr)
    {
        physics.characters.insertOrAssign(entity.value, Character{});
        character = physics.characters.find(entity.value);
    }
    return *character;
}

// Feet up onto higher ground and down to lower ground eased over the step
// smoothing, as in 3D; landing from a fall isn't.
float settle(float feet, float ground, const CharacterController2D& controller, bool wasGrounded, float seconds)
{
    if (!wasGrounded || controller.stepSmoothing <= 0 || seconds <= 0)
    {
        return ground;
    }
    return feet + (ground - feet) * (1 - std::exp(-seconds * 2.302585f / controller.stepSmoothing));
}

float cross2(Vector2 a, Vector2 b)
{
    return a.x * b.y - a.y * b.x;
}

// Dynamic bodies walked into get an impulse along each plane to stop closing on them.
void pushBodies(const Mover& mover, Vector2 origin, Vector2 velocity)
{
    for (int i = 0; i < mover.count; ++i)
    {
        const b2BodyId body = b2Shape_GetBody(mover.shapes[i]);
        if (b2Body_GetType(body) != b2_dynamicBody)
        {
            continue;
        }
        const Vector2 point = origin + mover.points[i];
        const Vector2 normal = -fromB2(mover.planes[i].plane.normal);
        const Vector2 arm = point - fromB2(b2Body_GetWorldCenter(body));
        const float mass = b2Body_GetMass(body), inertia = b2Body_GetRotationalInertia(body);
        const float armNormal = cross2(arm, normal);
        const float k = (mass > 0 ? 1 / mass : 0) + (inertia > 0 ? armNormal * armNormal / inertia : 0);
        if (k <= 0)
        {
            continue;
        }
        const float spin = b2Body_GetAngularVelocity(body);
        const Vector2 bodyVelocity = fromB2(b2Body_GetLinearVelocity(body)) + Vector2{-spin * arm.y, spin * arm.x};
        const float closing = dot(bodyVelocity - velocity, normal);
        const float impulse = std::max(-closing / k, 0.0f);
        if (impulse > 0)
        {
            b2Body_ApplyLinearImpulse(body, toB2(normal * impulse), toB2(point), true);
        }
    }
}

CharacterMove2D moveCharacterBox2D(ecs::World& world, ecs::Entity entity, Vector2 displacement, float seconds)
{
    scenes::Transform2D* transform = ecs::get<scenes::Transform2D>(world, entity);
    if (transform == nullptr)
    {
        return {};
    }
    const CharacterController2D* controller = ecs::read<CharacterController2D>(world, entity);
    PhysicsWorld* physics = find(world);
    if (controller == nullptr || physics == nullptr)
    {
        transform->position = transform->position + displacement;
        return {transform->position, false, {0, 1}, 0};
    }
    Character& character = characterOf(*physics, entity);
    const float time = std::max(seconds, 0.0f);
    if (!character.grounded || character.fallSpeed > 0)
    {
        character.fallSpeed += gravity.y * controller->gravityScale * time;
    }
    else
    {
        character.fallSpeed = 0;
    }
    Mover mover{entity, std::cos(std::clamp(controller->slopeLimit, 0.0f, 1.5707963f))};
    const b2Capsule capsule = characterCapsule(*controller, false);
    b2QueryFilter filter = b2DefaultQueryFilter();
    filter.categoryBits = moverBit;
    filter.maskBits = controller->mask;

    const Vector2 start = transform->position;
    const Vector2 target = start + displacement + Vector2{0, character.fallSpeed * time};
    Vector2 at = start;
    for (int iteration = 0; iteration < moveIterations; ++iteration)
    {
        mover.count = 0;
        b2World_CollideMover(physics->id, toB2(at), &capsule, filter, gatherPlanes, &mover);
        const b2PlaneSolverResult solved = b2SolvePlanes(toB2(target - at), mover.planes, mover.count);
        const float fraction = b2World_CastMover(physics->id, toB2(at), &capsule, solved.delta, filter);
        const Vector2 moved = fromB2(solved.delta) * fraction;
        at = at + moved;
        if (length(moved) < 0.001f)
        {
            break;
        }
    }
    if (character.fallSpeed > 0 && at.y - start.y < character.fallSpeed * time * 0.5f)
    {
        character.fallSpeed = 0; // a head against a ceiling
    }

    // The ground: a circle a little smaller than the capsule cast down from
    // its bottom cap; over an edge, a ray straight down from the middle.
    const bool wasGrounded = character.grounded;
    character.grounded = false;
    character.groundNormal = {0, 1};
    if (character.fallSpeed <= 0)
    {
        const float radius = capsule.radius * 0.95f;
        const float ride = capsule.center1.y - radius;
        const float reach = ride + groundSkin + (wasGrounded ? controller->stepHeight : 0.0f);
        const physics::QueryFilter own{controller->mask, entity, false};
        b2QueryFilter probeFilter = b2DefaultQueryFilter();
        probeFilter.categoryBits = UINT64_MAX;
        probeFilter.maskBits = controller->mask;
        const b2Vec2 center{0, 0};
        const b2ShapeProxy probe = b2MakeProxy(&center, 1, radius);
        Closest closest{own, reach};
        const Vector2 from = at + Vector2{0, capsule.center1.y};
        b2World_CastShape(physics->id, toB2(from), &probe, toB2(Vector2{0, -reach}), probeFilter, keepClosest,
                          &closest);
        if (closest.hit.hit && closest.hit.normal.y >= mover.walkable)
        {
            at.y = settle(at.y, from.y - closest.hit.distance - radius, *controller, wasGrounded, time);
            character.grounded = true;
            character.groundNormal = closest.hit.normal;
        }
        else
        {
            Closest below{own, ride + reach, false};
            const Vector2 middle = at + Vector2{0, ride};
            b2World_CastRay(physics->id, toB2(middle), toB2(Vector2{0, -(ride + reach)}), probeFilter, keepClosest,
                            &below);
            if (below.hit.hit && below.hit.normal.y >= mover.walkable)
            {
                at.y = settle(at.y, middle.y - below.hit.distance, *controller, wasGrounded, time);
                character.grounded = true;
                character.groundNormal = below.hit.normal;
            }
        }
        if (character.grounded)
        {
            character.fallSpeed = 0;
        }
    }
    if (time > 0)
    {
        pushBodies(mover, at, (at - start) * (1 / time));
    }
    transform->position = at;
    return {at, character.grounded, character.groundNormal, character.fallSpeed};
}

void launchCharacterBox2D(ecs::World& world, ecs::Entity entity, float upwardSpeed)
{
    if (PhysicsWorld* physics = find(world))
    {
        Character& character = characterOf(*physics, entity);
        character.fallSpeed = upwardSpeed;
        character.grounded = false;
    }
}

constexpr Physics2DService box2dPhysics{raycastBox2D,       shapeCastBox2D,       overlapBox2D,     contactsBox2D,
                                        moveCharacterBox2D, launchCharacterBox2D, shapeCastAllBox2D};

Physics2DService installed = box2dPhysics;

// --- Outlines ------------------------------------------------------------------

Vector3 at(const Matrix4x4& toWorld, Vector2 point)
{
    return transformPoint(Vector3{point.x, point.y, 0}, toWorld);
}

void polyline(std::span<const Vector2> points, bool closed, const Matrix4x4& toWorld, Vector4 color)
{
    for (size_t i = 0; i + 1 < points.size() + (closed ? 1 : 0); ++i)
    {
        debug::drawLine(at(toWorld, points[i]), at(toWorld, points[(i + 1) % points.size()]), color);
    }
}

void arc(Vector2 center, float radius, float from, float to, const Matrix4x4& toWorld, Vector4 color)
{
    constexpr int segments = 16;
    Vector2 points[segments + 1];
    for (int i = 0; i <= segments; ++i)
    {
        const float angle = from + (to - from) * static_cast<float>(i) / segments;
        points[i] = center + Vector2{std::cos(angle), std::sin(angle)} * radius;
    }
    polyline(points, false, toWorld, color);
}

void outline(const Collider2D& collider, const Matrix4x4& toWorld, Vector4 color)
{
    const Vector2 o = collider.offset;
    switch (collider.shape)
    {
    case ShapeKind2D::Box:
    {
        const Vector2 h = collider.size * 0.5f;
        const Vector2 corners[4] = {o + Vector2{-h.x, -h.y}, o + Vector2{h.x, -h.y}, o + Vector2{h.x, h.y},
                                    o + Vector2{-h.x, h.y}};
        polyline(corners, true, toWorld, color);
        break;
    }
    case ShapeKind2D::Circle:
        arc(o, collider.size.x, 0, 6.2831853f, toWorld, color);
        debug::drawLine(at(toWorld, o), at(toWorld, o + Vector2{collider.size.x, 0}), color); // its turn
        break;
    case ShapeKind2D::Capsule:
    {
        const float r = collider.size.x, half = collider.size.y * 0.5f;
        arc(o + Vector2{0, half}, r, 0, 3.1415927f, toWorld, color);
        arc(o - Vector2{0, half}, r, 3.1415927f, 6.2831853f, toWorld, color);
        debug::drawLine(at(toWorld, o + Vector2{r, -half}), at(toWorld, o + Vector2{r, half}), color);
        debug::drawLine(at(toWorld, o + Vector2{-r, -half}), at(toWorld, o + Vector2{-r, half}), color);
        break;
    }
    case ShapeKind2D::Polygon:
    {
        // The hull Box2D makes of the points.
        b2Vec2 points[maxPolygonPoints];
        const int32_t count = std::clamp(collider.pointCount, 0, maxPolygonPoints);
        for (int32_t i = 0; i < count; ++i)
        {
            points[i] = toB2(collider.points[i] + o);
        }
        const b2Hull hull = b2ComputeHull(points, count);
        Vector2 corners[maxPolygonPoints];
        for (int i = 0; i < hull.count; ++i)
        {
            corners[i] = fromB2(hull.points[i]);
        }
        polyline(std::span(corners, static_cast<size_t>(hull.count)), true, toWorld, color);
        break;
    }
    case ShapeKind2D::Chain:
    case ShapeKind2D::Tilemap:
        if (const ChainData* data = chainOf(collider.chain); data != nullptr && data->points.size() >= 2)
        {
            // A chain's points, or each of a tilemap's outlines.
            const uint32_t whole = static_cast<uint32_t>(data->points.size());
            const std::span<const uint32_t> counts =
                data->loops.empty() ? std::span<const uint32_t>(&whole, 1) : data->loops.span();
            uint32_t first = 0;
            for (const uint32_t count : counts)
            {
                Vector<Vector2> points(tag);
                points.resize(count);
                for (size_t i = 0; i < points.size(); ++i)
                {
                    points[i] = data->points[first + i] + o;
                }
                first += count;
                polyline(points.span(), data->loop, toWorld, color);
                // A tick at each segment's middle on its solid side (the right).
                const size_t segments = data->loop ? points.size() : points.size() - 1;
                for (size_t i = 0; i < segments; ++i)
                {
                    const Vector2 a = points[i], b = points[(i + 1) % points.size()];
                    const Vector2 along = b - a;
                    const float length = std::sqrt(along.x * along.x + along.y * along.y);
                    if (length > 0)
                    {
                        const Vector2 middle = (a + b) * 0.5f;
                        const float tick = std::min(0.15f, length * 0.25f) / length;
                        debug::drawLine(at(toWorld, middle), at(toWorld, middle + Vector2{along.y, -along.x} * tick),
                                        color);
                    }
                }
            }
        }
        break;
    }
}

} // namespace

void installPhysics2D(const Physics2DService& service)
{
    installed = service;
}

const Physics2DService& physics2DService()
{
    return installed;
}

RayHit2D raycast(ecs::World& world, Vector2 from, Vector2 direction, float distance, QueryFilter filter)
{
    return installed.raycast(world, from, direction, distance, filter);
}

RayHit2D shapeCast(ecs::World& world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction, float distance,
                   QueryFilter filter)
{
    return installed.shapeCast(world, shape, from, angle, direction, distance, filter);
}

uint32_t shapeCastAll(ecs::World& world, QueryShape2D shape, Vector2 from, float angle, Vector2 direction,
                      float distance, QueryFilter filter, std::span<RayHit2D> into)
{
    return installed.shapeCastAll != nullptr
               ? installed.shapeCastAll(world, shape, from, angle, direction, distance, filter, into)
               : 0;
}

uint32_t overlap(ecs::World& world, QueryShape2D shape, Vector2 at, float angle, QueryFilter filter,
                 std::span<ecs::Entity> into)
{
    return installed.overlap(world, shape, at, angle, filter, into);
}

uint32_t contacts(ecs::World& world, std::span<ContactEvent2D> into)
{
    return installed.contacts(world, into);
}

CharacterMove2D moveCharacter2D(ecs::World& world, ecs::Entity character, Vector2 displacement, float seconds)
{
    return installed.moveCharacter(world, character, displacement, seconds);
}

void launchCharacter2D(ecs::World& world, ecs::Entity character, float upwardSpeed)
{
    if (installed.launchCharacter != nullptr)
    {
        installed.launchCharacter(world, character, upwardSpeed);
    }
}

BodyPose2D bodyPose(const ecs::World& world, ecs::Entity entity)
{
    const PhysicsWorld* physics = find(world);
    const Body* body = physics != nullptr ? physics->bodies.find(entity.value) : nullptr;
    if (body == nullptr || B2_IS_NULL(body->body))
    {
        return {false, {}, 0};
    }
    return {true, fromB2(b2Body_GetPosition(body->body)), b2Rot_GetAngle(b2Body_GetRotation(body->body))};
}

namespace {

// The load starter (ADR 0099): every Chain collider's chain, and every
// Tilemap collider's map, started; a collider waits while its points load.
uint32_t startChainLoads(ecs::World& world)
{
    const ecs::ComponentId colliders[] = {ecs::componentIdOf<Collider2D>};
    ecs::Query* query = ecs::createQuery(world, {.required = colliders});
    struct Counting
    {
        ecs::World* world;
        uint32_t waiting;
    } counting{&world, 0};
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            auto& counting = *static_cast<Counting*>(data);
            const std::span<const Collider2D> found = ecs::readColumn<Collider2D>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < found.size(); ++i)
            {
                counting.waiting += chainLoading(resolvedCollider(*counting.world, entities[i], found[i])) ? 1 : 0;
            }
        },
        &counting);
    ecs::destroyQuery(query);
    return counting.waiting;
}

} // namespace

void registerPhysics2D()
{
    assets::registerLoadStarter(startChainLoads);
    ecs::registerComponent<RigidBody2D>("Crucible.Physics2D.RigidBody2D");
    ecs::registerComponent<Collider2D>("Crucible.Physics2D.Collider2D");
    ecs::registerComponent<Joint2D>("Crucible.Physics2D.Joint2D");
    ecs::registerComponent<CharacterController2D>("Crucible.Physics2D.CharacterController2D");
    loop::registerDefaultSystem({.name = syncSystemName, .phase = "PrePhysics", .function = sync, .gameplay = true});
    loop::registerDefaultSystem({.name = stepSystemName, .phase = "Physics", .function = stepWorld, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = writeBackSystemName, .phase = "PostPhysics", .function = writeBack, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = interpolateSystemName, .phase = "Update", .function = interpolate, .gameplay = true});
    physics::registerHitboxes2D();
}

void releasePhysics2D(const ecs::World& world)
{
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        if (worlds[i]->world == &world)
        {
            b2DestroyWorld(worlds[i]->id);
            destroy(tag, worlds[i]);
            worlds.removeSwap(i);
            physics::forgetHitboxes(world);
            if (worlds.empty())
            {
                freeChains(); // nothing is made from them now
            }
            return;
        }
    }
}

bool reloadChainAsset(assets::AssetId chain)
{
    for (ChainData* data : chainData)
    {
        if (data->id == chain)
        {
            if (!readChain(*data))
            {
                log::warning("a Chain collider's chain can't be read (reimport it)");
            }
            return true;
        }
    }
    return false;
}

void drawColliders2D(const ecs::World& world, Vector4 color)
{
    struct Gather
    {
        const ecs::World* world;
        Vector4 color;
    } gather{&world, color};
    const ecs::ComponentId required[] = {ecs::componentIdOf<Collider2D>, ecs::componentIdOf<scenes::Transform2D>};
    ecs::Query* query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const Collider2D> colliders = ecs::readColumn<Collider2D>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < colliders.size(); ++i)
            {
                outline(resolvedCollider(*gather.world, entities[i], colliders[i]),
                        scenes::computeWorldTransform(*gather.world, entities[i]), gather.color);
            }
        },
        &gather);
    ecs::destroyQuery(query);
    // Characters' capsules, from their feet up.
    const ecs::ComponentId controlled[] = {ecs::componentIdOf<CharacterController2D>,
                                           ecs::componentIdOf<scenes::Transform2D>};
    query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = controlled});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const CharacterController2D> controllers = ecs::readColumn<CharacterController2D>(view);
            const std::span<const scenes::Transform2D> transforms = ecs::readColumn<scenes::Transform2D>(view);
            for (size_t i = 0; i < controllers.size(); ++i)
            {
                const b2Capsule capsule = characterCapsule(controllers[i], true);
                Collider2D shape{};
                shape.shape = ShapeKind2D::Capsule;
                shape.size = {capsule.radius, capsule.center2.y - capsule.center1.y};
                shape.offset = {0, (capsule.center1.y + capsule.center2.y) * 0.5f};
                Matrix4x4 placed = Matrix4x4::identity();
                placed.rows[3] = {transforms[i].position.x, transforms[i].position.y, transforms[i].depth, 1};
                outline(shape, placed, gather.color);
            }
        },
        &gather);
    ecs::destroyQuery(query);
    // Joints: each anchor, the line between, a slider's axis, a hinge's limits.
    const ecs::ComponentId jointed[] = {ecs::componentIdOf<Joint2D>, ecs::componentIdOf<scenes::Transform2D>};
    query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = jointed});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const Joint2D> joints = ecs::readColumn<Joint2D>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < joints.size(); ++i)
            {
                const Joint2D& joint = joints[i];
                const Matrix4x4 own = scenes::computeWorldTransform(*gather.world, entities[i]);
                const Vector3 a = transformPoint(Vector3{joint.anchor.x, joint.anchor.y, 0}, own);
                Vector3 b = a;
                if (joint.other.value != 0 && ecs::read<scenes::Transform2D>(*gather.world, joint.other) != nullptr)
                {
                    b = transformPoint(Vector3{joint.otherAnchor.x, joint.otherAnchor.y, 0},
                                       scenes::computeWorldTransform(*gather.world, joint.other));
                }
                else if (const PhysicsWorld* physics = find(*gather.world))
                {
                    if (const JointState* state = physics->joints.find(entities[i].value);
                        state != nullptr && state->toWorld)
                    {
                        b = {state->worldAnchor.x, state->worldAnchor.y, a.z};
                    }
                }
                for (const Vector3 end : {a, b})
                {
                    debug::drawLine(end - Vector3{0.06f, 0, 0}, end + Vector3{0.06f, 0, 0}, gather.color);
                    debug::drawLine(end - Vector3{0, 0.06f, 0}, end + Vector3{0, 0.06f, 0}, gather.color);
                }
                debug::drawLine(a, b, gather.color);
                if (joint.kind == JointKind2D::Slider && length(joint.axis) > 1e-6f)
                {
                    Vector3 axis =
                        transformPoint(Vector3{joint.axis.x, joint.axis.y, 0}, own) - transformPoint({}, own);
                    axis = axis * (1 / length(axis));
                    const float from = joint.lowerLimit < joint.upperLimit ? joint.lowerLimit : -0.5f;
                    const float to = joint.lowerLimit < joint.upperLimit ? joint.upperLimit : 0.5f;
                    debug::drawLine(a + axis * from, a + axis * to, gather.color);
                }
                if (joint.kind == JointKind2D::Hinge && joint.lowerLimit < joint.upperLimit)
                {
                    const float base = std::atan2(own.rows[0].y, own.rows[0].x);
                    Vector3 last{};
                    for (int s = 0; s <= 16; ++s)
                    {
                        const float angle = base + joint.lowerLimit + (joint.upperLimit - joint.lowerLimit) * s / 16.0f;
                        const Vector3 point = a + Vector3{std::cos(angle), std::sin(angle), 0} * 0.3f;
                        if (s == 0 || s == 16)
                        {
                            debug::drawLine(a, point, gather.color);
                        }
                        if (s > 0)
                        {
                            debug::drawLine(last, point, gather.color);
                        }
                        last = point;
                    }
                }
            }
        },
        &gather);
    ecs::destroyQuery(query);
    // Hitboxes (ADR 0096): dimmer, and brighter while active.
    const ecs::ComponentId striking[] = {ecs::componentIdOf<Hitbox2D>};
    query = ecs::createQuery(const_cast<ecs::World&>(world), {.required = striking});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const Gather& gather = *static_cast<const Gather*>(data);
            const std::span<const Hitbox2D> hitboxes = ecs::readColumn<Hitbox2D>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            const Vector4 c = gather.color;
            for (size_t i = 0; i < hitboxes.size(); ++i)
            {
                if (hitboxes[i].shape != ShapeKind2D::Box && hitboxes[i].shape != ShapeKind2D::Circle &&
                    hitboxes[i].shape != ShapeKind2D::Capsule)
                {
                    continue;
                }
                Collider2D shape{};
                shape.shape = hitboxes[i].shape;
                shape.size = hitboxes[i].size;
                const Vector4 shown =
                    physics::hitboxActive(*gather.world, entities[i])
                        ? Vector4{c.x + (1 - c.x) * 0.6f, c.y + (1 - c.y) * 0.6f, c.z + (1 - c.z) * 0.6f, 1}
                        : Vector4{c.x * 0.6f, c.y * 0.6f, c.z * 0.6f, c.w};
                outline(shape, scenes::computeWorldTransform(*gather.world, entities[i]), shown);
            }
        },
        &gather);
    ecs::destroyQuery(query);
}

void recordQueries2D(bool on)
{
    recordingQueries = on;
    if (!on)
    {
        recordedQueries.clear();
    }
}

void drawQueries2D(Vector4 missed, Vector4 hit)
{
    const Matrix4x4 plane = Matrix4x4::identity();
    for (const QueryRecord& query : recordedQueries)
    {
        const bool struck = query.cast ? query.hit.hit : query.found > 0;
        const Vector4 color = struck ? hit : missed;
        const Vector2 end = query.from + query.direction * (struck && query.cast ? query.hit.distance : query.distance);
        if (query.cast)
        {
            debug::drawLine(at(plane, query.from), at(plane, end), color);
            if (struck)
            {
                const Vector2 p = query.hit.point;
                debug::drawLine(at(plane, p), at(plane, p + query.hit.normal * 0.25f), color);
                debug::drawLine(at(plane, p - Vector2{0.05f, 0}), at(plane, p + Vector2{0.05f, 0}), color);
                debug::drawLine(at(plane, p - Vector2{0, 0.05f}), at(plane, p + Vector2{0, 0.05f}), color);
            }
        }
        if (query.hasShape)
        {
            Collider2D shape{};
            shape.shape = query.shape.kind;
            shape.size = query.shape.size;
            Matrix4x4 placed = Matrix4x4::rotationZ(query.angle);
            placed.rows[3] = {query.from.x, query.from.y, 0, 1};
            outline(shape, placed, color);
            if (query.cast)
            {
                placed.rows[3] = {end.x, end.y, 0, 1};
                outline(shape, placed, color);
            }
        }
    }
    recordedQueries.clear();
}

} // namespace crucible::physics2d
