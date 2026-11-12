// Characters on uneven ground (ADR 0091): over steps lower than their step
// height, blocked by higher ones, up walkable slopes but not steep ones, and
// down stairs on the ground all the way.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

// A ramp rising along +x at `degrees`, its surface through the origin.
ecs::Entity ramp(Scene& scene, float degrees)
{
    const float angle = degrees * 3.14159265f / 180;
    const Vector3 center{std::cos(angle) * 5 + std::sin(angle) * 0.5f, std::sin(angle) * 5 - std::cos(angle) * 0.5f, 0};
    const ecs::Entity entity = scene.add(box({10, 1, 4}), center);
    ecs::get<scenes::LocalTransform>(*scene.world, entity)->rotation = Quaternion::fromAxisAngle({0, 0, 1}, angle);
    return entity;
}

} // namespace

TEST_CASE("A character climbs a step lower than its step height")
{
    Scene scene;
    ground(scene);
    scene.add(box({4, 0.2f, 4}), {3, 0.1f, 0}); // a 0.2 m step from x = 1
    const ecs::Entity walker = character(scene, {0, 0, 0});
    scene.step();
    const CharacterMove moved = walk(scene, walker, {0.05f, 0, 0}, 60);
    CHECK(moved.position.x > 2.5f);
    CHECK(moved.position.y == doctest::Approx(0.2f).epsilon(0.02));
    CHECK(moved.grounded);
}

TEST_CASE("Step Smoothing eases a character up onto a step")
{
    Scene scene;
    ground(scene);
    scene.add(box({4, 0.2f, 4}), {3, 0.1f, 0}); // a 0.2 m step from x = 1
    const ecs::Entity walker = character(scene, {0.7f, 0, 0});
    ecs::get<CharacterController>(*scene.world, walker)->stepSmoothing = 0.1f;
    scene.step();
    walk(scene, walker, {}, 2);
    // Onto the step: the feet start rising, but aren't there yet...
    float first = 0;
    for (int i = 0; i < 20 && first == 0; ++i)
    {
        first = walk(scene, walker, {0.05f, 0, 0}, 1).position.y;
    }
    CHECK(first > 0.01f);
    CHECK(first < 0.12f);
    // ...walking on, it closes on the step's top over about 0.1 s, never
    // snapping, and is there soon after.
    float previous = first, biggest = 0;
    CharacterMove later{};
    for (int i = 0; i < 12; ++i)
    {
        later = walk(scene, walker, {0.05f, 0, 0}, 1);
        biggest = std::max(biggest, later.position.y - previous);
        previous = later.position.y;
    }
    CHECK(later.grounded);
    CHECK(later.position.y == doctest::Approx(0.2f).epsilon(0.02));
    CHECK(biggest < 0.1f);
}

TEST_CASE("A step higher than its step height blocks it")
{
    Scene scene;
    ground(scene);
    scene.add(box({4, 0.5f, 4}), {3, 0.25f, 0}); // 0.5 m, from x = 1
    const ecs::Entity walker = character(scene, {0, 0, 0});
    scene.step();
    const CharacterMove moved = walk(scene, walker, {0.05f, 0, 0}, 60);
    // Riding 0.3 m up, its bottom cap (center 0.7 m up) meets the step's
    // top corner (0.5 m up): stopped sqrt(0.4^2 - 0.2^2) short of it.
    CHECK(moved.position.x == doctest::Approx(1 - std::sqrt(0.16f - 0.04f)).epsilon(0.02));
    CHECK(moved.position.y == doctest::Approx(0).epsilon(0.02));
}

TEST_CASE("A character walks up a walkable slope but not a steep one")
{
    {
        Scene scene;
        ground(scene);
        ramp(scene, 30);
        const ecs::Entity walker = character(scene, {-1, 0, 0});
        scene.step();
        const CharacterMove moved = walk(scene, walker, {0.05f, 0, 0}, 80);
        CHECK(moved.grounded);
        CHECK(moved.position.y > 1.0f); // up the 30 degree ramp
        CHECK(moved.groundNormal.y == doctest::Approx(std::cos(0.5235988f)).epsilon(0.02));
    }
    {
        Scene scene;
        ground(scene);
        ramp(scene, 60);
        const ecs::Entity walker = character(scene, {-1, 0, 0});
        scene.step();
        const CharacterMove moved = walk(scene, walker, {0.05f, 0, 0}, 80);
        CHECK(moved.position.y < 0.35f); // the 60 degree one is a wall
        CHECK(moved.position.x < 0.5f);
    }
}

TEST_CASE("A character follows the ground down stairs")
{
    Scene scene;
    ground(scene);
    for (int i = 0; i < 3; ++i)
    {
        // Steps down along +x: tops at 0.6, 0.4, 0.2.
        const float top = 0.6f - 0.2f * static_cast<float>(i);
        scene.add(box({1, top, 4}), {static_cast<float>(i), top * 0.5f, 0});
    }
    const ecs::Entity walker = character(scene, {0, 0.6f, 0});
    scene.step();
    walk(scene, walker, {}, 2); // settle
    bool grounded = true;
    for (int i = 0; i < 70; ++i)
    {
        const bool standing = walk(scene, walker, {0.05f, 0, 0}, 1).grounded;
        grounded = grounded && standing;
    }
    CHECK(grounded); // never fell
    CHECK(scene.position(walker).y == doctest::Approx(0).epsilon(0.02));
}
