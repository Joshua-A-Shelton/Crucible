#pragma once

// Swept hitboxes (ADR 0096), 3D and 2D: what the physics backends ask of
// them (Hitboxes.cpp).

#include <crucible/Ecs.h>

namespace crucible::physics {

// Registers Hitbox and "Physics.Hitboxes", Hitbox2D and "Physics2D.Hitboxes".
void registerHitboxes();
void registerHitboxes2D();

// Whether the entity's hitbox (3D or 2D) was active at the end of its last
// sweep, for drawing; false while editing.
bool hitboxActive(const ecs::World& world, ecs::Entity entity);

// Drops what's kept for the world (its hitboxes' last places and hits).
void forgetHitboxes(const ecs::World& world);

} // namespace crucible::physics
