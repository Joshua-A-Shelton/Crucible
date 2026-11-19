#pragma once

// Frame animation (ADR 0107) as the pose runtime calls it: sprite animators
// advance inside "Animation.Update", so their events join the same list as
// 3D characters', and the state machine calls (animation::setFloat, ...)
// reach an entity with a SpriteAnimator when it has no pose.

#include <crucible/Animation.h>
#include <crucible/AssetSource.h>
#include <crucible/Ecs.h>
#include <crucible/Result.h>
#include <crucible/StateMachineFile.h>
#include <crucible/Vector.h>

#include <cstdint>
#include <string_view>

namespace crucible::render2d {

// Moves every SpriteAnimator of the world on and sets its Sprite; the
// events it passed are added to `events`, in no particular order.
void advanceSpriteAnimators(ecs::World& world, float delta, uint64_t frameStart,
                            Vector<animation::AnimationEvent>& events);

Result<void> setSpriteParameter(ecs::World& world, ecs::Entity entity, std::string_view name,
                                assets::ParameterType type, float value);
// -1 when the entity has no SpriteAnimator playing a state machine.
int32_t spriteCurrentState(const ecs::World& world, ecs::Entity entity);
int32_t spriteFindState(ecs::World& world, ecs::Entity entity, std::string_view name);

// Forgets a sprite clip or state machine read, so it's read again; whether
// one was.
bool reloadSpriteAnimationAsset(const assets::AssetId& asset);
// Drops the world's players, as its world goes.
void releaseSpriteAnimators(const ecs::World& world);

// Registers SpriteAnimator and its load starter (registerSprite calls it).
void registerSpriteAnimator();

} // namespace crucible::render2d
