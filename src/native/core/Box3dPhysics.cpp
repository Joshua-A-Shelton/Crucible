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

#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include <box3d/box3d.h>

#include <algorithm>
#include <cmath>
#include <cstring>

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
           a.meshIndex == b.meshIndex;
}

// --- Worlds and bodies ------------------------------------------------------

struct Body
{
    b3BodyId body = b3_nullBodyId;
    b3ShapeId shape = b3_nullShapeId;
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

struct Task
{
    jobs::JobCounter counter;
};

struct PhysicsWorld
{
    const ecs::World* world = nullptr;
    b3WorldId id = b3_nullWorldId;
    HashMap<uint64_t, Body> bodies{tag};
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
    static bool allocatorSet = false;
    if (!allocatorSet)
    {
        b3SetAllocator(allocate, release);
        allocatorSet = true;
    }
    PhysicsWorld* made = create<PhysicsWorld>(tag);
    made->world = &world;
    b3WorldDef def = b3DefaultWorldDef();
    def.gravity = {0, -9.81f, 0};
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
    case ShapeKind::Convex:
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
        layers != body->layers || mask != body->mask)
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
        if (!B3_IS_NULL(body->body))
        {
            b3DestroyBody(body->body);
        }
        physics.bodies.erase(key);
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

void releasePhysics(const ecs::World& world)
{
    using namespace box3d;
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        if (worlds[i]->world == &world)
        {
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
    case ShapeKind::Convex:
        break;
    }
}

} // namespace

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
}

} // namespace crucible::physics
