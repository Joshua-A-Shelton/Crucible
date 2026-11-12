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

#include <crucible/DebugLines.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Physics2D.h>
#include <crucible/Systems.h>
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

Pose2D poseOf(const Matrix4x4& m)
{
    Pose2D pose;
    pose.position = {m.rows[3].x, m.rows[3].y};
    pose.angle = std::atan2(m.rows[0].y, m.rows[0].x);
    pose.scale = {std::sqrt(m.rows[0].x * m.rows[0].x + m.rows[0].y * m.rows[0].y),
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
        a.restitution != b.restitution || a.isTrigger != b.isTrigger || a.pointCount != b.pointCount)
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

struct PhysicsWorld
{
    const ecs::World* world = nullptr;
    b2WorldId id = b2_nullWorldId;
    HashMap<uint64_t, Body> bodies{tag};
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

b2BodyType typeOf(BodyKind kind)
{
    return kind == BodyKind::Dynamic ? b2_dynamicBody : kind == BodyKind::Kinematic ? b2_kinematicBody : b2_staticBody;
}

Vector2 scaled(Vector2 v, Vector2 scale)
{
    return {v.x * scale.x, v.y * scale.y};
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
    body.collider = collider;
    body.scale = scale;
    body.layers = layers;
    body.mask = mask;
    b2ShapeDef def = b2DefaultShapeDef();
    def.userData = entityData(entity);
    def.material.friction = std::max(collider.friction, 0.0f);
    def.material.restitution = std::clamp(collider.restitution, 0.0f, 1.0f);
    def.filter = {uint64_t{layers} | (uint64_t{mask} << 32), UINT64_MAX, 0};
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
    const Collider2D& collider = *ecs::read<Collider2D>(world, entity);
    const RigidBody2D* rigid = ecs::read<RigidBody2D>(world, entity);
    const BodyKind kind = rigid != nullptr ? rigid->kind : BodyKind::Static;
    const uint32_t layers = rigid != nullptr ? rigid->layers : 1u;
    const uint32_t mask = rigid != nullptr ? rigid->mask : physics::allLayers;
    const Pose2D pose = worldPose(world, entity);
    const scenes::Transform2D local = *ecs::read<scenes::Transform2D>(world, entity);

    Body* body = physics.bodies.find(entity.value);
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
    if (!same(collider, body->collider) ||
        std::fabs(pose.scale.x - body->scale.x) + std::fabs(pose.scale.y - body->scale.y) > 1e-5f ||
        layers != body->layers || mask != body->mask || kindChanged)
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

constexpr Physics2DService box2dPhysics{raycastBox2D, shapeCastBox2D, overlapBox2D, contactsBox2D};

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

uint32_t overlap(ecs::World& world, QueryShape2D shape, Vector2 at, float angle, QueryFilter filter,
                 std::span<ecs::Entity> into)
{
    return installed.overlap(world, shape, at, angle, filter, into);
}

uint32_t contacts(ecs::World& world, std::span<ContactEvent2D> into)
{
    return installed.contacts(world, into);
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

void registerPhysics2D()
{
    ecs::registerComponent<RigidBody2D>("Crucible.Physics2D.RigidBody2D");
    ecs::registerComponent<Collider2D>("Crucible.Physics2D.Collider2D");
    loop::registerDefaultSystem({.name = syncSystemName, .phase = "PrePhysics", .function = sync, .gameplay = true});
    loop::registerDefaultSystem({.name = stepSystemName, .phase = "Physics", .function = stepWorld, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = writeBackSystemName, .phase = "PostPhysics", .function = writeBack, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = interpolateSystemName, .phase = "Update", .function = interpolate, .gameplay = true});
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
            return;
        }
    }
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
                outline(colliders[i], scenes::computeWorldTransform(*gather.world, entities[i]), gather.color);
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
