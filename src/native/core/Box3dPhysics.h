#pragma once

// The engine's physics (ADR 0085): Box3D behind the physics contract. Its
// four systems, and the service calls it answers so far (contacts; queries
// and character moves come in later steps).

#include <crucible/Physics.h>

#include <span>

namespace crucible::physics::box3d {

// Registers "Physics.Sync", "Physics.Step", "Physics.WriteBack", and
// "Physics.Interpolate", unless an implementation registered them first.
void registerSystems();

uint32_t contacts(ecs::World& world, std::span<ContactEvent> into);

} // namespace crucible::physics::box3d
