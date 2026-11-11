// Shape casts on Box3D (ADR 0090): a sphere, box, or capsule swept along a
// ray stops where it first touches; touching at its start, at distance 0.

#include "PhysicsTestSupport.h"

using namespace crucible;
using namespace crucible::physics;
using namespace crucible::physics::testing;

TEST_CASE("A swept sphere stops where it first touches")
{
    Scene scene;
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 2, 0});
    scene.step();
    const RayHit hit = shapeCast(*scene.world, {ShapeKind::Sphere, {0.5f, 0, 0}}, {0, 10, 0}, Quaternion::identity(),
                                 {0, -1, 0}, 100, defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == block.value);
    CHECK(hit.distance == doctest::Approx(7.0f).epsilon(0.01)); // its bottom on the top at 2.5
    CHECK(hit.point.y == doctest::Approx(2.5f).epsilon(0.01));
    CHECK(hit.normal.y == doctest::Approx(1.0f).epsilon(0.01));
}

TEST_CASE("A swept box and capsule are their size and turned")
{
    Scene scene;
    scene.add(box({1, 1, 1}), {0, 0, 0});
    scene.step();
    // A box 2 wide along x swept along x: its face reaches the block's at 0.5.
    const RayHit flat = shapeCast(*scene.world, {ShapeKind::Box, {2, 0.2f, 0.2f}}, {-10, 0, 0}, Quaternion::identity(),
                                  {1, 0, 0}, 100, defaultQueryFilter());
    REQUIRE(flat.hit);
    CHECK(flat.distance == doctest::Approx(10 - 0.5f - 1).epsilon(0.01));
    // Turned a quarter around y, it's 0.2 deep along x.
    const Quaternion quarter{0, std::sin(0.7853982f), 0, std::cos(0.7853982f)};
    const RayHit turned = shapeCast(*scene.world, {ShapeKind::Box, {2, 0.2f, 0.2f}}, {-10, 0, 0}, quarter, {1, 0, 0},
                                    100, defaultQueryFilter());
    REQUIRE(turned.hit);
    CHECK(turned.distance == doctest::Approx(10 - 0.5f - 0.1f).epsilon(0.01));
    // A capsule (radius 0.25, 1 between its caps) falling: its lower cap lands.
    const RayHit capsule = shapeCast(*scene.world, {ShapeKind::Capsule, {0.25f, 1, 0}}, {0, 10, 0},
                                     Quaternion::identity(), {0, -1, 0}, 100, defaultQueryFilter());
    REQUIRE(capsule.hit);
    CHECK(capsule.distance == doctest::Approx(10 - 0.5f - 0.5f - 0.25f).epsilon(0.01));
}

TEST_CASE("A cast touching at its start hits at distance 0")
{
    Scene scene;
    const ecs::Entity block = scene.add(box({1, 1, 1}), {0, 0, 0});
    scene.step();
    const RayHit hit = shapeCast(*scene.world, {ShapeKind::Sphere, {0.25f, 0, 0}}, {0, 0.6f, 0}, Quaternion::identity(),
                                 {1, 0, 0}, 5, defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == block.value);
    CHECK(hit.distance == doctest::Approx(0.0f));
}
