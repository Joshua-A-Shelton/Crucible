// Contacts (ADR 0085): a landing begins a contact with a point and normal,
// a trigger reports without stopping anything, and contacts() holds a whole
// frame's ticks, starting afresh each frame.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

bool between(const ContactEvent& event, ecs::Entity x, ecs::Entity y)
{
    return (event.a.value == x.value && event.b.value == y.value) ||
           (event.a.value == y.value && event.b.value == x.value);
}

} // namespace

TEST_CASE("A body landing begins a contact where it lands")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {2, 1, 0}, &body);
    bool landed = false;
    for (int frame = 0; frame < 120 && !landed; ++frame)
    {
        scene.step();
        for (const ContactEvent& event : scene.events())
        {
            if (event.kind == ContactKind::Begin && between(event, floor, ball))
            {
                landed = true;
                CHECK_FALSE(event.trigger);
                CHECK(event.point.x == doctest::Approx(2).epsilon(0.05));
                CHECK(std::fabs(event.point.y) < 0.1f);
                CHECK(std::fabs(event.normal.y) == doctest::Approx(1).epsilon(0.01));
            }
        }
    }
    CHECK(landed);
}

TEST_CASE("A trigger reports what passes through without stopping it")
{
    Scene scene;
    Collider zone = box({4, 1, 4});
    zone.isTrigger = true;
    const ecs::Entity trigger = scene.add(zone, {0, 3, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.25f), {0, 6, 0}, &body);
    bool entered = false, left = false;
    for (int frame = 0; frame < 120; ++frame)
    {
        scene.step();
        for (const ContactEvent& event : scene.events())
        {
            if (event.trigger && between(event, trigger, ball))
            {
                entered = entered || event.kind == ContactKind::Begin;
                left = left || event.kind == ContactKind::End;
            }
        }
    }
    CHECK(entered);
    CHECK(left);
    CHECK(scene.position(ball).y < 0);
}

TEST_CASE("Contacts hold a whole frame's ticks and start afresh each frame")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.5f), {0, 0.51f, 0}, &body); // a centimetre up
    // Four ticks in one frame: it lands within them.
    scene.step(4);
    size_t begins = 0;
    for (const ContactEvent& event : scene.events())
    {
        begins += event.kind == ContactKind::Begin && between(event, floor, ball) ? 1 : 0;
    }
    CHECK(begins == 1);
    scene.step(1);
    CHECK(scene.events().empty());
}
