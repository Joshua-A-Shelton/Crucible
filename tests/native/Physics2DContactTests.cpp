// 2D contact events on Box2D (ADR 0093), as 3D's: begin and end over the
// frame's ticks, and triggers.

#include "Physics2DTestSupport.h"

#include <algorithm>

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

TEST_CASE("A 2D ball landing begins a contact with the ground")
{
    Scene2D scene;
    const ecs::Entity floor = ground2D(scene);
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0, 1}, &body);
    bool began = false;
    for (int i = 0; i < 90 && !began; ++i)
    {
        scene.step();
        for (const ContactEvent2D& event : scene.events())
        {
            const bool pair = (event.a.value == ball.value && event.b.value == floor.value) ||
                              (event.a.value == floor.value && event.b.value == ball.value);
            if (pair && event.kind == physics::ContactKind::Begin && !event.trigger)
            {
                began = true;
                CHECK(event.point.y == doctest::Approx(0).epsilon(0.05));
            }
        }
    }
    CHECK(began);
}

TEST_CASE("A 2D body passing through a trigger begins and ends a trigger contact")
{
    Scene2D scene;
    Collider2D zone = box2D({2, 1});
    zone.isTrigger = true;
    const ecs::Entity trigger = scene.add(zone, {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.2f), {0, 3}, &body);
    bool began = false, ended = false;
    for (int i = 0; i < 120; ++i)
    {
        scene.step();
        for (const ContactEvent2D& event : scene.events())
        {
            if (event.trigger && event.a.value == trigger.value && event.b.value == ball.value)
            {
                began = began || event.kind == physics::ContactKind::Begin;
                ended = ended || event.kind == physics::ContactKind::End;
            }
        }
    }
    CHECK(began);
    CHECK(ended);
}
