// The physics contract's engine side (ADR 0075): the installed service and
// its default, Box3D (ADR 0085, Box3dPhysics.cpp; its queries ADR 0090, its
// characters ADR 0091).

#include "Box3dPhysics.h"

#include <crucible/Physics.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>

namespace crucible::physics {

namespace {

constexpr PhysicsService defaultPhysics{box3d::raycast,  box3d::shapeCast,     box3d::overlap,
                                        box3d::contacts, box3d::moveCharacter, box3d::launchCharacter};

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

void launchCharacter(ecs::World& world, ecs::Entity character, float upwardSpeed)
{
    if (installed.launchCharacter != nullptr)
    {
        installed.launchCharacter(world, character, upwardSpeed);
    }
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
