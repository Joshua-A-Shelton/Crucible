#pragma once

// What the Box2D tests share (ADR 0093): a world of 2D bodies, run tick by
// tick and frame by frame as the loop runs the 2D physics systems.

#include "SubsystemTestSupport.h"

#include <crucible/Physics2D.h>
#include <crucible/Transform.h>

#include <doctest/doctest.h>

#include <cmath>
#include <vector>

namespace crucible::physics2d::testing {

constexpr double tickSeconds = 1.0 / 60;

struct Scene2D
{
    ecs::World* world = nullptr;
    uint64_t frame = 1;

    Scene2D()
    {
        registerPhysics2D();
        scenes::registerTransforms();
        world = ecs::createWorld();
    }

    ~Scene2D()
    {
        releasePhysics2D(*world);
        ecs::destroyWorld(world);
    }

    Scene2D(const Scene2D&) = delete;
    Scene2D& operator=(const Scene2D&) = delete;

    // A collider (static, alone) or a body, at a place.
    ecs::Entity add(Collider2D collider, Vector2 at, const RigidBody2D* body = nullptr, float angle = 0)
    {
        const ecs::ComponentId plain[] = {ecs::componentIdOf<Collider2D>, ecs::componentIdOf<scenes::Transform2D>,
                                          ecs::componentIdOf<scenes::WorldTransform>};
        const ecs::ComponentId bodied[] = {ecs::componentIdOf<Collider2D>, ecs::componentIdOf<scenes::Transform2D>,
                                           ecs::componentIdOf<scenes::WorldTransform>, ecs::componentIdOf<RigidBody2D>};
        const ecs::Entity entity =
            body != nullptr ? ecs::createEntity(*world, bodied) : ecs::createEntity(*world, plain);
        *ecs::get<Collider2D>(*world, entity) = collider;
        *ecs::get<scenes::Transform2D>(*world, entity) = {at, angle, {1, 1}, 0};
        if (body != nullptr)
        {
            *ecs::get<RigidBody2D>(*world, entity) = *body;
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

    void tick()
    {
        run(syncSystemName);
        run(stepSystemName);
        run(writeBackSystemName);
    }

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

    const scenes::Transform2D& transform(ecs::Entity entity) const
    {
        return *ecs::read<scenes::Transform2D>(*world, entity);
    }

    std::vector<ContactEvent2D> events() const
    {
        std::vector<ContactEvent2D> found(contacts(*world, {}));
        (void)contacts(*world, found);
        return found;
    }
};

inline Collider2D box2D(Vector2 size)
{
    Collider2D collider{};
    collider.shape = ShapeKind2D::Box;
    collider.size = size;
    collider.friction = 0.6f;
    return collider;
}

inline Collider2D circle2D(float radius)
{
    Collider2D collider{};
    collider.shape = ShapeKind2D::Circle;
    collider.size = {radius, 0};
    collider.friction = 0.6f;
    return collider;
}

inline RigidBody2D dynamic2D(float mass = 1)
{
    return {physics::BodyKind::Dynamic, mass, {}, 0, 1, 1, physics::allLayers};
}

inline void join2D(Scene2D& scene, ecs::Entity entity, const Joint2D& joint)
{
    if (ecs::read<Joint2D>(*scene.world, entity) == nullptr)
    {
        ecs::addComponent(*scene.world, entity, ecs::componentIdOf<Joint2D>);
    }
    *ecs::get<Joint2D>(*scene.world, entity) = joint;
}

// A 2D character: 0.4 m by 1 m between its caps, 0.3 m steps, 45 degree
// slopes, falling under gravity; its feet at `at`.
inline ecs::Entity character2D(Scene2D& scene, Vector2 at, float gravityScale = 1)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<CharacterController2D>, ecs::componentIdOf<scenes::Transform2D>,
                                    ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*scene.world, set);
    *ecs::get<CharacterController2D>(*scene.world, entity) = {0.4f,         1, 0.3f, 0.785398f, 1, physics::allLayers,
                                                              gravityScale, 0};
    *ecs::get<scenes::Transform2D>(*scene.world, entity) = {at, 0, {1, 1}, 0};
    return entity;
}

// A character's moves, one a tick, each followed by the tick.
inline CharacterMove2D walk2D(Scene2D& scene, ecs::Entity entity, Vector2 perMove, int moves)
{
    CharacterMove2D last{};
    for (int i = 0; i < moves; ++i)
    {
        last = moveCharacter2D(*scene.world, entity, perMove, static_cast<float>(tickSeconds));
        scene.step();
    }
    return last;
}

// The ground: a static box whose top is at y = 0.
inline ecs::Entity ground2D(Scene2D& scene)
{
    return scene.add(box2D({40, 1}), {0, -0.5f});
}

} // namespace crucible::physics2d::testing
