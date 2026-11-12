#pragma once

// The engine's physics (ADR 0085): Box3D behind the physics contract. Its
// four systems, and the service calls it answers so far (contacts, and the
// queries of ADR 0090, and the characters of ADR 0091).

#include <crucible/Physics.h>

#include <span>

namespace crucible::physics::box3d {

// Registers "Physics.Sync", "Physics.Step", "Physics.WriteBack", and
// "Physics.Interpolate", unless an implementation registered them first.
void registerSystems();

uint32_t contacts(ecs::World& world, std::span<ContactEvent> into);
RayHit raycast(ecs::World& world, Vector3 from, Vector3 direction, float distance, QueryFilter filter);
RayHit shapeCast(ecs::World& world, QueryShape shape, Vector3 from, Quaternion rotation, Vector3 direction,
                 float distance, QueryFilter filter);
uint32_t overlap(ecs::World& world, QueryShape shape, Vector3 at, Quaternion rotation, QueryFilter filter,
                 std::span<ecs::Entity> into);
CharacterMove moveCharacter(ecs::World& world, ecs::Entity character, Vector3 displacement, float seconds);
void launchCharacter(ecs::World& world, ecs::Entity character, float upwardSpeed);

} // namespace crucible::physics::box3d
