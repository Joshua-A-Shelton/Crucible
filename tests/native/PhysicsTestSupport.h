#pragma once

// What the Box3D tests share (ADR 0085): a world with bodies, run tick by
// tick and frame by frame as the loop runs the physics systems.

#include "SubsystemTestSupport.h"

#include <crucible/DebugLines.h>
#include <crucible/Physics.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <cmath>
#include <vector>

namespace crucible::physics::testing {

constexpr double tickSeconds = 1.0 / 60;

struct Scene
{
    ecs::World* world = nullptr;
    uint64_t frame = 1;

    Scene()
    {
        registerPhysics();
        scenes::registerTransforms();
        world = ecs::createWorld();
    }

    ~Scene()
    {
        releasePhysics(*world);
        ecs::destroyWorld(world);
    }

    Scene(const Scene&) = delete;
    Scene& operator=(const Scene&) = delete;

    // A collider (static, alone) or a body, at a place.
    ecs::Entity add(Collider collider, Vector3 at, const RigidBody* body = nullptr, Vector3 scale = {1, 1, 1})
    {
        Vector<ecs::ComponentId> set(MemoryTag::General);
        set.pushBack(ecs::componentIdOf<Collider>);
        set.pushBack(ecs::componentIdOf<scenes::LocalTransform>);
        set.pushBack(ecs::componentIdOf<scenes::WorldTransform>);
        if (body != nullptr)
        {
            set.pushBack(ecs::componentIdOf<RigidBody>);
        }
        const ecs::Entity entity = ecs::createEntity(*world, set.span());
        *ecs::get<Collider>(*world, entity) = collider;
        *ecs::get<scenes::LocalTransform>(*world, entity) = {at, Quaternion::identity(), scale};
        if (body != nullptr)
        {
            *ecs::get<RigidBody>(*world, entity) = *body;
        }
        return entity;
    }

    void run(std::string_view name, double alpha = 0)
    {
        const int32_t index = test::systemIndex(name);
        REQUIRE(index >= 0);
        const auto function =
            reinterpret_cast<loop::SystemFunction>(loop::nativeSystemFunction(static_cast<uint32_t>(index)));
        const loop::SystemContext context{world, nullptr, frame, tickSeconds, tickSeconds, alpha, 0, 0};
        function(context, loop::nativeSystemUserData(static_cast<uint32_t>(index)));
    }

    // One fixed tick: sync, step, write back.
    void tick()
    {
        run(syncSystemName);
        run(stepSystemName);
        run(writeBackSystemName);
    }

    // A frame of `ticks` ticks, then the interpolation at `alpha`.
    void step(int ticks = 1, double alpha = 0)
    {
        ++frame;
        for (int i = 0; i < ticks; ++i)
        {
            tick();
        }
        run(interpolateSystemName, alpha);
    }

    void seconds(double time)
    {
        for (int i = static_cast<int>(time / tickSeconds + 0.5); i > 0; --i)
        {
            step();
        }
    }

    Vector3 position(ecs::Entity entity) const
    {
        return ecs::read<scenes::LocalTransform>(*world, entity)->position;
    }

    std::vector<ContactEvent> events() const
    {
        std::vector<ContactEvent> found(contacts(*world, {}));
        (void)contacts(*world, found);
        return found;
    }
};

inline Collider box(Vector3 size)
{
    return {ShapeKind::Box, size, {}, 0.6f, 0, false, {}, 0};
}

inline Collider sphere(float radius)
{
    return {ShapeKind::Sphere, {radius, 0, 0}, {}, 0.6f, 0, false, {}, 0};
}

inline RigidBody dynamic(float mass = 1)
{
    return {BodyKind::Dynamic, mass, {}, {}, 1, 1, allLayers};
}

// A character (ADR 0091): 0.4 m by 1 m between its caps, 0.3 m steps, 45
// degree slopes, falling under gravity; its feet at `at`.
inline ecs::Entity character(Scene& scene, Vector3 at, float gravityScale = 1)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<CharacterController>, ecs::componentIdOf<scenes::LocalTransform>,
                                    ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, set);
    *ecs::get<CharacterController>(*scene.world, entity) = {0.4f, 1, 0.3f, 0.785398f, 1, allLayers, gravityScale};
    *ecs::get<scenes::LocalTransform>(*scene.world, entity) = {at, Quaternion::identity(), {1, 1, 1}};
    return entity;
}

// A character's moves, one a tick, each followed by the tick (so its body
// follows, and bodies move); the last move's result.
inline CharacterMove walk(Scene& scene, ecs::Entity entity, Vector3 perMove, int moves)
{
    CharacterMove last{};
    for (int i = 0; i < moves; ++i)
    {
        last = moveCharacter(*scene.world, entity, perMove, static_cast<float>(tickSeconds));
        scene.step();
    }
    return last;
}

// Gives an entity a Joint (ADR 0092).
inline void join(Scene& scene, ecs::Entity entity, const Joint& joint)
{
    if (ecs::read<Joint>(*scene.world, entity) == nullptr)
    {
        ecs::addComponent(*scene.world, entity, ecs::componentIdOf<Joint>);
    }
    *ecs::get<Joint>(*scene.world, entity) = joint;
}

// How far a body has turned from upright: the angle between its up and the world's.
inline float tilt(Scene& scene, ecs::Entity entity)
{
    const Vector3 up = rotate(Vector3{0, 1, 0}, bodyPose(*scene.world, entity).rotation);
    return std::acos(std::clamp(up.y, -1.0f, 1.0f));
}

// The ground: a static box whose top is at y = 0.
inline ecs::Entity ground(Scene& scene)
{
    return scene.add(box({40, 1, 40}), {0, -0.5f, 0});
}

} // namespace crucible::physics::testing
