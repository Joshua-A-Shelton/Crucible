// The physics contract's engine side (ADR 0075): the installed service and
// its default, Box3D (ADR 0085, Box3dPhysics.cpp; its queries ADR 0090),
// whose character moves answer as the null physics did until they're built.

#include "Box3dPhysics.h"

#include <crucible/Physics.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>

namespace crucible::physics {

namespace {

// Straight through everything, never on the ground (until M9 step 4).
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

constexpr PhysicsService defaultPhysics{box3d::raycast, box3d::shapeCast, box3d::overlap, box3d::contacts, moveFreely};

PhysicsService installed = defaultPhysics;

} // namespace

void installPhysics(const PhysicsService& service)
{
    installed = service;
}

const PhysicsService& physicsService()
{
    return installed;
}

QueryFilter defaultQueryFilter()
{
    return {allLayers, {}, false};
}

RayHit raycast(ecs::World& world, Vector3 from, Vector3 direction, float distance, QueryFilter filter)
{
    return installed.raycast(world, from, direction, distance, filter);
}

RayHit shapeCast(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
                 float distance, QueryFilter filter)
{
    return installed.shapeCast(world, shape, from, rotation, direction, distance, filter);
}

uint32_t overlap(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation, QueryFilter filter,
                 std::span<ecs::Entity> into)
{
    return installed.overlap(world, shape, at, rotation, filter, into);
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
    box3d::registerSystems();
}

} // namespace crucible::physics
