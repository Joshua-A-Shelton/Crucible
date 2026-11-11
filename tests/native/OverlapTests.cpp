// Overlaps on Box3D (ADR 0090): the entities whose colliders a shape
// overlaps, each once; as many as fit, counting them all.

#include "PhysicsTestSupport.h"

#include <algorithm>

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("An overlap finds each entity it touches once")
{
    Scene scene;
    const ecs::Entity floor = ground(scene);
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.add(box({1, 1, 1}), {15, 2, 0}); // out of reach
    scene.step();
    ecs::Entity found[4]{};
    const uint32_t count = overlap(*scene.world, {ShapeKind::Box, {4, 6, 4}}, {0, 1, 0}, Quaternion::identity(),
                                   defaultQueryFilter(), found);
    REQUIRE(count == 2);
    std::vector<uint64_t> values{found[0].value, found[1].value};
    CHECK(std::ranges::count(values, floor.value) == 1);
    CHECK(std::ranges::count(values, block.value) == 1);
    CHECK(overlap(*scene.world, {ShapeKind::Sphere, {0.5f, 0, 0}}, {0, 10, 0}, Quaternion::identity(),
                  defaultQueryFilter(), found) == 0);
}

TEST_CASE("An overlap counts past what fits")
{
    Scene scene;
    for (int i = 0; i < 5; ++i)
    {
        scene.add(sphere(0.4f), {static_cast<float>(i), 0, 0});
    }
    scene.step();
    ecs::Entity found[2]{};
    CHECK(overlap(*scene.world, {ShapeKind::Capsule, {1, 8, 0}}, {2, 0, 0}, Quaternion{0, 0, 0.7071068f, 0.7071068f},
                  defaultQueryFilter(), found) == 5);
    CHECK(found[0].value != 0);
    CHECK(found[1].value != 0);
}
