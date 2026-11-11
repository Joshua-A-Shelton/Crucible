// Raycasts on Box3D (ADR 0090): the first collider along a ray, where and
// how far; none past its distance; not the collider it starts in; and the
// last tick's world, not this frame's edits.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A ray hits the first collider along it")
{
    Scene scene;
    ground(scene);
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    const RayHit hit = raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == block.value);
    CHECK(hit.point.y == doctest::Approx(2.5f).epsilon(0.001));
    CHECK(hit.normal.y == doctest::Approx(1.0f).epsilon(0.001));
    CHECK(hit.distance == doctest::Approx(7.5f).epsilon(0.001));
    CHECK_FALSE(raycast(*scene.world, {0, 10, 0}, {0, 1, 0}, 100, defaultQueryFilter()).hit); // up: nothing
}

TEST_CASE("A ray stops at its distance")
{
    Scene scene;
    scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    CHECK_FALSE(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 7, defaultQueryFilter()).hit);
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 8, defaultQueryFilter()).hit);
}

TEST_CASE("A ray starting inside a collider goes on to the next")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    const RayHit hit = raycast(*scene.world, {0, 2, 0}, {0, -1, 0}, 100, defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == floor.value);
    CHECK(hit.distance == doctest::Approx(2.0f).epsilon(0.001));
}

TEST_CASE("Queries see the last tick's world")
{
    Scene scene;
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    // Moved by the game this frame: still where it was until the next sync.
    ecs::get<scenes::LocalTransform>(*scene.world, block)->position = {5, 2, 0};
    CHECK(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, defaultQueryFilter()).hit);
    scene.step();
    CHECK_FALSE(raycast(*scene.world, {0, 10, 0}, {0, -1, 0}, 100, defaultQueryFilter()).hit);
    CHECK(raycast(*scene.world, {5, 10, 0}, {0, -1, 0}, 100, defaultQueryFilter()).hit);
}
