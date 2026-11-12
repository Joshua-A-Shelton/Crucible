#pragma once

// Transforms (ADR 0050): an entity's place relative to its parent, and the
// world matrix the engine derives from it every frame.
//
// Shared with C# like the demo components: declared here, mirrored into
// Crucible.Scenes by the binding generator, registered by both languages
// under the mirror's full name. LocalTransform is saved in scenes (the C#
// half of the partial struct adds [Serialized]); WorldTransform is derived
// and never saved.

#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>

namespace crucible::scenes {

// Relative to the parent, or to the world for a root (ADR 0024:
// left-handed, Y up, meters).
struct CRUCIBLE_EXPOSE LocalTransform
{
    Vector3 position;
    Quaternion rotation;
    Vector3 scale;
};

// A 2D entity's place relative to its parent (ADR 0093): in the XY plane, Y
// up, turned by `angle` radians about Z (+X toward +Y, counterclockwise as
// the default camera sees it), scaled in X and Y, at Z = `depth` (draw
// order and layering). It makes the same WorldTransform as LocalTransform,
// so whatever draws 3D draws 2D; an entity has one or the other
// (LocalTransform wins if both), and either kind can parent either.
struct CRUCIBLE_EXPOSE Transform2D
{
    Vector2 position;
    float angle;
    Vector2 scale;
    float depth;
};

// Object to world, for row vectors (worldPoint = point * matrix): the
// entity's scale, rotation, and translation, then its parent's world matrix.
struct CRUCIBLE_EXPOSE WorldTransform
{
    Matrix4x4 matrix;
};

// The entity's world matrix as it is now, from its own and its ancestors'
// local transforms (cost: its depth), without storing it: WorldTransform
// stays as last propagated. Identity without a LocalTransform. Read-only, so
// callable from many threads while nothing writes transforms or the
// hierarchy. For many entities, updateWorldTransforms is cheaper.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL Matrix4x4 computeWorldTransform(const ecs::World& world, ecs::Entity entity);

// Recomputes every world matrix now, for code that can't wait for
// "crucible.TransformPropagation" (gameplay reading a parent it just moved).
// An entity takes part when it has both components; one under a parent
// without them is placed as a root. Not during iteration.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void updateWorldTransforms(ecs::World& world);

// Registers LocalTransform, Transform2D, and WorldTransform. Registering again is harmless.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerTransforms();

// The system "crucible.TransformPropagation", as the function address C#'s
// default schedule calls in RenderPrepare (ADR 0050): it recomputes the
// world matrix of every entity with both components, every run (ADR 0056);
// and it gives entities that have a LocalTransform but no
// WorldTransform one (applied at RenderPrepare's sync point, so it's in place
// for RenderSubmit). Not in the C++ system registry, which every schedule
// includes: custom schedules add it themselves, or not.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL uint64_t transformPropagationFunction();

} // namespace crucible::scenes
