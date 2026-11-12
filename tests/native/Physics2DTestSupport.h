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

// The ground: a static box whose top is at y = 0.
inline ecs::Entity ground2D(Scene2D& scene)
{
    return scene.add(box2D({40, 1}), {0, -0.5f});
}

} // namespace crucible::physics2d::testing
