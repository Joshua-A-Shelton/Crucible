// The physics contract's engine side (ADR 0075): the installed service and
// the null physics that stands in until an implementation replaces it.

#include <crucible/Physics.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>

namespace crucible::physics {

namespace {

// Nothing moves or collides; queries find nothing.
void nothing(const loop::SystemContext&, void*) {}

RayHit noHit(ecs::World&, Vector3, Vector3, float, uint32_t)
{
    return {};
}

RayHit noCastHit(ecs::World&, QueryShape, Vector3, Quaternion, Vector3, float, uint32_t)
{
    return {};
}

uint32_t noOverlap(ecs::World&, QueryShape, Vector3, Quaternion, uint32_t, std::span<ecs::Entity>)
{
    return 0;
}

uint32_t noContacts(ecs::World&, std::span<ContactEvent>)
{
    return 0;
}

// Straight through everything, never on the ground.
CharacterMove moveFreely(ecs::World& world, ecs::Entity character, Vector3 displacement, float)
{
    scenes::LocalTransform* transform = ecs::get<scenes::LocalTransform>(world, character);
    if (transform == nullptr)
    {
        return {};
    }
    transform->position = transform->position + displacement;
    return {transform->position, false, {0, 1, 0}};
}

constexpr PhysicsService nullPhysics{noHit, noCastHit, noOverlap, noContacts, moveFreely};

PhysicsService installed = nullPhysics;

} // namespace

void installPhysics(const PhysicsService& service)
{
    installed = service;
}

const PhysicsService& physicsService()
{
    return installed;
}

RayHit raycast(ecs::World& world, Vector3 from, Vector3 direction, float distance, uint32_t layers)
{
    return installed.raycast(world, from, direction, distance, layers);
}

RayHit shapeCast(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
                 float distance, uint32_t layers)
{
    return installed.shapeCast(world, shape, from, rotation, direction, distance, layers);
}

uint32_t overlap(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation, uint32_t layers,
                 std::span<ecs::Entity> into)
{
    return installed.overlap(world, shape, at, rotation, layers, into);
}

uint32_t contacts(ecs::World& world, std::span<ContactEvent> into)
{
    return installed.contacts(world, into);
}

CharacterMove moveCharacter(ecs::World& world, ecs::Entity character, Vector3 displacement, float seconds)
{
    return installed.moveCharacter(world, character, displacement, seconds);
}

void registerPhysics()
{
    ecs::registerComponent<RigidBody>("Crucible.Physics.RigidBody");
    ecs::registerComponent<Collider>("Crucible.Physics.Collider");
    ecs::registerComponent<Joint>("Crucible.Physics.Joint");
    ecs::registerComponent<CharacterController>("Crucible.Physics.CharacterController");
    loop::registerDefaultSystem({.name = syncSystemName, .phase = "PrePhysics", .function = nothing, .gameplay = true});
    loop::registerDefaultSystem({.name = stepSystemName, .phase = "Physics", .function = nothing, .gameplay = true});
    loop::registerDefaultSystem(
        {.name = writeBackSystemName, .phase = "PostPhysics", .function = nothing, .gameplay = true});
}

} // namespace crucible::physics
